using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SSMP.Logging;
using SSMP.Networking.Matchmaking;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.HolePunch;

/// <summary>
/// UDP Hole Punch implementation of <see cref="IEncryptedTransportServer"/>.
/// Handles hole punch coordination for incoming client connections.
/// </summary>
internal class HolePunchEncryptedTransportServer : IEncryptedTransportServer {
    /// <summary>
    /// Number of punch packets to send per client.
    /// Set to 100 iterations at <see cref="PunchPacketDelayMs"/> ms each (5 s total)
    /// to maximise the probability of establishing a NAT mapping before the client connects.
    /// </summary>
    private const int PunchPacketCount = 100;

    /// <summary>
    /// Delay between punch packets in milliseconds.
    /// </summary>
    private const int PunchPacketDelayMs = 50;

    /// <summary>
    /// Pre-allocated punch packet payload (<c>"PUNCH"</c> encoded as UTF-8).
    /// Allocated once at class initialisation and reused for every send to avoid
    /// per-packet heap allocations on the hot punch path.
    /// </summary>
    private static readonly byte[] PunchPacket = "PUNCH"u8.ToArray();

    /// <summary>
    /// Fallback handoff point for callers that cannot pass the socket through the constructor
    /// (e.g. when <see cref="Start"/> is triggered via an event). Consumed and cleared on
    /// <see cref="Start"/> to prevent accidental reuse across sessions.
    /// Prefer the constructor parameter where the call chain allows it.
    /// </summary>
    public static Socket? PreBoundSocket { get; set; }

    /// <summary>
    /// The underlying DTLS server.
    /// </summary>
    private readonly DtlsServer _dtlsServer;

    /// <summary>
    /// Dictionary containing the clients of this server.
    /// </summary>
    private readonly ConcurrentDictionary<IPEndPoint, HolePunchEncryptedTransportClient> _clients;

    /// <summary>
    /// MMS client instance for lobby management.
    /// Stored to enable proper cleanup when server shuts down.
    /// </summary>
    private readonly MmsClient? _mmsClient;

    /// <summary>
    /// Optional pre-bound socket supplied at construction time.
    /// Takes priority over <see cref="PreBoundSocket"/> in <see cref="Start"/>.
    /// </summary>
    private readonly Socket? _preBoundSocket;

    /// <summary>
    /// Cancels all in-flight <see cref="PunchToClientAsync"/> tasks when
    /// <see cref="Stop"/> is called, ensuring no punch packets are sent after
    /// the underlying socket has been closed.
    /// </summary>
    private CancellationTokenSource? _punchCts;

    /// <inheritdoc />
    public event Action<IEncryptedTransportClient>? ClientConnectedEvent;

    /// <summary>
    /// Initialises a new <see cref="HolePunchEncryptedTransportServer"/>.
    /// </summary>
    /// <param name="mmsClient">
    /// MMS client to subscribe to for punch notifications.
    /// Pass <see langword="null"/> if MMS is not in use.
    /// </param>
    /// <param name="preBoundSocket">
    /// UDP socket already bound during STUN/UDP discovery.
    /// Pass <see langword="null"/> to fall back to <see cref="PreBoundSocket"/>, or to let
    /// the server bind a fresh socket (NAT traversal may be unreliable in that case).
    /// </param>
    public HolePunchEncryptedTransportServer(MmsClient? mmsClient = null, Socket? preBoundSocket = null) {
        _mmsClient = mmsClient;
        _preBoundSocket = preBoundSocket;
        _dtlsServer = new DtlsServer();
        _clients = new ConcurrentDictionary<IPEndPoint, HolePunchEncryptedTransportClient>();
        _dtlsServer.DataReceivedEvent += OnClientDataReceived;
    }

    /// <summary>
    /// Starts the server on the given port, reusing a pre-bound socket where available.
    /// Subscribes to <see cref="MmsClient.PunchClientRequested"/> if an
    /// <see cref="MmsClient"/> was provided at construction time.
    /// </summary>
    /// <remarks>
    /// The constructor-supplied socket takes priority over <see cref="PreBoundSocket"/>.
    /// <see cref="PreBoundSocket"/> is cleared immediately after being consumed to prevent
    /// accidental reuse on a subsequent <see cref="Start"/> call.
    /// </remarks>
    /// <param name="port">UDP port to listen on when no pre-bound socket is available.</param>
    public void Start(int port) {
        Logger.Info($"HolePunch Server: Starting on port {port}");

        // Constructor parameter takes priority; static property is the fallback for callers
        // that set it before firing the event that constructs and starts the server.
        var socket = _preBoundSocket ?? PreBoundSocket;
        PreBoundSocket = null; // consume immediately — prevents reuse on a second Start()

        _punchCts = new CancellationTokenSource();

        if (_mmsClient is not null)
            _mmsClient.PunchClientRequested += OnPunchClientRequested;
        else
            Logger.Warn("HolePunch Server: No MmsClient provided — push-based punch coordination disabled");

        if (socket is not null) {
            Logger.Info("HolePunch Server: Reusing pre-bound socket from STUN discovery");
            _dtlsServer.Start(socket);
        } else {
            Logger.Warn(
                "HolePunch Server: No pre-bound socket — binding a new socket (NAT traversal may be unreliable)"
            );
            _dtlsServer.Start(port);
        }
    }

    /// <summary>
    /// Stops the server, cancels all in-flight punch tasks, closes the MMS lobby,
    /// disconnects all tracked clients, and shuts down the underlying DTLS server.
    /// </summary>
    public void Stop() {
        Logger.Info("HolePunch Server: Stopping");

        // Cancel all in-flight punch tasks before closing the socket so we don't
        // send packets on a socket that may already be disposed.
        _punchCts?.Cancel();
        _punchCts?.Dispose();
        _punchCts = null;

        if (_mmsClient is not null) {
            _mmsClient.PunchClientRequested -= OnPunchClientRequested;
            _mmsClient.CloseLobby();
        }

        // Disconnect tracked clients before clearing the dictionary
        foreach (var client in _clients.Values)
            _dtlsServer.DisconnectClient(client.EndPoint);

        _clients.Clear();
        _dtlsServer.Stop();
    }

    /// <inheritdoc />
    public void DisconnectClient(IEncryptedTransportClient client) {
        if (client is not HolePunchEncryptedTransportClient hpClient)
            return;

        _dtlsServer.DisconnectClient(hpClient.EndPoint);
        _clients.TryRemove(hpClient.EndPoint, out _);
    }

    /// <summary>
    /// Invoked by <see cref="MmsClient.PunchClientRequested"/> when the MMS server pushes
    /// a new client endpoint. Validates the IP and starts a fire-and-forget punch sequence.
    /// </summary>
    /// <param name="clientIp">Client's public IP address string as received from MMS.</param>
    /// <param name="clientPort">Client's public port as received from MMS.</param>
    private void OnPunchClientRequested(string clientIp, int clientPort) {
        if (!IPAddress.TryParse(clientIp, out var ip)) {
            Logger.Warn($"HolePunch Server: Invalid client IP in punch request: '{clientIp}'");
            return;
        }

        // Fire-and-forget — PunchToClientAsync manages its own lifetime via _punchCts
        _ = PunchToClientAsync(new IPEndPoint(ip, clientPort));
    }

    /// <summary>
    /// Sends <see cref="PunchPacketCount"/> UDP punch packets to <paramref name="clientEndpoint"/>
    /// at <see cref="PunchPacketDelayMs"/> ms intervals to establish a NAT mapping.
    /// Cancels cleanly when the server stops via <see cref="_punchCts"/>.
    /// </summary>
    /// <remarks>
    /// Cancellation is driven entirely by <see cref="Task.Delay(int, CancellationToken)"/>;
    /// no explicit <see cref="CancellationToken.ThrowIfCancellationRequested"/> call is needed
    /// inside the loop because the delay already throws <see cref="OperationCanceledException"/>
    /// as soon as the token is signalled.
    /// </remarks>
    /// <param name="clientEndpoint">The remote endpoint to punch towards.</param>
    /// <exception cref="OperationCanceledException">
    /// Caught internally; logged at Debug level and swallowed so the fire-and-forget
    /// caller is not affected.
    /// </exception>
    /// <exception cref="Exception">
    /// Any other exception is caught internally, logged at Warn level, and swallowed.
    /// </exception>
    private async Task PunchToClientAsync(IPEndPoint clientEndpoint) {
        var ct = _punchCts?.Token ?? CancellationToken.None;

        Logger.Debug($"HolePunch Server: Starting punch sequence to {clientEndpoint}");

        try {
            for (var i = 0; i < PunchPacketCount; i++) {
                _dtlsServer.SendRaw(PunchPacket, clientEndpoint);
                await Task.Delay(PunchPacketDelayMs, ct);
            }

            Logger.Info($"HolePunch Server: Punch sequence complete for {clientEndpoint}");
        } catch (OperationCanceledException) {
            Logger.Debug($"HolePunch Server: Punch sequence cancelled for {clientEndpoint}");
        } catch (Exception ex) {
            Logger.Warn($"HolePunch Server: Punch sequence failed for {clientEndpoint}: {ex.Message}");
        }
    }

    /// <summary>
    /// Invoked by the DTLS server when data arrives from a remote endpoint.
    /// Registers new clients on first contact and routes data to existing ones.
    /// </summary>
    /// <remarks>
    /// The <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey, Func{TKey, TValue})"/>
    /// factory may execute more than once under contention and must therefore remain
    /// side-effect free. <see cref="ClientConnectedEvent"/> is raised outside
    /// <c>GetOrAdd</c> to guarantee exactly one invocation per unique endpoint.
    /// </remarks>
    /// <param name="dtlsClient">The DTLS client descriptor supplied by the underlying server.</param>
    /// <param name="data">Buffer containing the received data.</param>
    /// <param name="length">Number of valid bytes in <paramref name="data"/>.</param>
    private void OnClientDataReceived(DtlsServerClient dtlsClient, byte[] data, int length) {
        var isNew = false;
        var client = _clients.GetOrAdd(
            dtlsClient.EndPoint, _ => {
                isNew = true;
                return new HolePunchEncryptedTransportClient(dtlsClient);
            }
        );
        if (isNew) ClientConnectedEvent?.Invoke(client);
        client.RaiseDataReceived(data, length);
    }
}

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
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
    /// Increased to 100 (5s) to ensure reliability.
    /// </summary>
    private const int PunchPacketCount = 100;

    /// <summary>
    /// Delay between punch packets in milliseconds.
    /// </summary>
    private const int PunchPacketDelayMs = 50;

    /// <summary>
    /// Pre-allocated punch packet bytes ("PUNCH").
    /// </summary>
    private static readonly byte[] PunchPacket = "PUNCH"u8.ToArray();

    /// <summary>
    /// MMS client instance for lobby management.
    /// Stored to enable proper cleanup when server shuts down.
    /// </summary>
    private MmsClient? _mmsClient;

    /// <summary>
    /// The underlying DTLS server.
    /// </summary>
    private readonly DtlsServer _dtlsServer;

    /// <summary>
    /// Pre-bound socket for NAT hole-punching.
    /// Created by ConnectInterface during lobby creation, consumed by HolePunchEncryptedTransportServer.
    /// </summary>
    public static Socket? PreBoundSocket { get; set; }

    /// <summary>
    /// Dictionary containing the clients of this server.
    /// </summary>
    private readonly ConcurrentDictionary<IPEndPoint, HolePunchEncryptedTransportClient> _clients;

    /// <inheritdoc />
    public event Action<IEncryptedTransportClient>? ClientConnectedEvent;

    public HolePunchEncryptedTransportServer(MmsClient? mmsClient = null) {
        _mmsClient = mmsClient;
        _dtlsServer = new DtlsServer();
        _clients = new ConcurrentDictionary<IPEndPoint, HolePunchEncryptedTransportClient>();
        _dtlsServer.DataReceivedEvent += OnClientDataReceived;
    }

    /// <inheritdoc />
    public void Start(int port) {
        Logger.Info($"HolePunch Server: Starting on port {port}");

        if (_mmsClient != null) {
            _mmsClient.RefreshHostMappingRequested += OnHostMappingRefreshRequested;
            _mmsClient.HostMappingReceived += OnHostMappingReceived;
            _mmsClient.StartPunchRequested += OnStartPunchRequested;
        }
        
        var socket = PreBoundSocket;
        PreBoundSocket = null;
        
        _dtlsServer.Start(port, socket);

        _mmsClient?.StartPendingClientPolling();
    }

    /// <inheritdoc />
    public void Stop() {
        Logger.Info("HolePunch Server: Stopping");

        if (_mmsClient != null) {
            _mmsClient.RefreshHostMappingRequested -= OnHostMappingRefreshRequested;
            _mmsClient.HostMappingReceived -= OnHostMappingReceived;
            _mmsClient.StartPunchRequested -= OnStartPunchRequested;
            _mmsClient.CloseLobby();
            _mmsClient = null;
        }

        _dtlsServer.Stop();
        _clients.Clear();
    }

    /// <summary>
    /// Called when MMS notifies us of a client needing punch-back.
    /// </summary>
    private void OnStartPunchRequested(string joinId, string clientIp, int clientPort, int hostPort, long startTimeMs) {
        _mmsClient?.StopHostDiscoveryRefresh();
        if (!IPAddress.TryParse(clientIp, out var ip)) {
            Logger.Warn($"HolePunch Server: Invalid client IP: {clientIp}");
            return;
        }

        _ = PunchToClientAsync(new IPEndPoint(ip, clientPort), startTimeMs);
    }

    /// <inheritdoc />
    public void DisconnectClient(IEncryptedTransportClient client) {
        if (client is not HolePunchEncryptedTransportClient hpClient)
            return;
        
        _dtlsServer.DisconnectClient(hpClient.EndPoint);
        _clients.TryRemove(hpClient.EndPoint, out _);
    }

    /// <summary>
    /// Callback method for when data is received from a server client.
    /// </summary>
    private void OnClientDataReceived(DtlsServerClient dtlsClient, byte[] data, int length) {
        var client = _clients.GetOrAdd(dtlsClient.EndPoint, _ => {
            var newClient = new HolePunchEncryptedTransportClient(dtlsClient);
            ClientConnectedEvent?.Invoke(newClient);
            return newClient;
        });

        client.RaiseDataReceived(data, length);
    }
    
    /// <summary>
    /// Called when MMS asks the host to refresh its matchmaking mapping on the live UDP server socket.
    /// </summary>
    private void OnHostMappingRefreshRequested(string joinId, string hostDiscoveryToken, long serverTimeMs) {
        Logger.Info($"HolePunch Server: Refreshing host mapping for join {joinId}");
        _mmsClient?.StartHostDiscoveryRefresh(hostDiscoveryToken, (data, endpoint) => _dtlsServer.SendRaw(data, endpoint));
    }

    /// <summary>
    /// Called when MMS confirms it has learned the host's current external mapping.
    /// </summary>
    private void OnHostMappingReceived() {
        Logger.Info("HolePunch Server: Host mapping learned by MMS, stopping refresh");
        _mmsClient?.StopHostDiscoveryRefresh();
    }

    /// <summary>
    /// Sends <see cref="PunchPacketCount"/> UDP packets to <paramref name="clientEndpoint"/>,
    /// spaced <see cref="PunchPacketDelayMs"/> ms apart, starting at <paramref name="startTimeMs"/>.
    /// Exceptions are caught and logged rather than propagated, since this runs fire-and-forget.
    /// </summary>
    private async Task PunchToClientAsync(IPEndPoint clientEndpoint, long startTimeMs)
    {
        try
        {
            Logger.Debug($"HolePunch Server: Punching to client at {clientEndpoint}");
            var delay = startTimeMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (delay > 0) await Task.Delay(TimeSpan.FromMilliseconds(delay));

            for (var i = 0; i < PunchPacketCount; i++)
            {
                _dtlsServer.SendRaw(PunchPacket, clientEndpoint);
                await Task.Delay(PunchPacketDelayMs);
            }

            Logger.Info($"HolePunch Server: Punch complete to {clientEndpoint}");
        }
        catch (Exception ex) { Logger.Error($"HolePunch Server: Punch to {clientEndpoint} failed – {ex.Message}"); }
    }
}

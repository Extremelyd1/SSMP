using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using SSMP.Game;
using SSMP.Logging;
using SSMP.Networking.Transport.Common;
using Steamworks;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of <see cref="IEncryptedTransportServer"/>.
/// Manages multiple client connections via Steam P2P networking.
/// Optimized for maximum performance with minimal allocations.
/// </summary>
internal sealed class SteamEncryptedTransportServer : IEncryptedTransportServer {
    /// <summary>
    /// Maximum Steam P2P packet size.
    /// </summary>
    private const int MaxPacketSize = 1200;

    /// <summary>
    /// Polling interval in milliseconds for Steam P2P packet receive loop.
    /// 17.2ms achieves ~58Hz polling rate to balance responsiveness and CPU usage.
    /// </summary>
    private const double PollIntervalMS = 17.2;

    /// <summary>
    /// Maximum wait time threshold before switching from spin to sleep (ms).
    /// </summary>
    private const int SpinWaitThreshold = 15;

    /// <summary>
    /// Maximum drift correction threshold in milliseconds.
    /// </summary>
    private const long MaxDriftThreshold = 100;

    /// <inheritdoc />
    public event Action<IEncryptedTransportClient>? ClientConnectedEvent;

    /// <summary>
    /// Connected clients indexed by Steam ID.
    /// Uses optimal concurrency level for typical game server scenarios.
    /// </summary>
    private readonly ConcurrentDictionary<CSteamID, SteamEncryptedTransportClient> _clients = 
        new(Environment.ProcessorCount, 64);

    /// <summary>
    /// Buffer for receiving P2P packets (aligned for better cache performance).
    /// </summary>
    private readonly byte[] _receiveBuffer = new byte[MaxPacketSize];

    /// <summary>
    /// Whether the server is currently running.
    /// </summary>
    private volatile bool _isRunning;

    /// <summary>
    /// Cached flag for Steam initialization state.
    /// </summary>
    private volatile bool _steamInitialized;

    /// <summary>
    /// Callback for P2P session requests.
    /// </summary>
    private Callback<P2PSessionRequest_t>? _sessionRequestCallback;

    /// <summary>
    /// Token source for cancelling the receive loop.
    /// </summary>
    private CancellationTokenSource? _receiveTokenSource;

    /// <summary>
    /// Thread for receiving P2P packets.
    /// </summary>
    private Thread? _receiveThread;

    /// <summary>
    /// Cached loopback channel reference.
    /// </summary>
    private SteamLoopbackChannel? _cachedLoopbackChannel;

    /// <summary>
    /// Start listening for Steam P2P connections.
    /// </summary>
    /// <param name="port">Port parameter (unused for Steam P2P)</param>
    /// <exception cref="InvalidOperationException">Thrown if Steam is not initialized.</exception>
    public void Start(int port) {
        _steamInitialized = SteamManager.IsInitialized;

        if (!_steamInitialized) {
            throw new InvalidOperationException("Cannot start Steam P2P server: Steam is not initialized");
        }

        if (_isRunning) {
            Logger.Warn("Steam P2P server already running");
            return;
        }

        _isRunning = true;

        _sessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);

        SteamNetworking.AllowP2PPacketRelay(true);

        Logger.Info("Steam P2P: Server started, listening for connections");

        _cachedLoopbackChannel = SteamLoopbackChannel.GetOrCreate();
        _cachedLoopbackChannel.RegisterServer(this);

        _receiveTokenSource = new CancellationTokenSource();
        _receiveThread = new Thread(ReceiveLoop) { 
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "Steam P2P Server Receive"
        };
        _receiveThread.Start();
    }

    /// <inheritdoc />
    public void Stop() {
        if (!_isRunning) return;

        Logger.Info("Steam P2P: Stopping server");

        // Signal shutdown first
        _isRunning = false;
        _receiveTokenSource?.Cancel();

        if (_receiveThread != null) {
            if (!_receiveThread.Join(5000)) {
                Logger.Warn("Steam P2P Server: Receive thread did not terminate within 5 seconds");
            }

            _receiveThread = null;
        }

        _receiveTokenSource?.Dispose();
        _receiveTokenSource = null;

        // Disconnect all clients
        foreach (var client in _clients.Values) {
            DisconnectClientInternal(client);
        }
        _clients.Clear();

        // Cleanup loopback
        if (_cachedLoopbackChannel != null) {
            _cachedLoopbackChannel.UnregisterServer();
            SteamLoopbackChannel.ReleaseIfEmpty();
            _cachedLoopbackChannel = null;
        }

        _sessionRequestCallback?.Dispose();
        _sessionRequestCallback = null;

        Logger.Info("Steam P2P: Server stopped");
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DisconnectClient(IEncryptedTransportClient client) {
        if (client is SteamEncryptedTransportClient steamClient) {
            DisconnectClientInternal(steamClient);
        }
    }

    /// <summary>
    /// Internal disconnect logic to avoid interface cast overhead in hot paths.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DisconnectClientInternal(SteamEncryptedTransportClient steamClient) {
        var steamId = new CSteamID(steamClient.SteamId);
        if (!_clients.TryRemove(steamId, out _)) return;

        if (_steamInitialized && SteamManager.IsInitialized) {
            SteamNetworking.CloseP2PSessionWithUser(steamId);
        }

        Logger.Info($"Steam P2P: Disconnected client {steamId}");
    }

    /// <summary>
    /// Callback handler for P2P session requests.
    /// Automatically accepts all requests and creates client connections.
    /// </summary>
    private void OnP2PSessionRequest(P2PSessionRequest_t request) {
        if (!_isRunning) return;

        var remoteSteamId = request.m_steamIDRemote;
        Logger.Info($"Steam P2P: Received session request from {remoteSteamId}");

        if (!SteamNetworking.AcceptP2PSessionWithUser(remoteSteamId)) {
            Logger.Warn($"Steam P2P: Failed to accept session from {remoteSteamId}");
            return;
        }

        // Fast path: check if already connected before creating new client
        if (_clients.ContainsKey(remoteSteamId)) return;

        var client = new SteamEncryptedTransportClient(remoteSteamId.m_SteamID);
        
        // Use TryAdd to handle race conditions
        if (_clients.TryAdd(remoteSteamId, client)) {
            Logger.Info($"Steam P2P: New client connected: {remoteSteamId}");
            ClientConnectedEvent?.Invoke(client);
        }
    }

    /// <summary>
    /// Continuously polls for incoming P2P packets with precise timing.
    /// Uses Stopwatch + hybrid spin/sleep for consistent ~58Hz polling rate.
    /// Steam API limitation: no blocking receive or callback available for server-side, must poll.
    /// </summary>
    private void ReceiveLoop() {
        var token = _receiveTokenSource;
        if (token == null) return;

        var cancellationToken = token.Token;
        var stopwatch = Stopwatch.StartNew();
        var nextPollTime = stopwatch.ElapsedMilliseconds;
        const long pollInterval = (long)PollIntervalMS;

        // Preallocate SpinWait struct to avoid per-iteration allocation
        var spinWait = new SpinWait();

        while (_isRunning) {
            try {
                // Fast cancellation check without allocation
                if (cancellationToken.IsCancellationRequested) break;

                // Exit cleanly if Steam shuts down (e.g., during forceful game closure)
                if (!SteamManager.IsInitialized) {
                    _steamInitialized = false;
                    Logger.Info("Steam P2P Server: Steam shut down, exiting receive loop");
                    break;
                }

                // Precise wait until next poll time
                var currentTime = stopwatch.ElapsedMilliseconds;
                var waitTime = nextPollTime - currentTime;

                if (waitTime > 0) {
                    if (waitTime > SpinWaitThreshold) {
                        // Sleep for most of the wait, then spin for precision
                        var sleepTime = (int)(waitTime - 2);
                        if (sleepTime > 0) {
                            Thread.Sleep(sleepTime);
                        }
                    }

                    // Spin for remaining time (high precision, low overhead)
                    spinWait.Reset();
                    while (stopwatch.ElapsedMilliseconds < nextPollTime) {
                        spinWait.SpinOnce();
                    }
                }

                // Poll for available packets (hot path)
                ProcessIncomingPackets();

                // Schedule next poll
                nextPollTime += pollInterval;

                // Drift correction: prevent accumulating lag
                var now = stopwatch.ElapsedMilliseconds;
                if (now > nextPollTime + MaxDriftThreshold) {
                    nextPollTime = now + pollInterval;
                }
            } catch (InvalidOperationException ex) when (ex.Message.Contains("Steamworks is not initialized")) {
                // Steam shut down during operation - exit gracefully
                _steamInitialized = false;
                Logger.Info("Steam P2P Server: Steamworks shut down during receive, exiting loop");
                break;
            } catch (Exception e) {
                Logger.Error($"Steam P2P: Error in server receive loop: {e}");
                // Continue polling on error, but maintain timing
                nextPollTime = stopwatch.ElapsedMilliseconds + pollInterval;
            }
        }

        Logger.Info("Steam P2P Server: Receive loop exited cleanly");
    }

    /// <summary>
    /// Processes available P2P packets.
    /// Optimized for minimal allocations and maximum throughput.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessIncomingPackets() {
        // Early exit checks (use bitwise OR to avoid branch prediction penalty)
        if (!_isRunning | !_steamInitialized) return;

        var receiveBuffer = _receiveBuffer;
        var clients = _clients;

        // Server listens for client packets on Channel 0
        while (SteamNetworking.IsP2PPacketAvailable(out var packetSize)) {
            if (!SteamNetworking.ReadP2PPacket(
                    receiveBuffer,
                    MaxPacketSize,
                    out packetSize,
                    out var remoteSteamId
                )) {
                continue;
            }

            // Fast path: direct dictionary lookup
            if (clients.TryGetValue(remoteSteamId, out var client)) {
                var size = (int)packetSize;
                
                // Allocate only for client delivery - unavoidable as client needs owned copy
                var data = new byte[size];
                Buffer.BlockCopy(receiveBuffer, 0, data, 0, size);
                client.RaiseDataReceived(data, size);
            } else {
                Logger.Warn($"Steam P2P: Received packet from unknown client {remoteSteamId}");
            }
        }
    }

    /// <summary>
    /// Receives a packet from the loopback channel.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReceiveLoopbackPacket(byte[] data, int length) {
        // Early exit checks (use bitwise OR to avoid branch prediction penalty)
        if (!_isRunning | !_steamInitialized) return;

        try {
            var steamId = SteamUser.GetSteamID();

            // Try to get existing client first (common case)
            if (!_clients.TryGetValue(steamId, out var client)) {
                // Create new loopback client
                client = new SteamEncryptedTransportClient(steamId.m_SteamID);
                
                // Use TryAdd to handle race conditions
                if (_clients.TryAdd(steamId, client)) {
                    ClientConnectedEvent?.Invoke(client);
                } else {
                    // Another thread added it, retrieve the instance
                    _clients.TryGetValue(steamId, out client);
                }
            }

            client?.RaiseDataReceived(data, length);
        } catch (InvalidOperationException) {
            // Steam shut down between check and API call - ignore silently
            _steamInitialized = false;
        }
    }
}

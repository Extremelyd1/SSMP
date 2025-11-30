using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using SSMP.Game;
using SSMP.Logging;
using SSMP.Networking.Transport.Common;
using Steamworks;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of <see cref="IEncryptedTransportServer{TClient}"/>.
/// Manages multiple client connections via Steam P2P networking.
/// </summary>
internal class SteamEncryptedTransportServer : IEncryptedTransportServer {
    /// <summary>
    /// P2P channel for server communication.
    /// </summary>
    private const int P2P_CHANNEL = 0;

    /// <summary>
    /// Maximum Steam P2P packet size.
    /// </summary>
    private const int MAX_PACKET_SIZE = 1200;

    /// <inheritdoc />
    public event Action<IEncryptedTransportClient>? ClientConnectedEvent;

    /// <summary>
    /// Connected clients indexed by Steam ID.
    /// </summary>
    private readonly ConcurrentDictionary<CSteamID, SteamEncryptedTransportClient> _clients = new();

    /// <summary>
    /// Buffer for receiving P2P packets.
    /// </summary>
    private readonly byte[] _receiveBuffer = new byte[MAX_PACKET_SIZE];

    /// <summary>
    /// Whether the server is currently running.
    /// </summary>
    private volatile bool _isRunning;

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
    /// Start listening for Steam P2P connections.
    /// </summary>
    /// <param name="port">Port parameter (unused for Steam P2P)</param>
    /// <exception cref="InvalidOperationException">Thrown if Steam is not initialized.</exception>
    public void Start(int port) {
        if (!SteamManager.IsInitialized) {
            throw new InvalidOperationException("Cannot start Steam P2P server: Steam is not initialized");
        }

        if (_isRunning) {
            Logger.Warn("Steam P2P server already running");
            return;
        }

        _isRunning = true;

        // Register callback for incoming P2P session requests
        _sessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);

        // Allow P2P packet relay through Steam servers if direct connection fails
        SteamNetworking.AllowP2PPacketRelay(true);

        Logger.Info("Steam P2P: Server started, listening for connections");

        // Register for loopback
        SteamLoopbackChannel.RegisterServer(this);

        // Start receive loop
        _receiveTokenSource = new CancellationTokenSource();
        _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        _receiveThread.Start();
    }

    /// <inheritdoc />
    public void Stop() {
        if (!_isRunning) return;

        Logger.Info("Steam P2P: Stopping server");

        // Disconnect all clients
        foreach (var client in _clients.Values) {
            DisconnectClient(client);
        }

        _clients.Clear();
        _sessionRequestCallback?.Dispose();
        _sessionRequestCallback = null;
        _isRunning = false;

        Logger.Info("Steam P2P: Server stopped");

        // Stop receive loop
        _receiveTokenSource?.Cancel();
        _receiveTokenSource?.Dispose();
        _receiveTokenSource = null;
        _receiveThread = null;

        // Unregister loopback
        SteamLoopbackChannel.UnregisterServer();
    }

    /// <inheritdoc />
    public void DisconnectClient(IEncryptedTransportClient client) {
        if (client is not SteamEncryptedTransportClient steamClient) return;
        
        var steamId = new CSteamID(steamClient.SteamId);
        if (!_clients.TryRemove(steamId, out _)) return;

        if (SteamManager.IsInitialized) {
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

        // Accept the P2P session
        if (!SteamNetworking.AcceptP2PSessionWithUser(remoteSteamId)) {
            Logger.Warn($"Steam P2P: Failed to accept session from {remoteSteamId}");
            return;
        }

        // Create client wrapper if this is a new connection
        if (_clients.ContainsKey(remoteSteamId)) return;

        var client = new SteamEncryptedTransportClient(remoteSteamId.m_SteamID);
        _clients[remoteSteamId] = client;

        Logger.Info($"Steam P2P: New client connected: {remoteSteamId}");

        // Fire client connected event
        ClientConnectedEvent?.Invoke(client);
    }

    /// <summary>
    /// Processes incoming P2P packets for all connected clients.
    /// Should be called regularly (e.g., in Update loop).
    /// </summary>
    /// <summary>
    /// Processes incoming P2P packets for all connected clients.
    /// </summary>
    private void ReceiveLoop() {
        var token = _receiveTokenSource;
        while (_isRunning && token != null && !token.IsCancellationRequested) {
            try {
                ProcessIncomingPackets();
            } catch (Exception e) {
                Logger.Error($"Steam P2P: Error in server receive loop: {e}");
            }
        }
    }

    /// <summary>
    /// Processes available P2P packets.
    /// </summary>
    private void ProcessIncomingPackets() {
        if (!_isRunning || !SteamManager.IsInitialized) return;

        // Process all available packets on our channel
        while (SteamNetworking.IsP2PPacketAvailable(out uint packetSize, P2P_CHANNEL)) {
            // Read the packet
            if (!SteamNetworking.ReadP2PPacket(_receiveBuffer, MAX_PACKET_SIZE, out packetSize,
                    out CSteamID remoteSteamId, P2P_CHANNEL)) {
                continue;
            }

            // Route packet to the appropriate client
            if (_clients.TryGetValue(remoteSteamId, out var client)) {
                client.RaiseDataReceived(_receiveBuffer, (int)packetSize);
            } else {
                Logger.Warn($"Steam P2P: Received packet from unknown client {remoteSteamId}");
            }
        }
    }

    /// <summary>
    /// Receives a packet from the loopback channel.
    /// </summary>
    public void ReceiveLoopbackPacket(byte[] data) {
        if (!_isRunning) return;

        // Loopback comes from local user
        var steamId = SteamUser.GetSteamID();
        
        // Ensure client exists
        if (!_clients.TryGetValue(steamId, out var client)) {
            // Create client wrapper if this is a new connection
            client = new SteamEncryptedTransportClient(steamId.m_SteamID);
            _clients[steamId] = client;
            Logger.Info($"Steam P2P: New loopback client connected: {steamId}");
            ClientConnectedEvent?.Invoke(client);
        }

        client.RaiseDataReceived(data, data.Length);
    }
}

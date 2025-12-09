using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using SSMP.Api.Server;
using SSMP.Api.Server.Networking;
using SSMP.Logging;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Connection;
using SSMP.Networking.Packet.Data;
using SSMP.Networking.Packet.Update;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Server;

/// <summary>
/// Server that manages connection with clients.
/// </summary>
internal class NetServer : INetServer {
    /// <summary>
    /// The time to throttle a client after they were rejected connection in milliseconds.
    /// </summary>
    private const int ThrottleTime = 2500;

    /// <summary>
    /// The packet manager instance.
    /// </summary>
    private readonly PacketManager _packetManager;
    
    /// <summary>
    /// Underlying encrypted transport server instance.
    /// </summary>
    private IEncryptedTransportServer? _transportServer;
        
    /// <summary>
    /// Dictionary mapping client IDs to net server clients.
    /// </summary>
    private readonly ConcurrentDictionary<ushort, NetServerClient> _clientsById;

    /// <summary>
    /// Dictionary for throttling clients by their endpoint.
    /// Maps endpoint to a stopwatch tracking their last connection attempt.
    /// Steam clients (which return null ThrottleKey) are not throttled.
    /// </summary>
    private readonly ConcurrentDictionary<IPEndPoint, Stopwatch> _throttledClients;

    /// <summary>
    /// Concurrent queue that contains received data from a client ready for processing.
    /// </summary>
    private readonly ConcurrentQueue<ReceivedData> _receivedQueue;
    
    /// <summary>
    /// Wait handle for inter-thread signalling when new data is ready to be processed.
    /// </summary>
    private readonly AutoResetEvent _processingWaitHandle;

    /// <summary>
    /// Byte array containing leftover data that was not processed as a packet yet.
    /// </summary>
    private byte[]? _leftoverData;

    /// <summary>
    /// Cancellation token source for all threads of the server.
    /// </summary>
    private CancellationTokenSource? _taskTokenSource;

    /// <summary>
    /// Processing thread for handling received data.
    /// </summary>
    private Thread? _processingThread;

    /// <summary>
    /// Event that is called when a client times out.
    /// </summary>
    public event Action<ushort>? ClientTimeoutEvent;

    /// <summary>
    /// Event that is called when the server shuts down.
    /// </summary>
    public event Action? ShutdownEvent;

    /// <summary>
    /// Event that is called when a new client wants to connect.
    /// </summary>
    public event Action<NetServerClient, ClientInfo, ServerInfo>? ConnectionRequestEvent;

    /// <inheritdoc />
    public bool IsStarted { get; private set; }

    public NetServer(
        PacketManager packetManager
    ) {
        _packetManager = packetManager;


        _clientsById = new ConcurrentDictionary<ushort, NetServerClient>();
        _throttledClients = new ConcurrentDictionary<IPEndPoint, Stopwatch>();

        _receivedQueue = new ConcurrentQueue<ReceivedData>();
        
        _processingWaitHandle = new AutoResetEvent(false);
        
        _packetManager.RegisterServerConnectionPacketHandler<ClientInfo>(
            ServerConnectionPacketId.ClientInfo, 
            OnClientInfoReceived
        );
    }

    /// <summary>
    /// Starts the server on the given port.
    /// </summary>
    /// <param name="port">The networking port.</param>
    /// <param name="transportServer">The transport server to use.</param>
    public void Start(int port, IEncryptedTransportServer transportServer) {
        if (transportServer == null) {
            throw new ArgumentNullException(nameof(transportServer));
        }

        if (IsStarted) {
            Stop();
        }
        
        Logger.Info($"Starting NetServer on port {port}");
        IsStarted = true;
        
        _transportServer = transportServer;
        _transportServer.Start(port);

        // Create a cancellation token source for the tasks that we are creating
        _taskTokenSource = new CancellationTokenSource();

        // Start a thread for handling the processing of received data
        _processingThread = new Thread(() => StartProcessing(_taskTokenSource.Token)) { IsBackground = true };
        _processingThread.Start();

        _transportServer.ClientConnectedEvent += OnClientConnected;
    }

    /// <summary>
    /// Callback when a new client connects via any transport.
    /// Subscribe to the client's data event and enqueue received data.
    /// </summary>
    private void OnClientConnected(IEncryptedTransportClient transportClient) {
        transportClient.DataReceivedEvent += (buffer, length) => {
            _receivedQueue.Enqueue(new ReceivedData {
                TransportClient = transportClient,
                Buffer = buffer,
                NumReceived = length
            });
            _processingWaitHandle.Set();
        };
    }

    /// <summary>
    /// Starts processing queued network data.
    /// </summary>
    /// <param name="token">The cancellation token for checking whether this task is requested to cancel.</param>
    private void StartProcessing(CancellationToken token) {
        WaitHandle[] waitHandles = [_processingWaitHandle, token.WaitHandle];

        while (!token.IsCancellationRequested) {
            WaitHandle.WaitAny(waitHandles);

            while (!token.IsCancellationRequested && _receivedQueue.TryDequeue(out var receivedData)) {
                var packets = PacketManager.HandleReceivedData(
                    receivedData.Buffer,
                    receivedData.NumReceived,
                    ref _leftoverData
                );

                var transportClient = receivedData.TransportClient;

                // Try to find existing client by transport client reference
                var client = _clientsById.Values.FirstOrDefault(c => c.TransportClient == transportClient);
                
                if (client == null) {
                    // Extract throttle key for throttling
                    var throttleKey = transportClient.EndPoint;

                    if (throttleKey != null && _throttledClients.TryGetValue(throttleKey, out var clientStopwatch)) {
                        if (clientStopwatch.ElapsedMilliseconds < ThrottleTime) {
                            // Reset stopwatch and ignore packets so the client times out
                            clientStopwatch.Restart();
                            continue;
                        }

                        // Stopwatch exceeds max throttle time so we remove the client from the dict
                        _throttledClients.TryRemove(throttleKey, out _);
                    }

                    Logger.Info($"Received packet from unknown client: {transportClient.ToDisplayString()}, creating new client");

                    // We didn't find a client with the given identifier, so we assume it is a new client
                    // that wants to connect
                    client = CreateNewClient(transportClient);
                }

                HandleClientPackets(client, packets);
            }
        }
    }

    /// <summary>
    /// Create a new client and start sending UDP updates and registering the timeout event.
    /// </summary>
    /// <param name="transportClient">The transport client to create the client from.</param>
    /// <returns>A new net server client instance.</returns>
    private NetServerClient CreateNewClient(IEncryptedTransportClient transportClient) {
        var netServerClient = new NetServerClient(transportClient, _packetManager);
        
        netServerClient.ChunkSender.Start();

        netServerClient.ConnectionManager.ConnectionRequestEvent += OnConnectionRequest;
        netServerClient.ConnectionManager.ConnectionTimeoutEvent += () => HandleClientTimeout(netServerClient);
        netServerClient.ConnectionManager.StartAcceptingConnection();

        netServerClient.UpdateManager.TimeoutEvent += () => HandleClientTimeout(netServerClient);
        netServerClient.UpdateManager.StartUpdates();

        // Only add to _clientsById dictionary
        _clientsById.TryAdd(netServerClient.Id, netServerClient);

        return netServerClient;
    }

    /// <summary>
    /// Handles the event when a client times out. Disconnects the UDP client and cleans up any references
    /// to the client.
    /// </summary>
    /// <param name="client">The client that timed out.</param>
    private void HandleClientTimeout(NetServerClient client) {
        var id = client.Id;

        // Only execute the client timeout callback if the client is registered and thus has an ID
        if (client.IsRegistered) {
            ClientTimeoutEvent?.Invoke(id);
        }

        client.Disconnect();
        _transportServer?.DisconnectClient(client.TransportClient);
        _clientsById.TryRemove(id, out _);

        Logger.Info($"Client {id} timed out");
    }

    /// <summary>
    /// Handle a list of packets from a registered client.
    /// </summary>
    /// <param name="client">The registered client.</param>
    /// <param name="packets">The list of packets to handle.</param>
    private void HandleClientPackets(NetServerClient client, List<Packet.Packet> packets) {
        var id = client.Id;

        foreach (var packet in packets) {
            // If the client is not registered, try to read as connection packet first
            if (!client.IsRegistered) {
                var savedReadPos = packet.ReadPosition;
                var connectionPacket = new ServerConnectionPacket();

                // Attempt connection parse on the original packet.
                // If it fails, restore read position so we can try other packet types.
                if (connectionPacket.ReadPacket(packet)) {
                    _packetManager.HandleServerConnectionPacket(id, connectionPacket);
                    // Parsed successfully as connection packet; packet has been consumed.
                    // Skip further processing to avoid double-handling.
                    continue;
                }

                // Restore read cursor for subsequent parsers
                packet.ReadPosition = savedReadPos;
            }

            var serverUpdatePacket = new ServerUpdatePacket();
            if (!serverUpdatePacket.ReadPacket(packet)) {
                if (client.IsRegistered) {
                    continue;
                }

                Logger.Debug($"Received malformed packet from client: {client.TransportClient.ToDisplayString()}");

                var throttleKey = client.TransportClient.EndPoint;
                if (throttleKey != null) {
                    _throttledClients[throttleKey] = Stopwatch.StartNew();
                }

                continue;
            }

            // Route all transports through UpdateManager for sequence/ACK tracking
            // UpdateManager will skip UDP-specific logic for Steam transports
            client.UpdateManager.OnReceivePacket<ServerUpdatePacket, ServerUpdatePacketId>(serverUpdatePacket);

            var packetData = serverUpdatePacket.GetPacketData();
            if (packetData.TryGetValue(ServerUpdatePacketId.Slice, out var sliceData)) {
                packetData.Remove(ServerUpdatePacketId.Slice);
                client.ChunkReceiver.ProcessReceivedData((SliceData) sliceData);
            }

            if (packetData.TryGetValue(ServerUpdatePacketId.SliceAck, out var sliceAckData)) {
                packetData.Remove(ServerUpdatePacketId.SliceAck);
                client.ChunkSender.ProcessReceivedData((SliceAckData) sliceAckData);
            }
        
            if (client.IsRegistered) {
                _packetManager.HandleServerUpdatePacket(id, serverUpdatePacket);
            }
        }
    }

    /// <summary>
    /// Callback method for when a connection request is received.
    /// </summary>
    /// <param name="clientId">The ID of the client.</param>
    /// <param name="clientInfo">The client info instance containing details about the client.</param>
    /// <param name="serverInfo">The server info instance that should be modified to reflect whether the client's
    /// connection is accepted or not.</param>
    private void OnConnectionRequest(ushort clientId, ClientInfo clientInfo, ServerInfo serverInfo) {
        if (!_clientsById.TryGetValue(clientId, out var client)) {
            Logger.Error($"Connection request for client without known ID: {clientId}");
            serverInfo.ConnectionResult = ServerConnectionResult.RejectedOther;
            serverInfo.ConnectionRejectedMessage = "Unknown client";

            return;
        }
        
        // Invoke the connection request event ourselves first, then check the result
        ConnectionRequestEvent?.Invoke(client, clientInfo, serverInfo);

        if (serverInfo.ConnectionResult == ServerConnectionResult.Accepted) {
            Logger.Debug($"Connection request for client ID {clientId} was accepted, finishing connection sends, then registering client");

            client.ConnectionManager.FinishConnection(() => {
                Logger.Debug("Connection has finished sending data, registering client");
                
                client.IsRegistered = true;
                client.ConnectionManager.StopAcceptingConnection();
            });
        } else {
            // Connection rejected - stop accepting new connection attempts immediately
            // FinishConnection and throttling will be handled in OnClientInfoReceived after
            // ServerInfo has been sent
            client.ConnectionManager.StopAcceptingConnection();
        }
    }

    /// <summary>
    /// Callback method for when client info is received in a connection packet.
    /// </summary>
    /// <param name="clientId">The ID of the client that sent the client info.</param>
    /// <param name="clientInfo">The client info instance.</param>
    private void OnClientInfoReceived(ushort clientId, ClientInfo clientInfo) {
        if (!_clientsById.TryGetValue(clientId, out var client)) {
            Logger.Error($"ClientInfo received from client without known ID: {clientId}");
            return;
        }

        // ProcessClientInfo will invoke OnConnectionRequest which populates the serverInfo,
        // and then send the ServerInfo packet to the client
        var serverInfo = client.ConnectionManager.ProcessClientInfo(clientInfo);

        // If connection was rejected, we need to finish sending the rejection message
        // and then disconnect + throttle the client
        if (serverInfo.ConnectionResult != ServerConnectionResult.Accepted) {
            // The rejection message has now been enqueued (by ProcessClientInfo -> SendServerInfo)
            // Wait for it to finish sending, then disconnect and throttle
            client.ConnectionManager.FinishConnection(() => { 
                OnClientDisconnect(clientId);
                var throttleKey = client.TransportClient.EndPoint;
                if (throttleKey != null) {
                    _throttledClients[throttleKey] = Stopwatch.StartNew();
                }
            });
        }
    }

    /// <summary>
    /// Stops the server and cleans up everything.
    /// </summary>
    public void Stop() {
        if (!IsStarted) {
            return;
        }

        Logger.Info("Stopping NetServer");

        IsStarted = false;

        // Request cancellation for the processing thread
        _taskTokenSource?.Cancel();

        // Wait for processing thread to exit gracefully (with timeout)
        if (_processingThread != null && _processingThread.IsAlive) {
            if (!_processingThread.Join(1000)) {
                Logger.Warn("Processing thread did not exit within timeout");
            }

            _processingThread = null;
        }

        // Stop transport server first to prevent new connections and unregister from loopback
        _transportServer?.Stop();
        if (_transportServer != null) {
            _transportServer.ClientConnectedEvent -= OnClientConnected;
            _taskTokenSource?.Dispose();
            _taskTokenSource = null;

            // Invoke the shutdown event to notify all registered parties of the shutdown
            ShutdownEvent?.Invoke();
        }
    }

    /// <summary>
    /// Callback method for when a client disconnects from the server.
    /// </summary>
    /// <param name="id">The ID of the client.</param>
    public void OnClientDisconnect(ushort id) {
        if (!_clientsById.TryGetValue(id, out var client)) {
            Logger.Warn($"Handling disconnect from ID {id}, but there's no matching client");
            return;
        }

        client.Disconnect();
        _transportServer?.DisconnectClient(client.TransportClient);
        _clientsById.TryRemove(id, out _);

        Logger.Info($"Client {id} disconnected");
    }

    /// <summary>
    /// Get the update manager for the client with the given ID.
    /// </summary>
    /// <param name="id">The ID of the client.</param>
    /// <returns>The update manager for the client, or null if there does not exist a client with the
    /// given ID.</returns>
    public ServerUpdateManager? GetUpdateManagerForClient(ushort id) {
        if (!_clientsById.TryGetValue(id, out var netServerClient)) {
            return null;
        }

        return netServerClient.UpdateManager;
    }

    /// <summary>
    /// Execute a given action for the update manager of all connected clients.
    /// </summary>
    /// <param name="dataAction">The action to execute with each update manager.</param>
    public void SetDataForAllClients(Action<ServerUpdateManager> dataAction) {
        foreach (var netServerClient in _clientsById.Values) {
            dataAction(netServerClient.UpdateManager);
        }
    }

    /// <inheritdoc />
    public IServerAddonNetworkSender<TPacketId> GetNetworkSender<TPacketId>(
        ServerAddon addon
    ) where TPacketId : Enum {
        if (addon == null) {
            throw new ArgumentNullException(nameof(addon));
        }

        // Check whether this addon has actually requested network access through their property
        // We check this otherwise an ID has not been assigned and it can't send network data
        if (!addon.NeedsNetwork) {
            throw new InvalidOperationException("Addon has not requested network access through property");
        }

        // Check whether there already is a network sender for the given addon
        if (addon.NetworkSender != null) {
            if (!(addon.NetworkSender is IServerAddonNetworkSender<TPacketId> addonNetworkSender)) {
                throw new InvalidOperationException(
                    "Cannot request network senders with differing generic parameters");
            }

            return addonNetworkSender;
        }

        // Otherwise create one, store it and return it
        var newAddonNetworkSender = new ServerAddonNetworkSender<TPacketId>(this, addon);
        addon.NetworkSender = newAddonNetworkSender;

        return newAddonNetworkSender;
    }

    /// <inheritdoc />
    public IServerAddonNetworkReceiver<TPacketId> GetNetworkReceiver<TPacketId>(
        ServerAddon addon,
        Func<TPacketId, IPacketData> packetInstantiator
    ) where TPacketId : Enum {
        if (addon == null) {
            throw new ArgumentException("Parameter 'addon' cannot be null");
        }

        if (packetInstantiator == null) {
            throw new ArgumentNullException(nameof(packetInstantiator));
        }

        // Check whether this addon has actually requested network access through their property
        // We check this otherwise an ID has not been assigned and it can't send network data
        if (!addon.NeedsNetwork) {
            throw new InvalidOperationException("Addon has not requested network access through property");
        }

        if (!addon.Id.HasValue) {
            throw new InvalidOperationException("Addon has no ID assigned");
        }

        ServerAddonNetworkReceiver<TPacketId>? networkReceiver = null;

        // Check whether an existing network receiver exists
        if (addon.NetworkReceiver == null) {
            networkReceiver = new ServerAddonNetworkReceiver<TPacketId>(addon, _packetManager);
            addon.NetworkReceiver = networkReceiver;
        } else if (addon.NetworkReceiver is not IServerAddonNetworkReceiver<TPacketId>) {
            throw new InvalidOperationException(
                "Cannot request network receivers with differing generic parameters");
        }

        // After we know that this call did not use a different generic, we can update packet info
        ServerUpdatePacket.AddonPacketInfoDict[addon.Id.Value] = new AddonPacketInfo(
            // Transform the packet instantiator function from a TPacketId as parameter to byte
            networkReceiver?.TransformPacketInstantiator(packetInstantiator)!,
            (byte) Enum.GetValues(typeof(TPacketId)).Length
        );

        return (addon.NetworkReceiver as IServerAddonNetworkReceiver<TPacketId>)!;
    }
}

/// <summary>
/// Data class for storing received data from a given IP end-point.
/// </summary>
internal class ReceivedData {
    /// <summary>
    /// The transport client that sent this data.
    /// </summary>
    public required IEncryptedTransportClient TransportClient { get; init; }
    
    /// <summary>
    /// Byte array of the buffer containing received data.
    /// </summary>
    public required byte[] Buffer { get; init; }
    
    /// <summary>
    /// The number of bytes in the buffer that were received. The rest of the buffer is empty.
    /// </summary>
    public int NumReceived { get; init; }
}

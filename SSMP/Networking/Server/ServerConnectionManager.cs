using System;
using System.Timers;
using SSMP.Logging;
using SSMP.Networking.Chunk;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Connection;
using SSMP.Networking.Packet.Data;

namespace SSMP.Networking.Server;

/// <summary>
/// Server-side manager for handling the initial connection to a new client.
/// </summary>
internal class ServerConnectionManager : ConnectionManager {
    /// <summary>
    /// Server-side chunk sender used to handle sending chunks.
    /// </summary>
    private readonly ServerChunkSender _chunkSender;
    /// <summary>
    /// Server-side chunk received used to receive chunks.
    /// </summary>
    private readonly ServerChunkReceiver _chunkReceiver;

    /// <summary>
    /// The ID of the client that this class manages.
    /// </summary>
    private readonly ushort _clientId;

    /// <summary>
    /// Timer that triggers when the connection takes too long and thus times out.
    /// </summary>
    private readonly Timer _timeoutTimer;

    /// <summary>
    /// Event that is called when the client has sent the client info, and thus we can check the connection request.
    /// </summary>
    public event Action<ushort, ClientInfo, ServerInfo>? ConnectionRequestEvent;
    /// <summary>
    /// Event that is called when the connection times out.
    /// </summary>
    public event Action? ConnectionTimeoutEvent;

    public ServerConnectionManager(
        PacketManager packetManager,
        ServerChunkSender chunkSender,
        ServerChunkReceiver chunkReceiver,
        ushort clientId
    ) : base(packetManager) {
        _chunkSender = chunkSender;
        _chunkReceiver = chunkReceiver;

        _clientId = clientId;

        _timeoutTimer = new Timer {
            Interval = TimeoutMillis,
            AutoReset = false
        };
        _timeoutTimer.Elapsed += (_, _) => ConnectionTimeoutEvent?.Invoke();

        _chunkReceiver.ChunkReceivedEvent += OnChunkReceived;
    }

    /// <summary>
    /// Start accepting connections, which will start the timeout timer.
    /// </summary>
    public void StartAcceptingConnection() {
        Logger.Debug("StartAcceptingConnection");
        
        _timeoutTimer.Start();
    }

    /// <summary>
    /// Stop accepting connections, which will stop the timeout timer.
    /// </summary>
    public void StopAcceptingConnection() {
        Logger.Debug("StopAcceptingConnection");
        
        _timeoutTimer.Stop();
    }

    /// <summary>
    /// Finish up the connection and execute the given callback when it is finished.
    /// </summary>
    /// <param name="callback">The action to execute when the connection is finished.</param>
    public void FinishConnection(Action callback) {
        _chunkSender.FinishSendingData(callback);
    }

    /// <summary>
    /// Process the given (received) client info. This will invoke the <see cref="ConnectionRequestEvent"/> and
    /// send the resulting server info back to the client. The ServerInfo is sent automatically within this method,
    /// so callers should not send it separately.
    /// </summary>
    /// <param name="clientInfo">The received client info packet.</param>
    /// <returns>The ServerInfo that was sent to the client, containing connection acceptance or rejection details.</returns>
    public ServerInfo ProcessClientInfo(ClientInfo clientInfo) {
        Logger.Debug($"Received client info from client with ID: {_clientId}");

        var serverInfo = new ServerInfo();

        try {
            ConnectionRequestEvent?.Invoke(_clientId, clientInfo, serverInfo);
        } catch (Exception e) {
            Logger.Error($"Exception occurred while executing the connection request event:\n{e}");
        }

        SendServerInfo(serverInfo);
        
        return serverInfo;
    }

    /// <summary>
    /// Sends the ServerInfo packet to the client.
    /// </summary>
    private void SendServerInfo(ServerInfo serverInfo) {
        var connectionPacket = new ClientConnectionPacket();
        connectionPacket.SetSendingPacketData(ClientConnectionPacketId.ServerInfo, serverInfo);

        var packet = new Packet.Packet();
        connectionPacket.CreatePacket(packet);

        _chunkSender.EnqueuePacket(packet);
    }

    /// <summary>
    /// Callback method for when a chunk is received. Will construct a connection packet and try to read the chunk
    /// into it. If successful, will let the packet manager handle the data in it.
    /// </summary>
    /// <param name="packet">The raw packet that contains the data from the chunk.</param>
    private void OnChunkReceived(Packet.Packet packet) {
        var connectionPacket = new ServerConnectionPacket();
        if (!connectionPacket.ReadPacket(packet)) {
            Logger.Debug($"Received malformed connection packet chunk from client: {_clientId}");
            return;
        }

        PacketManager.HandleServerConnectionPacket(_clientId, connectionPacket);
    }
}

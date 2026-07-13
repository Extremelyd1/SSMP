using System;
using System.Collections.Generic;
using SSMP.Logging;
using SSMP.Networking.Chunk;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Connection;
using SSMP.Networking.Packet.Data;

namespace SSMP.Networking.Client;

/// <summary>
/// Client-side manager for handling connection-phase traffic.
/// </summary>
/// <remarks>
/// Also handles chunked addon payloads after connection setup. We deliberately reused the
/// existing connection-packet chunking path instead of introducing a separate transport path.
/// </remarks>
internal class ClientConnectionManager : ConnectionManager {
    /// <summary>
    /// The client-side chunk sender used to handle sending chunks.
    /// </summary>
    private readonly ChunkSender _chunkSender;

    /// <summary>
    /// The client-side chunk received used to receive chunks.
    /// </summary>
    private readonly ChunkReceiver _chunkReceiver;

    /// <summary>
    /// Event that is called when server info is received from the server we are trying to connect to.
    /// </summary>
    public event Action<ServerInfo>? ServerInfoReceivedEvent;

    /// <summary>
    /// Whether post-setup chunked addon payloads are allowed to be dispatched yet.
    /// </summary>
    public bool AllowAddonChunks { get; set; }

    /// <summary>
    /// Construct the connection manager with the given packet manager and chunk sender, and receiver instances.
    /// Will register handlers for handshake traffic and for the reused connection-packet chunk transport.
    /// </summary>
    public ClientConnectionManager(
        PacketManager packetManager,
        ChunkSender chunkSender,
        ChunkReceiver chunkReceiver
    ) : base(packetManager) {
        _chunkSender = chunkSender;
        _chunkReceiver = chunkReceiver;

        packetManager.RegisterClientConnectionPacketHandler<ServerInfo>(
            ClientConnectionPacketId.ServerInfo,
            OnServerInfoReceived
        );
        _chunkReceiver.ChunkReceivedEvent += OnChunkReceived;
    }

    /// <summary>
    /// Start establishing the connection to the server with the given information.
    /// </summary>
    /// <param name="username">The username of the player.</param>
    /// <param name="authKey">The authentication key of the player.</param>
    /// <param name="addonData">
    /// List of addon data that represents the enabled networked addons that the client uses.
    /// </param>
    public void StartConnection(
        string username,
        string authKey,
        List<AddonData> addonData
    ) {
        // Create the connection packet that will be chunk-transported to the server.
        var connectionPacket = new ServerConnectionPacket();

        // Set the client info data in the connection packet
        connectionPacket.SetSendingPacketData(
            ServerConnectionPacketId.ClientInfo, new ClientInfo {
                Username = username,
                AuthKey = authKey,
                AddonData = addonData
            }
        );

        // Create the raw packet from the connection packet
        var packet = new Packet.Packet();
        connectionPacket.CreatePacket(packet);

        // All transports use ChunkSender for connection packets.
        // This same chunk path is intentionally reused later for oversized addon payloads.
        _chunkSender.EnqueuePacket(packet);
    }

    /// <summary>
    ///     Callback method for when server info is received from the server.
    /// </summary>
    /// <param name="serverInfo">The server info instance received from the server.</param>
    private void OnServerInfoReceived(ServerInfo serverInfo) {
        Logger.Debug($"ServerInfo received, connection accepted: {serverInfo.ConnectionResult}");

        ServerInfoReceivedEvent?.Invoke(serverInfo);
    }

    /// <summary>
    /// Callback method for when a new connection-phase chunk is received from the server.
    /// This covers both handshake packets and the deliberate reuse of connection packets for chunked addon payloads.
    /// </summary>
    /// <param name="packet">The raw packet that contains the data from the chunk.</param>
    private void OnChunkReceived(Packet.Packet packet) {
        // Create the connection packet instance and try to read it
        var connectionPacket = new ClientConnectionPacket();
        if (!connectionPacket.ReadPacket(packet)) {
            Logger.Debug("Received malformed connection packet chunk from server");
            return;
        }

        var packetData = connectionPacket.GetPacketData();
        if (packetData.ContainsKey(ClientConnectionPacketId.ServerInfo)) {
            PacketManager.HandleClientConnectionPacket(connectionPacket);
            return;
        }

        if (!packetData.TryGetValue(ClientConnectionPacketId.ChunkAddonData, out var chunkAddonDataRaw)) {
            Logger.Debug("Received unexpected connection chunk packet with no supported payload");
            return;
        }

        if (!AllowAddonChunks) {
            Logger.Warn("Discarded chunked addon payload before the client completed connection setup");
            return;
        }

        var chunkAddonData = (ChunkAddonData) chunkAddonDataRaw;
        if (!ClientConnectionPacket.AddonPacketInfoDict.TryGetValue(chunkAddonData.AddonId, out var addonPacketInfo)) {
            Logger.Warn($"Received chunked addon payload for unknown addon ID {chunkAddonData.AddonId}");
            return;
        }

        var packetDataInstance = addonPacketInfo.PacketDataInstantiator.Invoke(chunkAddonData.PacketId);
        packetDataInstance.ReadData(new Packet.Packet(chunkAddonData.Payload));
        PacketManager.HandleClientAddonPacketSingle(
            chunkAddonData.AddonId, chunkAddonData.PacketId, packetDataInstance
        );
    }
}

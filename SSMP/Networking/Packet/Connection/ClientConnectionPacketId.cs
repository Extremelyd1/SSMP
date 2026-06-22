namespace SSMP.Networking.Packet.Connection;

/// <summary>
/// Enumeration of packet IDs for connection-phase packets for server-to-client communication.
/// </summary>
/// <remarks>
/// These packets are used for handshake traffic and, by deliberate design choice,
/// for chunked addon payload transport.
/// </remarks>
internal enum ClientConnectionPacketId {
    /// <summary>
    /// Information about the server meant for the client detailing whether the connection was accepted.
    /// </summary>
    ServerInfo = 0,

    /// <summary>
    /// Chunk-only large addon payload sent over the connection chunk path.
    /// </summary>
    ChunkAddonData = 1,
}

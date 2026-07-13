namespace SSMP.Networking.Packet.Connection;

/// <summary>
/// Enumeration of packet IDs for connection-phase packets for client-to-server communication.
/// </summary>
/// <remarks>
/// These packets are used for handshake traffic and, by deliberate design choice,
/// for chunked addon payload transport.
/// </remarks>
internal enum ServerConnectionPacketId {
    /// <summary>
    /// Information about the client that the server can use to determine whether to accept the connection.
    /// </summary>
    ClientInfo = 0,

    /// <summary>
    /// Chunk-only large addon payload sent over the connection chunk path.
    /// </summary>
    ChunkAddonData = 1,
}

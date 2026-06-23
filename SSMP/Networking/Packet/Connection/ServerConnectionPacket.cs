using SSMP.Networking.Packet.Data;

namespace SSMP.Networking.Packet.Connection;

/// <summary>
/// Packet that contains connection information for client to server communication.
/// </summary>
internal class ServerConnectionPacket : BasePacket<ServerConnectionPacketId> {
    /// <inheritdoc />
    protected override IPacketData InstantiatePacketDataFromId(ServerConnectionPacketId packetId) {
        return packetId switch {
            ServerConnectionPacketId.ClientInfo => new ClientInfo(),
            ServerConnectionPacketId.ChunkAddonData => new ChunkAddonData(),
            _ => new EmptyData()
        };
    }
}

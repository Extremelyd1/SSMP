using SSMP.Networking.Packet.Data;

namespace SSMP.Networking.Packet.Connection;

/// <summary>
/// Packet that contains connection information for server to client communication.
/// </summary>
internal class ClientConnectionPacket : BasePacket<ClientConnectionPacketId> {
    /// <inheritdoc />
    protected override IPacketData InstantiatePacketDataFromId(ClientConnectionPacketId packetId) {
        return packetId switch {
            ClientConnectionPacketId.ServerInfo => new ServerInfo(),
            ClientConnectionPacketId.ChunkAddonData => new ChunkAddonData(),
            _ => new EmptyData()
        };
    }
}

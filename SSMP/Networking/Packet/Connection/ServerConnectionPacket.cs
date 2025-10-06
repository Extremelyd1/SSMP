using SSMP.Networking.Packet.Data;

namespace SSMP.Networking.Packet.Connection;

/// <summary>
/// Packet that contains connection information for client to server communication.
/// </summary>
internal class ServerConnectionPacket : BasePacket<ServerConnectionPacketId> {
    /// <inheritdoc />
    protected override IPacketData InstantiatePacketDataFromId(ServerConnectionPacketId packetId) {
        switch (packetId) {
            case ServerConnectionPacketId.ClientInfo:
                return new ClientInfo();
            default:
                return new EmptyData();
        }
    }
}

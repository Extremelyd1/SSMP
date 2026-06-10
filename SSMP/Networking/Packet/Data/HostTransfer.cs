namespace SSMP.Networking.Packet.Data;

/// <summary>
/// Packet data for a host transfer.
/// </summary>
internal class HostTransfer : IPacketData {
    /// <inheritdoc />
    public bool IsReliable => true;

    /// <inheritdoc />
    public bool DropReliableDataIfNewerExists => true;

    /// <summary>
    /// The name of the scene in which the player becomes the scene host.
    /// </summary>
    public string SceneName { get; set; } = null!;

    /// <summary>
    /// If true, the player should stop being the scene host and become a client instead.
    /// </summary>
    public bool Demote { get; set; }

    /// <inheritdoc />
    public void WriteData(IPacket packet) {
        packet.Write(SceneName);
        packet.Write(Demote);
    }

    /// <inheritdoc />
    public void ReadData(IPacket packet) {
        SceneName = packet.ReadString();
        Demote = packet.ReadBool();
    }
}

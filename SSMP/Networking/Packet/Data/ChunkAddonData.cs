using System;

namespace SSMP.Networking.Packet.Data;

/// <summary>
/// Dedicated chunk-only envelope for large addon payloads.
/// This bypasses the normal BasePacket addon framing so it can carry payloads above ushort.MaxValue.
/// </summary>
internal sealed class ChunkAddonData : IPacketData {
    /// <inheritdoc />
    public bool IsReliable => true;

    /// <inheritdoc />
    public bool DropReliableDataIfNewerExists => false;

    /// <summary>
    /// The addon ID that owns the payload.
    /// </summary>
    public byte AddonId { get; set; }

    /// <summary>
    /// The addon-local packet ID of the payload.
    /// </summary>
    public byte PacketId { get; set; }

    /// <summary>
    /// The serialized addon payload bytes.
    /// </summary>
    public byte[] Payload { get; set; } = null!;

    /// <inheritdoc />
    public void WriteData(IPacket packet) {
        packet.Write(AddonId);
        packet.Write(PacketId);
        packet.Write(Payload.Length);
        packet.Write(Payload);
    }

    /// <inheritdoc />
    public void ReadData(IPacket packet) {
        AddonId = packet.ReadByte();
        PacketId = packet.ReadByte();

        var payloadLength = packet.ReadInt();
        if (payloadLength < 0) {
            throw new Exception($"Chunk addon payload length cannot be negative: {payloadLength}");
        }

        Payload = packet.ReadBytes(payloadLength);
    }
}

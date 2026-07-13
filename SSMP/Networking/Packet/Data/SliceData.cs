using System;

namespace SSMP.Networking.Packet.Data;

/// <summary>
/// Packet with raw byte data as a slice of a bigger chunk meant for large reliable data transfer during connection.
/// </summary>
internal class SliceData : IPacketData {
    /// <inheritdoc />
    public bool IsReliable => false;

    /// <inheritdoc />
    public bool DropReliableDataIfNewerExists => false;

    /// <summary>
    /// The ID of the chunk that is being networked.
    /// </summary>
    public byte ChunkId { get; set; }

    /// <summary>
    /// The ID of this slice.
    /// </summary>
    public ushort SliceId { get; set; }

    /// <summary>
    /// The total number of slices in this chunk.
    /// </summary>
    public ushort NumSlices { get; set; }

    /// <summary>
    /// Byte array containing the data of this slice.
    /// </summary>
    public byte[] Data { get; set; } = null!;

    /// <inheritdoc />
    public void WriteData(IPacket packet) {
        packet.Write(ChunkId);
        packet.Write(SliceId);
        packet.Write(NumSlices);

        var length = Data.Length;
        if (length > ConnectionManager.MaxSliceSize) {
            throw new ArgumentOutOfRangeException(
                nameof(Data), $"Length of data for slice cannot exceed {ConnectionManager.MaxSliceSize}"
            );
        }

        if (SliceId == NumSlices - 1) {
            packet.Write((ushort) length);
        }

        packet.Write(Data);
    }

    /// <inheritdoc />
    public void ReadData(IPacket packet) {
        ChunkId = packet.ReadByte();
        SliceId = packet.ReadUShort();
        NumSlices = packet.ReadUShort();

        if (NumSlices is < 1 or > ConnectionManager.MaxSlicesPerChunk) {
            throw new Exception($"Invalid slice count: {NumSlices}");
        }

        if (SliceId >= NumSlices) {
            throw new Exception($"SliceId {SliceId} exceeds total slices {NumSlices}");
        }

        ushort length;
        if (SliceId == NumSlices - 1) {
            length = packet.ReadUShort();
            if (length is < 1 or > ConnectionManager.MaxSliceSize) {
                throw new Exception(
                    $"Invalid slice data length: {length} must be between 1 and {ConnectionManager.MaxSliceSize}"
                );
            }
        } else {
            length = ConnectionManager.MaxSliceSize;
        }

        Data = packet.ReadBytes(length);
    }
}

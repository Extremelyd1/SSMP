using System;

namespace SSMP.Networking.Packet.Data;

/// <summary>
/// Packet for acknowledging a received slice packet for large reliable data transfer during connection.
/// </summary>
internal class SliceAckData : IPacketData {
    /// <inheritdoc />
    public bool IsReliable => false;

    /// <inheritdoc />
    public bool DropReliableDataIfNewerExists => false;

    /// <summary>
    /// The ID of the chunk that is being networked.
    /// </summary>
    public byte ChunkId { get; set; }

    /// <summary>
    /// The total number of slices in this chunk.
    /// </summary>
    public ushort NumSlices { get; set; }

    /// <summary>
    /// Boolean array containing whether a slice was acked. For writing packets, the length of the array can equal
    /// the number of slices. For reading packets, the length of the array will equal the maximum possible number
    /// of slices per chunk.
    /// </summary>
    public bool[] Acked { get; set; } = null!;

    /// <inheritdoc />
    public void WriteData(IPacket packet) {
        packet.Write(ChunkId);
        packet.Write(NumSlices);

        // Keep track of current index for writing ack array
        var currentIndex = 0;
        while (currentIndex < NumSlices) {
            packet.Write(CreateAckFlag(currentIndex, currentIndex + 8, Acked));
            currentIndex += 8;
        }
    }

    /// <inheritdoc />
    public void ReadData(IPacket packet) {
        ChunkId = packet.ReadByte();
        NumSlices = packet.ReadUShort();

        switch (NumSlices) {
            case < 1:
                throw new Exception("Invalid slice count: NumSlices must be at least 1");
            case > ConnectionManager.MaxSlicesPerChunk:
                throw new Exception(
                    $"Invalid slice count: {NumSlices} exceeds maximum of {ConnectionManager.MaxSlicesPerChunk}"
                );
        }

        var acked = new bool[NumSlices];

        // Keep track of current index for writing to ack array
        var currentIndex = 0;
        while (currentIndex < NumSlices) {
            var flag = packet.ReadByte();
            ReadAckFlag(flag, currentIndex, currentIndex + 8, ref acked);
            currentIndex += 8;
        }

        Acked = acked;
    }

    /// <summary>
    /// Create a bit flag as a byte from the given boolean array with start and end indices.
    /// </summary>
    /// <param name="startIndex">The (inclusive) start index to start reading from the boolean array.</param>
    /// <param name="endIndex">The (exclusive) end index to stop reading from the boolean array.</param>
    /// <param name="acked">The boolean array to read values from for the flag.</param>
    /// <returns>The bit flag as a byte.</returns>
    private static byte CreateAckFlag(int startIndex, int endIndex, bool[] acked) {
        byte flag = 0;
        byte currentValue = 1;

        for (var i = startIndex; i < endIndex; i++) {
            if (acked.Length <= i) {
                break;
            }

            if (acked[i]) {
                flag |= currentValue;
            }

            currentValue *= 2;
        }

        return flag;
    }

    /// <summary>
    /// Read a bit flag in byte form and put the bits into the given reference boolean array.
    /// </summary>
    /// <param name="flag">The bit flag as a byte.</param>
    /// <param name="startIndex">The (inclusive) start index to start reading from the boolean array.</param>
    /// <param name="endIndex">The (exclusive) end index to stop reading from the boolean array.</param>
    /// <param name="acked">The boolean array as a reference to write values to from the flag.</param>
    private static void ReadAckFlag(byte flag, int startIndex, int endIndex, ref bool[] acked) {
        byte currentValue = 1;

        for (var i = startIndex; i < endIndex; i++) {
            if (i >= acked.Length) {
                break;
            }

            if ((flag & currentValue) != 0) {
                acked[i] = true;
            }

            currentValue *= 2;
        }
    }
}

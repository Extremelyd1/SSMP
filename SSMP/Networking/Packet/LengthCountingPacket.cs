using System;
using System.Collections.Generic;
using SSMP.Math;

namespace SSMP.Networking.Packet;

/// <summary>
/// A lightweight implementation of IPacket that only counts the number of bytes written,
/// completely avoiding any heap allocations or byte copies during size validation.
/// </summary>
public sealed class LengthCountingPacket : IPacket {
    /// <summary>
    /// Gets the number of bytes counted.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// Resets the byte count back to zero.
    /// </summary>
    public void Reset() => Length = 0;

    /// <inheritdoc />
    public void Write(byte value) => Length += 1;

    /// <inheritdoc />
    public void Write(ushort value) => Length += 2;

    /// <inheritdoc />
    public void Write(uint value) => Length += 4;

    /// <inheritdoc />
    public void Write(ulong value) => Length += 8;

    /// <inheritdoc />
    public void Write(sbyte value) => Length += 1;

    /// <inheritdoc />
    public void Write(short value) => Length += 2;

    /// <inheritdoc />
    public void Write(int value) => Length += 4;

    /// <inheritdoc />
    public void Write(long value) => Length += 8;

    /// <inheritdoc />
    public void Write(float value) => Length += 4;

    /// <inheritdoc />
    public void Write(double value) => Length += 8;

    /// <inheritdoc />
    public void Write(bool value) => Length += 1;

    /// <inheritdoc />
    public void Write(string value) => Length += 2 + System.Text.Encoding.UTF8.GetByteCount(value);

    /// <inheritdoc />
    public void Write(Vector2 value) => Length += 8;

    /// <inheritdoc />
    public void Write(Vector3 value) => Length += 12;

    /// <inheritdoc />
    public void Write(byte[] values) => Length += values.Length;

    /// <inheritdoc />
    public void WriteBitFlag<TEnum>(ISet<TEnum> set) where TEnum : Enum {
        var enumLength = Enum.GetValues(typeof(TEnum)).Length;
        switch (enumLength) {
            case <= 8:
                Length += 1;
                break;
            case <= 16:
                Length += 2;
                break;
            case <= 32:
                Length += 4;
                break;
            case <= 64:
                Length += 8;
                break;
        }
    }

    /// <inheritdoc />
    public byte ReadByte() => throw new NotSupportedException();

    /// <inheritdoc />
    public ushort ReadUShort() => throw new NotSupportedException();

    /// <inheritdoc />
    public uint ReadUInt() => throw new NotSupportedException();

    /// <inheritdoc />
    public ulong ReadULong() => throw new NotSupportedException();

    /// <inheritdoc />
    public sbyte ReadSByte() => throw new NotSupportedException();

    /// <inheritdoc />
    public short ReadShort() => throw new NotSupportedException();

    /// <inheritdoc />
    public int ReadInt() => throw new NotSupportedException();

    /// <inheritdoc />
    public long ReadLong() => throw new NotSupportedException();

    /// <inheritdoc />
    public float ReadFloat() => throw new NotSupportedException();

    /// <inheritdoc />
    public double ReadDouble() => throw new NotSupportedException();

    /// <inheritdoc />
    public bool ReadBool() => throw new NotSupportedException();

    /// <inheritdoc />
    public string ReadString() => throw new NotSupportedException();

    /// <inheritdoc />
    public Vector2 ReadVector2() => throw new NotSupportedException();

    /// <inheritdoc />
    public Vector3 ReadVector3() => throw new NotSupportedException();

    /// <inheritdoc />
    public ISet<TEnum> ReadBitFlag<TEnum>() where TEnum : Enum => throw new NotSupportedException();

    /// <inheritdoc />
    public byte[] ReadBytes(int length) => throw new NotSupportedException();
}

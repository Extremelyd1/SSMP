using System;
using System.Collections.Generic;
using System.Text;
using SSMP.Math;

namespace SSMP.Networking.Packet;

/// <inheritdoc />
internal class Packet : IPacket {
    /// <summary>
    /// A list of bytes that are contained in this packet.
    /// </summary>
    /// <summary>
    /// A list of bytes that are contained in this packet.
    /// Null if the packet is in read-only View Mode.
    /// </summary>
    private readonly List<byte>? _buffer;

    /// <summary>
    /// Byte array used as a readable buffer.
    /// In View Mode, this wraps the external array directly.
    /// </summary>
    private readonly byte[] _readableBuffer;

    /// <summary>
    /// The offset in the readable buffer where the packet data starts.
    /// </summary>
    private readonly int _offset;

    /// <summary>
    /// The current position in the buffer to read.
    /// Relative to the _offset start.
    /// </summary>
    private int _readPos;

    /// <summary>
    /// The length of the packet content.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// Creates a packet with the given byte array of data.
    /// Used when receiving packets to read data from.
    /// COPIES the data to a new internal list.
    /// </summary>
    /// <param name="data">The data to copy.</param>
    public Packet(byte[] data) {
        _buffer = new List<byte>(data.Length);
        _buffer.AddRange(data);
        _readableBuffer = _buffer.ToArray();
        _offset = 0;
        Length = data.Length;
    }

    /// <summary>
    /// Creates a packet in View Mode that wraps the given array without copying.
    /// The packet will be Read-Only.
    /// </summary>
    /// <param name="data">The array to wrap.</param>
    /// <param name="offset">The offset in the array.</param>
    /// <param name="length">The length of the view.</param>
    public Packet(byte[] data, int offset, int length) {
        _buffer = null;
        _readableBuffer = data;
        _offset = offset;
        Length = length;
        _readPos = 0;
    }

    /// <summary>
    /// Creates an empty packet to write data into.
    /// </summary>
    public Packet() {
        _buffer = [];
        _readableBuffer = [];
        _offset = 0;
        Length = 0;
    }

    /// <summary>
    /// Inserts the length of the packet's content at the start of the buffer.
    /// Zero-allocation: uses direct byte manipulation instead of BitConverter.
    /// </summary>
    public void WriteLength() {
        if (_buffer == null) throw new InvalidOperationException("Cannot write to Read-Only Packet");
        var length = (ushort) _buffer.Count;
        _buffer.Insert(0, (byte) length);
        _buffer.Insert(1, (byte) (length >> 8));
    }

    /// <summary>
    /// Gets the packet's content in array form.
    /// </summary>
    /// <returns>A byte array representing the packet content.</returns>
    public byte[] ToArray() {
        if (_buffer != null) return _buffer.ToArray();
        // Return a copy of the view
        var copy = new byte[Length];
        Array.Copy(_readableBuffer, _offset, copy, 0, Length);
        return copy;
    }

    /// <summary>
    /// Clears the packet buffer, allowing reuse.
    /// Resets length and read position to 0.
    /// </summary>
    public void Clear() {
        if (_buffer == null) throw new InvalidOperationException("Cannot clear Read-Only Packet");
        _buffer.Clear();
        // Readable buffer assumes it mirrors _buffer in write mode, but usually _readableBuffer is a copy or view.
        // In Write Mode (constructor Packet()), _readableBuffer is initialized to empty array.
        // We should ensure consistency if _readableBuffer was modified (it isn't in write mode).
        // Actually, _readableBuffer is only set in constructors.
        // For writing, we operate on _buffer.
        Length = 0;
        _readPos = 0;
    }

    /// <summary>
    /// Copies the packet's content to the provided buffer.
    /// Avoids allocation when caller provides pre-allocated or pooled buffer.
    /// </summary>
    /// <param name="destination">The destination buffer to copy to.</param>
    /// <param name="destinationOffset">The offset in the destination buffer to start copying.</param>
    /// <param name="sourceOffset">The offset in the packet buffer to start copying from.</param>
    /// <param name="count">The number of bytes to copy.</param>
    public void CopyTo(byte[] destination, int destinationOffset, int sourceOffset, int count) {
        // Use the readable buffer if available (View Mode or after receive)
        // Note: For View Mode, sourceOffset is relative to _offset
        if (_readableBuffer.Length > 0) {
            Array.Copy(_readableBuffer, _offset + sourceOffset, destination, destinationOffset, count);
        } else if (_buffer != null) {
            // Fallback for write-mode packets (List backing)
            for (var i = 0; i < count; i++) {
                destination[destinationOffset + i] = _buffer[sourceOffset + i];
            }
        }
    }

    /// <summary>
    /// Write an array of bytes to the packet.
    /// </summary>
    /// <param name="values">A byte array of values to write.</param>
    public void Write(byte[] values) {
        if (_buffer == null) throw new InvalidOperationException("Cannot write to Read-Only Packet");
        _buffer.AddRange(values);
    }

    /// <summary>
    /// Read an array of bytes of the given length from the packet.
    /// </summary>
    /// <param name="length">The length to read.</param>
    /// <returns>A byte array of the given length containing the content at the current position in the
    /// packet.</returns>
    /// <exception cref="Exception">Thrown if there are not enough bytes of content left to read.</exception>
    public byte[] ReadBytes(int length) {
        // Check whether there is enough bytes left to read
        if (_buffer != null && _buffer.Count >= _readPos + length) {
            var bytes = new byte[length];

            Array.Copy(_readableBuffer, _offset + _readPos, bytes, 0, length);

            // Increase the reading position in the buffer
            _readPos += length;

            return bytes;
        }

        throw new Exception($"Could not read {length} bytes");
    }

    #region IPacket interface implementations

    #region Writing integral numeric types

    /// <inheritdoc />
    public void Write(byte value) {
        if (_buffer == null) throw new InvalidOperationException("Cannot write to Read-Only Packet");
        _buffer.Add(value);
    }

    /// <inheritdoc />
    /// Zero-allocation: uses direct byte manipulation instead of BitConverter.
    public void Write(ushort value) {
        if (_buffer == null) throw new InvalidOperationException("Cannot write to Read-Only Packet");
        _buffer.Add((byte) value);
        _buffer.Add((byte) (value >> 8));
    }

    /// <inheritdoc />
    /// Zero-allocation: uses direct byte manipulation instead of BitConverter.
    public void Write(uint value) {
        if (_buffer == null) throw new InvalidOperationException("Cannot write to Read-Only Packet");
        _buffer.Add((byte) value);
        _buffer.Add((byte) (value >> 8));
        _buffer.Add((byte) (value >> 16));
        _buffer.Add((byte) (value >> 24));
    }

    /// <inheritdoc />
    /// Zero-allocation: uses direct byte manipulation instead of BitConverter.
    public void Write(ulong value) {
        if (_buffer == null) throw new InvalidOperationException("Cannot write to Read-Only Packet");
        _buffer.Add((byte) value);
        _buffer.Add((byte) (value >> 8));
        _buffer.Add((byte) (value >> 16));
        _buffer.Add((byte) (value >> 24));
        _buffer.Add((byte) (value >> 32));
        _buffer.Add((byte) (value >> 40));
        _buffer.Add((byte) (value >> 48));
        _buffer.Add((byte) (value >> 56));
    }

    /// <inheritdoc />
    public void Write(sbyte value) {
        if (_buffer == null) throw new InvalidOperationException("Cannot write to Read-Only Packet");
        _buffer.Add((byte) value);
    }

    /// <inheritdoc />
    /// Zero-allocation: uses direct byte manipulation instead of BitConverter.
    public void Write(short value) {
        if (_buffer == null) throw new InvalidOperationException("Cannot write to Read-Only Packet");
        _buffer.Add((byte) value);
        _buffer.Add((byte) (value >> 8));
    }

    /// <inheritdoc />
    /// Zero-allocation: uses direct byte manipulation instead of BitConverter.
    public void Write(int value) {
        if (_buffer == null) throw new InvalidOperationException("Cannot write to Read-Only Packet");
        _buffer.Add((byte) value);
        _buffer.Add((byte) (value >> 8));
        _buffer.Add((byte) (value >> 16));
        _buffer.Add((byte) (value >> 24));
    }

    /// <inheritdoc />
    /// Zero-allocation: uses direct byte manipulation instead of BitConverter.
    public void Write(long value) {
        if (_buffer == null) throw new InvalidOperationException("Cannot write to Read-Only Packet");
        _buffer.Add((byte) value);
        _buffer.Add((byte) (value >> 8));
        _buffer.Add((byte) (value >> 16));
        _buffer.Add((byte) (value >> 24));
        _buffer.Add((byte) (value >> 32));
        _buffer.Add((byte) (value >> 40));
        _buffer.Add((byte) (value >> 48));
        _buffer.Add((byte) (value >> 56));
    }

    #endregion

    #region Writing floating-point numeric types

    /// <inheritdoc />
    /// Zero-allocation: uses unsafe pointer casting to reinterpret bits as int.
    /// This avoids BitConverter.GetBytes() allocation.
    public unsafe void Write(float value) {
        Write(*(int*) &value);
    }

    /// <inheritdoc />
    /// Zero-allocation: uses unsafe pointer casting to reinterpret bits as long.
    /// This avoids BitConverter.GetBytes() allocation.
    public unsafe void Write(double value) {
        Write(*(long*) &value);
    }

    #endregion

    #region Writing other types

    /// <inheritdoc />
    public void Write(bool value) {
        if (_buffer == null) throw new InvalidOperationException("Cannot write to Read-Only Packet");
        _buffer.Add(value ? (byte) 1 : (byte) 0);
    }

    /// <inheritdoc />
    public void Write(string value) {
        // Encode the string into a byte array with UTF-8
        var byteEncodedString = Encoding.UTF8.GetBytes(value);

        // Check whether we can actually write the length of this string in a unsigned short
        if (byteEncodedString.Length > ushort.MaxValue) {
            throw new Exception($"Could not write string of length: {byteEncodedString.Length} to packet");
        }

        // Write the length of the encoded string and then the byte array itself
        Write((ushort) byteEncodedString.Length);
        Write(byteEncodedString);
    }

    /// <inheritdoc />
    public void Write(Vector2 value) {
        Write(value.X);
        Write(value.Y);
    }

    /// <inheritdoc />
    public void Write(Vector3 value) {
        Write(value.X);
        Write(value.Y);
        Write(value.Z);
    }

    /// <inheritdoc />
    public void WriteBitFlag<TEnum>(ISet<TEnum> set) where TEnum : Enum {
        var enumTypes = Enum.GetValues(typeof(TEnum));
        var enumLength = enumTypes.Length;

        ulong flag = 0;
        ulong currentValue = 1;

        for (var i = 0; i < enumLength; i++) {
            if (set.Contains((TEnum) enumTypes.GetValue(i))) {
                flag |= currentValue;
            }

            currentValue *= 2;
        }

        switch (enumLength) {
            case <= 8:
                Write((byte) flag);
                break;
            case <= 16:
                Write((ushort) flag);
                break;
            case <= 32:
                Write((uint) flag);
                break;
            case <= 64:
                Write(flag);
                break;
        }
    }

    #endregion

    #region Reading integral numeric types

    /// <inheritdoc />
    public byte ReadByte() {
        // Check whether there is at least 1 byte left to read
        if (_buffer != null ? _buffer.Count > _readPos : Length > _readPos) {
            var value = _readableBuffer[_offset + _readPos];

            // Increase reading position in the buffer
            _readPos += 1;

            return value;
        }

        throw new Exception("Could not read value of type 'byte'!");
    }

    /// <inheritdoc />
    public ushort ReadUShort() {
        // Check whether there are at least 2 bytes left to read
        if (_buffer != null ? _buffer.Count > _readPos + 1 : Length > _readPos + 1) {
            var value = BitConverter.ToUInt16(_readableBuffer, _offset + _readPos);

            // Increase the reading position in the buffer
            _readPos += 2;

            return value;
        }

        throw new Exception("Could not read value of type 'ushort'!");
    }

    /// <inheritdoc />
    public uint ReadUInt() {
        // Check whether there are at least 4 bytes left to read
        if (_buffer != null ? _buffer.Count > _readPos + 3 : Length > _readPos + 3) {
            var value = BitConverter.ToUInt32(_readableBuffer, _offset + _readPos);

            // Increase the reading position in the buffer
            _readPos += 4;

            return value;
        }

        throw new Exception("Could not read value of type 'uint'!");
    }

    /// <inheritdoc />
    public ulong ReadULong() {
        // Check whether there are at least 8 bytes left to read
        if (_buffer != null ? _buffer.Count > _readPos + 7 : Length > _readPos + 7) {
            var value = BitConverter.ToUInt64(_readableBuffer, _offset + _readPos);

            // Increase the reading position in the buffer
            _readPos += 8;

            return value;
        }

        throw new Exception("Could not read value of type 'ulong'!");
    }

    /// <inheritdoc />
    public sbyte ReadSByte() {
        // Check whether there are at least 1 byte left to read
        if (_buffer != null ? _buffer.Count > _readPos : Length > _readPos) {
            var value = (sbyte) _readableBuffer[_offset + _readPos];

            // Increase the reading position in the buffer
            _readPos += 1;

            return value;
        }

        throw new Exception("Could not read value of type 'sbyte'!");
    }

    /// <inheritdoc />
    public short ReadShort() {
        // Check whether there are at least 2 bytes left to read
        if (_buffer != null ? _buffer.Count > _readPos + 1 : Length > _readPos + 1) {
            var value = BitConverter.ToInt16(_readableBuffer, _offset + _readPos);

            // Increase the reading position in the buffer
            _readPos += 2;

            return value;
        }

        throw new Exception("Could not read value of type 'short'!");
    }

    /// <inheritdoc />
    public int ReadInt() {
        // Check whether there are at least 4 bytes left to read
        if (_buffer != null ? _buffer.Count > _readPos + 3 : Length > _readPos + 3) {
            var value = BitConverter.ToInt32(_readableBuffer, _offset + _readPos);

            // Increase the reading position in the buffer
            _readPos += 4;

            return value;
        }

        throw new Exception("Could not read value of type 'int'!");
    }

    /// <inheritdoc />
    public long ReadLong() {
        // Check whether there are at least 8 bytes left to read
        if (_buffer != null ? _buffer.Count > _readPos + 7 : Length > _readPos + 7) {
            var value = BitConverter.ToInt64(_readableBuffer, _offset + _readPos);

            // Increase the reading position in the buffer
            _readPos += 8;

            return value;
        }

        throw new Exception("Could not read value of type 'long'!");
    }

    #endregion

    #region Reading floating-point numeric types

    /// <inheritdoc />
    public float ReadFloat() {
        // Check whether there are at least 4 bytes left to read
        if (_buffer != null ? _buffer.Count > _readPos + 3 : Length > _readPos + 3) {
            var value = BitConverter.ToSingle(_readableBuffer, _offset + _readPos);

            // Increase the reading position in the buffer
            _readPos += 4;

            return value;
        }

        throw new Exception("Could not read value of type 'float'!");
    }

    /// <inheritdoc />
    public double ReadDouble() {
        // Check whether there are at least 8 bytes left to read
        if (_buffer != null ? _buffer.Count > _readPos + 7 : Length > _readPos + 7) {
            var value = BitConverter.ToDouble(_readableBuffer, _offset + _readPos);

            // Increase the reading position in the buffer
            _readPos += 8;

            return value;
        }

        throw new Exception("Could not read value of type 'double'!");
    }

    #endregion

    #region Reading other types

    /// <inheritdoc />
    public bool ReadBool() {
        // Check whether there is at least 1 byte left to read
        if (_buffer != null ? _buffer.Count > _readPos : Length > _readPos) {
            var value = BitConverter.ToBoolean(_readableBuffer, _offset + _readPos);

            // Increase the reading position in the buffer
            _readPos += 1;

            return value;
        }

        throw new Exception("Could not read value of type 'bool'!");
    }

    /// <inheritdoc />
    public string ReadString() {
        // First read the length of the string as an unsigned short, which implicitly checks
        // whether there are at least 2 bytes left to read
        var length = ReadUShort();

        // Edge case if the length is zero, we simply return an empty string already
        if (length == 0) {
            return "";
        }

        // Now we check whether there are at least as many bytes left to read as the length of the string
        if ((_buffer?.Count ?? Length) < _readPos + length) {
            throw new Exception("Could not read value of type 'string'!");
        }

        // Now we read and decode the string
        var value = Encoding.UTF8.GetString(_readableBuffer, _offset + _readPos, length);

        // Increase the reading position in the buffer
        _readPos += length;

        return value;
    }

    /// <inheritdoc />
    public Vector2 ReadVector2() {
        // Simply construct the Vector2 by reading a float from the packet twice, which should
        // check whether there are enough bytes left to read and throw exceptions if not
        return new Vector2(ReadFloat(), ReadFloat());
    }

    /// <inheritdoc />
    public Vector3 ReadVector3() {
        // Simply construct the Vector3 by reading a float from the packet thrice, which should
        // check whether there are enough bytes left to read and throw exceptions if not
        return new Vector3(ReadFloat(), ReadFloat(), ReadFloat());
    }

    /// <inheritdoc />
    public ISet<TEnum> ReadBitFlag<TEnum>() where TEnum : Enum {
        var enumTypes = Enum.GetValues(typeof(TEnum));
        var enumLength = enumTypes.Length;

        ulong flag = enumLength switch {
            <= 8 => ReadByte(),
            <= 16 => ReadUShort(),
            <= 32 => ReadUInt(),
            <= 64 => ReadULong(),
            _ => 0
        };

        ulong currentValue = 1;
        var set = new HashSet<TEnum>();
        foreach (var enumType in enumTypes) {
            if ((flag & currentValue) != 0) {
                set.Add((TEnum) enumType);
            }

            currentValue *= 2;
        }

        return set;
    }

    #endregion

    #endregion
}
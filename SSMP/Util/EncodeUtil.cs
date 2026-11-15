using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SSMP.Collection;
using SSMP.Game.Client.Save;
using SSMP.Game.Server.Save;
using SSMP.Math;
using SSMP.Serialization;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Util;

/// <summary>
/// Static class to help with encoding/decoding values to/from bytes.
/// </summary>
public static class EncodeUtil {
    #region Constants

    /// <summary>
    /// The file path of the embedded resource file for string data.
    /// </summary>
    private const string StringDataFilePath = "SSMP.Resource.string-data.json";

    /// <summary>
    /// The size of the count at the start of an encoded string list, hashset, vector list or byte array.
    /// </summary>
    private const int CountPrefixSize = sizeof(ushort);

    /// <summary>
    /// The size of the index and the length at the start of a dynamically encoded string.
    /// </summary>
    private const int DynamicStringHeaderSize = 2 * sizeof(ushort);

    /// <summary>
    /// The size of an encoded Vector2.
    /// </summary>
    private const int Vector2Size = 2 * sizeof(float);

    /// <summary>
    /// The size of an encoded Vector3.
    /// </summary>
    private const int Vector3Size = 3 * sizeof(float);

    /// <summary>
    /// The variable type in the save data for a list of strings.
    /// </summary>
    private const string StringListType = "System.Collections.Generic.List`1[System.String]";

    /// <summary>
    /// The variable type in the save data for a hash set of strings.
    /// </summary>
    private const string StringSetType = "HashSet<string>";

    /// <summary>
    /// The variable type in the save data for a list of vectors.
    /// </summary>
    private const string Vector3ListType = "System.Collections.Generic.List`1[SSMP.Math.Vector3]";

    /// <summary>
    /// The variable type in the save data for a list of integers.
    /// </summary>
    private const string Int32ListType = "System.Collections.Generic.List`1[System.Int32]";

    /// <summary>
    /// The variable type in the save data for a byte array.
    /// </summary>
    private const string ByteArrayType = "System.Byte[]";

    #endregion

    #region Static initialisation

    /// <summary>
    /// Bi-directional lookup that maps strings (for encoding) to their indices.
    /// </summary>
    private static readonly BiLookup<string?, ushort> StringIndices;

    /// <summary>
    /// Static construct to load the scene indices.
    /// </summary>
    static EncodeUtil() {
        StringIndices = new BiLookup<string?, ushort>();

        var strings = FileUtil.LoadObjectFromEmbeddedJson<List<string?>>(StringDataFilePath);
        if (strings == null) {
            throw new InvalidDataException("Could not deserialize strings from embedded JSON");
        }

        ushort index = 0;
        foreach (var str in strings) {
            StringIndices.Add(str, index++);
        }
    }

    #endregion

    #region Public methods

    /// <summary>
    /// Encode a given value into a byte array in the context of save data.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="name">Optional name of the variable to assist with encoding nulls.</param>
    /// <returns>A byte array containing the encoded value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the given value is out of range to be encoded.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when the value has an unsupported type.</exception>
    public static byte[] EncodeSaveDataValue(object? value, string? name = null) {
        if (value == null) {
            return EncodeNullSaveDataValue(name);
        }

        return value switch {
            bool boolean              => [(byte) (boolean ? 1 : 0)],
            Enum enumValue            => BitConverter.GetBytes(Convert.ToInt32(enumValue)),
            float single              => BitConverter.GetBytes(single),
            int integer               => BitConverter.GetBytes(integer),
            ulong unsignedLong        => BitConverter.GetBytes(unsignedLong),
            long signedLong           => BitConverter.GetBytes(signedLong),
            uint unsignedInteger      => BitConverter.GetBytes(unsignedInteger),
            ushort unsignedShort      => BitConverter.GetBytes(unsignedShort),
            short signedShort         => BitConverter.GetBytes(signedShort),
            double doubleValue        => BitConverter.GetBytes(doubleValue),
            string text               => EncodeString(text),
            Vector2 vector2           => EncodeVector2(vector2),
            Vector3 vector3           => EncodeVector3(vector3),
            byte[] bytes              => EncodeByteArray(bytes),
            List<string> stringList   => EncodeCollection(stringList, "string list", EncodeString),
            HashSet<string> stringSet => EncodeCollection(stringSet, "hashset string list", EncodeString),
            List<Vector3> vectors     => EncodeCollection(vectors, "vector list", EncodeVector3),
            MapZone mapZone           => [(byte) mapZone],
            List<int> integers        => EncodeInt32List(integers),
            _ => throw new ArgumentException($"No encoding implementation for type: {value.GetType()}")
        };
    }

    /// <summary>
    /// Decode a given save data value from its name and encoded byte array. This only supports values from PlayerData.
    /// </summary>
    /// <param name="name">The variable name from PlayerData.</param>
    /// <param name="encodedValue">The encoded value as a byte array.</param>
    /// <returns>The decoded object.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the given name does not correspond with a PlayerData
    /// value that can be decoded, because its variable properties do not exist.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the length of the given byte array does not match
    /// the value that should be decoded from it.</exception>
    /// <exception cref="ArgumentException">Thrown when the value can not be decoded for another reason.</exception>
    public static object? DecodeSaveDataValue(string? name, byte[] encodedValue) {
        if (name == null) {
            return null;
        }

        if (!SaveDataMapping.Instance.PlayerDataVarProperties.TryGetValue(name, out var varProperties)) {
            throw new InvalidOperationException(
                $"Could not decode save data value with name: \"{name}\", missing variable properties"
            );
        }

        return varProperties.VarType switch {
            "System.Boolean"             => DecodeBoolean(encodedValue),
            "System.Single"              => DecodeSingle(encodedValue),
            "System.Int32"               => DecodeInt32(encodedValue),
            "System.UInt64"              => DecodeUInt64(encodedValue),
            "System.Int64"               => DecodeInt64(encodedValue),
            "System.UInt32"              => DecodeUInt32(encodedValue),
            "System.UInt16"              => DecodeUInt16(encodedValue),
            "System.Int16"               => DecodeInt16(encodedValue),
            "System.Double"              => DecodeDouble(encodedValue),
            "System.String"              => DecodeExactString(encodedValue),
            "SSMP.Math.Vector2"          => DecodeVector2(encodedValue),
            "SSMP.Math.Vector3"          => DecodeVector3(encodedValue),
            "SSMP.Serialization.MapZone" => DecodeMapZone(encodedValue),
            StringListType               => DecodeStrings<List<string>>(encodedValue, "List<string>"),
            StringSetType                => DecodeStrings<HashSet<string>>(encodedValue, "HashSet<string>"),
            Vector3ListType              => DecodeVector3List(encodedValue),
            Int32ListType                => DecodeInt32List(encodedValue),
            ByteArrayType                => DecodeByteArray(encodedValue),
            var type              => throw new ArgumentException($"Could not decode type: {type}")
        };
    }

    #endregion

    #region Save data conversion

    /// <summary>
    /// Convert the given <see cref="ModSaveFile.SaveData"/> to a dictionary of raw indices and byte arrays.
    /// </summary>
    /// <param name="saveData">The save data to convert.</param>
    /// <returns>A dictionary of raw values.</returns>
    internal static Dictionary<ushort, byte[]> ConvertToServerSaveData(ModSaveFile.SaveData saveData) {
        // Create new dictionary for this player's specific save data
        var encodedSaveData = new Dictionary<ushort, byte[]>();

        // Loop through the entries in the player's save data
        foreach (var entry in saveData.PlayerDataEntries) {
            var name = entry.Name;
            var decodedObject = entry.Value;

            //Logger.Debug($"Encoding entry: {name}, {decodedObject}");

            CheckEncodeAddData(encodedSaveData, name, SaveDataMapping.Instance.PlayerDataIndices, decodedObject);
        }

        var sceneData = saveData.SceneData;
        foreach (var geoRockData in sceneData.GeoRockData) {
            CheckEncodeAddData(
                encodedSaveData,
                geoRockData.GetKey(),
                SaveDataMapping.Instance.GeoRockIndices,
                geoRockData.HitsLeft
            );
        }

        foreach (var persistentBoolData in sceneData.PersistentBoolData) {
            CheckEncodeAddData(
                encodedSaveData,
                persistentBoolData.GetKey(),
                SaveDataMapping.Instance.PersistentBoolIndices,
                persistentBoolData.Activated
            );
        }

        foreach (var persistentIntData in sceneData.PersistentIntData) {
            CheckEncodeAddData(
                encodedSaveData,
                persistentIntData.GetKey(),
                SaveDataMapping.Instance.PersistentIntIndices,
                persistentIntData.Value
            );
        }

        return encodedSaveData;
    }

    private static void CheckEncodeAddData<TKey>(
        Dictionary<ushort, byte[]> encodedSaveData,
        TKey key,
        BiLookup<TKey, ushort> lookup,
        object? decodedObject
    ) {
        if (!lookup.TryGetValue(key, out var index)) {
            Logger.Warn($"Could not find index for data for key: {key}");
            return;
        }

        // Try to encode the value into our byte array representation
        byte[] encodedValue;
        try {
            encodedValue = EncodeSaveDataValue(decodedObject, key as string);
        } catch (Exception e) {
            Logger.Warn(
                decodedObject == null
                    ? $"Could not encode null save data value, exception:\n{e}"
                    : $"Could not encode save data value of type: {decodedObject.GetType()}, exception:\n{e}"
            );

            return;
        }

        // Finally, store the value in the dictionary we created for the player
        encodedSaveData[index] = encodedValue;
    }

    /// <summary>
    /// Convert the given dictionary of raw indices and byte arrays to <see cref="ModSaveFile.SaveData"/>.
    /// </summary>
    /// <param name="encodedSaveData">The dictionary containing raw values.</param>
    /// <returns>An instance of save data with the converted values.</returns>
    internal static ModSaveFile.SaveData ConvertFromServerSaveData(Dictionary<ushort, byte[]> encodedSaveData) {
        var mapping = SaveDataMapping.Instance;
        var saveData = new ModSaveFile.SaveData();
        var sceneData = saveData.SceneData;

        foreach (var (index, encodedValue) in encodedSaveData) {
            try {
                if (mapping.PlayerDataIndices.TryGetValue(index, out var pdName)) {
                    saveData.PlayerDataEntries.Add(
                        new ModSaveFile.PlayerDataEntry {
                            Name = pdName,
                            Value = DecodeSaveDataValue(pdName, encodedValue)
                        }
                    );
                } else if (mapping.GeoRockIndices.TryGetValue(index, out var itemKey)) {
                    sceneData.GeoRockData.Add(
                        new ModSaveFile.GeoRockData {
                            Id = itemKey.Id,
                            SceneName = itemKey.SceneName,
                            HitsLeft = encodedValue[0]
                        }
                    );
                } else if (mapping.PersistentBoolIndices.TryGetValue(index, out itemKey)) {
                    sceneData.PersistentBoolData.Add(
                        new ModSaveFile.PersistentBoolData {
                            Id = itemKey.Id,
                            SceneName = itemKey.SceneName,
                            Activated = encodedValue[0] == 1
                        }
                    );
                } else if (mapping.PersistentIntIndices.TryGetValue(index, out itemKey)) {
                    sceneData.PersistentIntData.Add(
                        new ModSaveFile.PersistentIntData {
                            Id = itemKey.Id,
                            SceneName = itemKey.SceneName,
                            Value = encodedValue[0]
                        }
                    );
                } else {
                    Logger.Warn($"Could not find key for index: {index}");
                }
            } catch (Exception e) {
                Logger.Warn($"Could not decode save data value with index: {index}, exception:\n{e}");
            }
        }

        return saveData;
    }

    #endregion

    #region Validation

    private static ushort ReadCollectionCount(byte[] encoded) {
        EnsureAvailable(encoded, 0, CountPrefixSize);
        return BitConverter.ToUInt16(encoded, 0);
    }

    private static void ValidateLength(byte[] encoded, int expected, string type) {
        if (encoded.Length != expected) {
            throw new ArgumentOutOfRangeException(
                nameof(encoded),
                $"Encoded value has incorrect value length for {type}: {encoded.Length}; expected {expected}"
            );
        }
    }

    private static void EnsureAvailable(byte[] encoded, int offset, int count) {
        if (count > encoded.Length - offset) {
            throw new ArgumentOutOfRangeException(
                nameof(encoded),
                $"Encoded value is truncated at offset {offset}; requires {count} bytes, " +
                $"but only {encoded.Length} bytes are available"
            );
        }
    }

    #endregion

    #region Encoding

    private static byte[] EncodeNullSaveDataValue(string? name) {
        // A null has no runtime type, so the variable type decides the encoding. Collections are encoded as empty,
        // because decoding them reads a count first. Anything else is encoded as a null string.
        var type = name != null &&
                   SaveDataMapping.Instance.PlayerDataVarProperties.TryGetValue(name, out var properties)
            ? properties.VarType
            : null;

        return type switch {
            StringSetType or StringListType or Vector3ListType or ByteArrayType => [0, 0], // 0 count ushort
            Int32ListType => [0], // 0 count byte
            _ => EncodeString(null)
        };
    }

    private static byte[] EncodeByteArray(byte[] values) {
        if (values.Length > ushort.MaxValue) {
            throw new ArgumentOutOfRangeException($"Could not encode byte array length: {values.Length}");
        }

        var encoded = new byte[CountPrefixSize + values.Length];
        BitConverter.TryWriteBytes(encoded, (ushort) values.Length);
        values.CopyTo(encoded, CountPrefixSize);
        return encoded;
    }

    private static byte[] EncodeCollection<T>(
        IReadOnlyCollection<T> values,
        string kind,
        Func<T, byte[]> encodeItem
    ) {
        if (values.Count > ushort.MaxValue) {
            throw new ArgumentOutOfRangeException($"Could not encode {kind} length: {values.Count}");
        }

        using var stream = new MemoryStream();
        var length = BitConverter.GetBytes((ushort) values.Count);
        stream.Write(length, 0, length.Length);

        foreach (var value in values) {
            var encoded = encodeItem(value);
            stream.Write(encoded, 0, encoded.Length);
        }

        return stream.ToArray();
    }

    private static byte[] EncodeInt32List(List<int> values) {
        if (values.Count > byte.MaxValue) {
            throw new ArgumentOutOfRangeException($"Could not encode int list length: {values.Count}");
        }

        // Preserve the existing protocol: one byte for the count and one byte per integer.
        var encoded = new byte[values.Count + 1];
        encoded[0] = (byte) values.Count;

        for (var i = 0; i < values.Count; i++) {
            // Only one byte is sent per integer, so a value outside this range arrives as a different number
            if (values[i] is < 0 or > byte.MaxValue) {
                Logger.Warn($"Integer list value does not fit in a byte and will change: {values[i]}");
            }

            encoded[i + 1] = (byte) values[i];
        }

        return encoded;
    }

    private static byte[] EncodeString(string? stringValue) {
        // A null string is the special index ushort.MaxValue followed by the special length ushort.MaxValue
        if (stringValue == null) {
            return [255, 255, 255, 255];
        }

        // Known strings are encoded as just their ushort index to save bandwidth. Others are encoded as the special
        // index, a ushort length and the UTF-8 bytes, so the length must stay below the special length.
        if (!StringIndices.TryGetValue(stringValue, out var index)) {
            Logger.Debug($"String '{stringValue}' not found in static indices, encoding dynamically.");
            var utf8Bytes = Encoding.UTF8.GetBytes(stringValue);
            if (utf8Bytes.Length > ushort.MaxValue - 3) {
                throw new ArgumentOutOfRangeException($"String is too long to encode: {stringValue}");
            }

            var result = new byte[DynamicStringHeaderSize + utf8Bytes.Length];
            // The special index that indicates a dynamically encoded string
            BitConverter.TryWriteBytes(result, ushort.MaxValue);
            // String length
            BitConverter.TryWriteBytes(result.AsSpan(sizeof(ushort)), (ushort) utf8Bytes.Length);
            // String bytes
            utf8Bytes.CopyTo(result, DynamicStringHeaderSize);
            return result;
        }

        return BitConverter.GetBytes(index);
    }

    private static byte[] EncodeVector2(Vector2 vec2Value) {
        var encoded = new byte[Vector2Size];
        BitConverter.TryWriteBytes(encoded, vec2Value.X);
        BitConverter.TryWriteBytes(encoded.AsSpan(sizeof(float)), vec2Value.Y);
        return encoded;
    }

    private static byte[] EncodeVector3(Vector3 vec3Value) {
        var encoded = new byte[Vector3Size];
        BitConverter.TryWriteBytes(encoded, vec3Value.X);
        BitConverter.TryWriteBytes(encoded.AsSpan(sizeof(float)), vec3Value.Y);
        BitConverter.TryWriteBytes(encoded.AsSpan(2 * sizeof(float)), vec3Value.Z);
        return encoded;
    }

    #endregion

    #region Decoding

    private static bool DecodeBoolean(byte[] encoded) {
        ValidateLength(encoded, sizeof(bool), "bool");
        return encoded[0] == 1;
    }

    private static float DecodeSingle(byte[] encoded) {
        ValidateLength(encoded, sizeof(float), "float");
        return BitConverter.ToSingle(encoded, 0);
    }

    private static int DecodeInt32(byte[] encoded) {
        ValidateLength(encoded, sizeof(int), "int");
        return BitConverter.ToInt32(encoded, 0);
    }

    private static ulong DecodeUInt64(byte[] encoded) {
        ValidateLength(encoded, sizeof(ulong), "ulong");
        return BitConverter.ToUInt64(encoded, 0);
    }

    private static long DecodeInt64(byte[] encoded) {
        ValidateLength(encoded, sizeof(long), "long");
        return BitConverter.ToInt64(encoded, 0);
    }

    private static uint DecodeUInt32(byte[] encoded) {
        ValidateLength(encoded, sizeof(uint), "uint");
        return BitConverter.ToUInt32(encoded, 0);
    }

    private static ushort DecodeUInt16(byte[] encoded) {
        ValidateLength(encoded, sizeof(ushort), "ushort");
        return BitConverter.ToUInt16(encoded, 0);
    }

    private static short DecodeInt16(byte[] encoded) {
        ValidateLength(encoded, sizeof(short), "short");
        return BitConverter.ToInt16(encoded, 0);
    }

    private static double DecodeDouble(byte[] encoded) {
        ValidateLength(encoded, sizeof(double), "double");
        return BitConverter.ToDouble(encoded, 0);
    }

    private static Vector2 DecodeVector2(byte[] encoded) {
        ValidateLength(encoded, Vector2Size, "Vector2");
        return new Vector2(
            BitConverter.ToSingle(encoded, 0),
            BitConverter.ToSingle(encoded, sizeof(float))
        );
    }

    private static Vector3 DecodeVector3(byte[] encoded) {
        ValidateLength(encoded, Vector3Size, "Vector3");
        return DecodeVector3At(encoded, 0);
    }

    private static Vector3 DecodeVector3At(byte[] encoded, int offset) {
        // The caller validates that 12 bytes are available at the given offset.
        return new Vector3(
            BitConverter.ToSingle(encoded, offset),
            BitConverter.ToSingle(encoded, offset + sizeof(float)),
            BitConverter.ToSingle(encoded, offset + 2 * sizeof(float))
        );
    }

    private static MapZone DecodeMapZone(byte[] encoded) {
        ValidateLength(encoded, sizeof(byte), "MapZone");
        return (MapZone) encoded[0];
    }

    private static string? DecodeExactString(byte[] encoded) {
        var offset = 0;
        var value = DecodeString(encoded, ref offset);

        // Unlike a string in a collection, this string has to fill the entire array
        ValidateLength(encoded, offset, "string");
        return value;
    }

    private static TCollection DecodeStrings<TCollection>(byte[] encoded, string type)
        where TCollection : ICollection<string>, new() {
        var count = ReadCollectionCount(encoded);
        var values = new TCollection();
        var offset = CountPrefixSize;

        for (var i = 0; i < count; i++) {
            values.Add(DecodeString(encoded, ref offset)!);
        }

        ValidateLength(encoded, offset, type);
        return values;
    }

    private static List<Vector3> DecodeVector3List(byte[] encoded) {
        var count = ReadCollectionCount(encoded);
        ValidateLength(encoded, CountPrefixSize + count * Vector3Size, "List<Vector3>");

        var values = new List<Vector3>(count);
        for (var i = 0; i < count; i++) {
            values.Add(DecodeVector3At(encoded, CountPrefixSize + i * Vector3Size));
        }

        return values;
    }

    private static List<int> DecodeInt32List(byte[] encoded) {
        // The existing wire format stores a byte count and one byte per int.
        // Do not change this to four-byte ints without a versioned protocol migration.
        EnsureAvailable(encoded, 0, sizeof(byte));
        var count = encoded[0];
        ValidateLength(encoded, sizeof(byte) + count, "List<int>");

        var values = new List<int>(count);
        for (var i = 0; i < count; i++) {
            values.Add(encoded[i + 1]);
        }

        return values;
    }

    private static byte[] DecodeByteArray(byte[] encoded) {
        var count = ReadCollectionCount(encoded);
        ValidateLength(encoded, CountPrefixSize + count, "byte[]");
        return [.. encoded.AsSpan(CountPrefixSize, count)];
    }

    private static string? DecodeString(byte[] encoded, ref int offset) {
        // The offset is moved past the string, so that collections can decode their strings in sequence
        EnsureAvailable(encoded, offset, sizeof(ushort));
        var sceneIndex = BitConverter.ToUInt16(encoded, offset);

        if (sceneIndex != ushort.MaxValue) {
            offset += sizeof(ushort);
            return StringIndices.TryGetValue(sceneIndex, out var name)
                ? name
                : throw new ArgumentException($"Could not decode string from save update: {sceneIndex}");
        }

        // The special index indicates a dynamically encoded string
        EnsureAvailable(encoded, offset, DynamicStringHeaderSize);
        var strLen = BitConverter.ToUInt16(encoded, offset + sizeof(ushort));

        // The special length indicates a null string
        if (strLen == ushort.MaxValue) {
            offset += DynamicStringHeaderSize;
            return null;
        }

        EnsureAvailable(encoded, offset + DynamicStringHeaderSize, strLen);
        var decoded = Encoding.UTF8.GetString(encoded, offset + DynamicStringHeaderSize, strLen);
        offset += DynamicStringHeaderSize + strLen;
        return decoded;
    }

    #endregion
}

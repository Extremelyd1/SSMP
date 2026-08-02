using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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
    /// <summary>
    /// The file path of the embedded resource file for string data.
    /// </summary>
    private const string StringDataFilePath = "SSMP.Resource.string-data.json";

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

    /// <summary>
    /// Get a single byte for the given array of booleans where each bit represents a boolean from the array.
    /// </summary>
    /// <param name="bits">An array of booleans of at most length 8.</param>
    /// <returns>A byte representing the booleans.</returns>
    private static byte GetByte(bool[] bits) {
        byte result = 0;
        for (var i = 0; i < bits.Length; i++) {
            if (bits[i]) {
                result |= (byte) (1 << i);
            }
        }

        return result;
    }

    /// <summary>
    /// Get a boolean array representing the given byte where each boolean is a bit from the byte.
    /// </summary>
    /// <param name="b">A byte that contains boolean for each bit.</param>
    /// <returns>An boolean array of length 8.</returns>
    private static bool[] GetBoolsFromByte(byte b) {
        var result = new bool[8];
        for (var i = 0; i < result.Length; i++) {
            result[i] = (b & (1 << i)) > 0;
        }

        return result;
    }

    /// <summary>
    /// Try to get the string index corresponding to the given string for encoding/decoding purposes.
    /// </summary>
    /// <param name="sceneName">The string.</param>
    /// <param name="index">The index of the string or default if the string could not be found.</param>
    /// <returns>true if there is a corresponding index for the given string, false otherwise.</returns>
    private static bool TryGetStringIndex(string? sceneName, out ushort index) {
        return StringIndices.TryGetValue(sceneName, out index);
    }

    /// <summary>
    /// Try to get the string corresponding to the given string index for encoding/decoding purposes.
    /// </summary>
    /// <param name="index">The string.</param>
    /// <param name="sceneName">The string or default if the string index could not be found.</param>
    /// <returns>true if there is a corresponding string for the given index, false otherwise.</returns>
    private static bool TryGetStringName(ushort index, [MaybeNullWhen(false)] out string sceneName) {
        return StringIndices.TryGetValue(index, out sceneName);
    }

    /// <summary>
    /// Encode a given value into a byte array in the context of save data.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="name">Optional name of the variable to assist with encoding nulls.</param>
    /// <returns>A byte array containing the encoded value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the given value is out of range to be encoded.
    /// </exception>
    /// <exception cref="NotImplementedException">Thrown when the given value has a type that cannot be encoded due to
    /// missing implementation.</exception>
    public static byte[] EncodeSaveDataValue(object? value, string? name = null) {
        if (value == null) {
            if (name != null && SaveDataMapping.Instance.PlayerDataVarProperties.TryGetValue(name, out var varProps)) {
                var type = varProps.VarType;
                switch (type) {
                    case "System.String":
                        // Sentinel index ushort.MaxValue (65535) and dynamic string prefix ushort.MaxValue for null
                        return [255, 255, 255, 255];
                    case "System.Collections.Generic.List`1[System.String]":
                    case "HashSet<string>":
                    case "System.Collections.Generic.List`1[SSMP.Math.Vector3]":
                    case "System.Byte[]":
                        return [0, 0]; // 0 length ushort
                    case "System.Collections.Generic.List`1[System.Int32]":
                        return [0]; // 0 length byte
                }
            }

            // Fallback representation for unspecified nulls: default empty string/list bytes
            return [255, 255, 255, 255];
        }

        switch (value) {
            case bool bValue:
                return [(byte) (bValue ? 1 : 0)];
            case Enum enumValue:
                return BitConverter.GetBytes(Convert.ToInt32(enumValue));
            case float fValue:
                return BitConverter.GetBytes(fValue);
            case int iValue:
                return BitConverter.GetBytes(iValue);
            case ulong ulValue:
                return BitConverter.GetBytes(ulValue);
            case long lValue:
                return BitConverter.GetBytes(lValue);
            case uint uiValue:
                return BitConverter.GetBytes(uiValue);
            case ushort usValue:
                return BitConverter.GetBytes(usValue);
            case short sValue:
                return BitConverter.GetBytes(sValue);
            case double dValue:
                return BitConverter.GetBytes(dValue);
            case string sValue:
                return EncodeString(sValue);
            case Vector2 vec2Value:
                return EncodeVector2(vec2Value);
            case Vector3 vecValue:
                return EncodeVector3(vecValue);
            case byte[] { Length: > ushort.MaxValue } byteArrayValue:
                throw new ArgumentOutOfRangeException($"Could not encode byte array length: {byteArrayValue.Length}");
            case byte[] byteArrayValue:
                return BitConverter.GetBytes((ushort) byteArrayValue.Length)
                                   .Concat(byteArrayValue)
                                   .ToArray();
            case List<string> { Count: > ushort.MaxValue } listValue:
                throw new ArgumentOutOfRangeException($"Could not encode string list length: {listValue.Count}");
            case List<string> listValue: {
                var length = (ushort) listValue.Count;

                IEnumerable<byte> byteArray = BitConverter.GetBytes(length);

                for (var i = 0; i < length; i++) {
                    var encoded = EncodeString(listValue[i]);

                    byteArray = byteArray.Concat(encoded);
                }

                return byteArray.ToArray();
            }
            case HashSet<string> { Count: > ushort.MaxValue } hashSetValue:
                throw new ArgumentOutOfRangeException(
                    $"Could not encode hashset string list length: {hashSetValue.Count}"
                );
            case HashSet<string> hashSetValue: {
                var listValue = hashSetValue.ToList();
                var length = (ushort) listValue.Count;

                IEnumerable<byte> byteArray = BitConverter.GetBytes(length);

                for (var i = 0; i < length; i++) {
                    var encoded = EncodeString(listValue[i]);

                    byteArray = byteArray.Concat(encoded);
                }

                return byteArray.ToArray();
            }
            case BossSequenceDoorCompletion bsdCompValue: {
                // For now we only encode the bools of completion struct
                var firstBools = new[] {
                    bsdCompValue.CanUnlock, bsdCompValue.Unlocked, bsdCompValue.Completed, bsdCompValue.AllBindings,
                    bsdCompValue.NoHits, bsdCompValue.BoundNail, bsdCompValue.BoundShell, bsdCompValue.BoundCharms
                };

                var byte1 = GetByte(firstBools);

                var byte2 = (byte) (bsdCompValue.BoundSoul ? 1 : 0);

                return [byte1, byte2];
            }
            case BossStatueCompletion bsCompValue: {
                var bools = new[] {
                    bsCompValue.HasBeenSeen, bsCompValue.IsUnlocked, bsCompValue.CompletedTier1,
                    bsCompValue.CompletedTier2,
                    bsCompValue.CompletedTier3, bsCompValue.SeenTier3Unlock, bsCompValue.UsingAltVersion
                };

                return [GetByte(bools)];
            }
            case List<Vector3> { Count: > ushort.MaxValue } vecListValue:
                throw new ArgumentOutOfRangeException($"Could not encode vector list length: {vecListValue.Count}");
            case List<Vector3> vecListValue: {
                var length = (ushort) vecListValue.Count;

                IEnumerable<byte> byteArray = BitConverter.GetBytes(length);

                for (var i = 0; i < length; i++) {
                    var encoded = EncodeVector3(vecListValue[i]);

                    byteArray = byteArray.Concat(encoded);
                }

                return byteArray.ToArray();
            }
            case MapZone mapZone:
                return [(byte) mapZone];
            case List<int> { Count: > byte.MaxValue } intListValue:
                throw new ArgumentOutOfRangeException($"Could not encode int list length: {intListValue.Count}");
            case List<int> intListValue: {
                var length = (byte) intListValue.Count;

                // Create a byte array for the encoded result that has the size of the length of the int list plus one
                // for the length itself
                var byteArray = new byte[length + 1];
                byteArray[0] = length;

                for (var i = 0; i < length; i++) {
                    byteArray[i + 1] = (byte) intListValue[i];
                }

                return byteArray;
            }
        }

        throw new ArgumentException($"No encoding implementation for type: {value.GetType()}");

        // To preserve network bandwidth, we encode known strings into indices, since there is a limited number of
        // strings in the save data
        byte[] EncodeString(string? stringValue) {
            if (stringValue == null) {
                if (TryGetStringIndex(null, out var nullIndex)) {
                    return BitConverter.GetBytes(nullIndex);
                }

                return [255, 255, 255, 255];
            }

            if (!TryGetStringIndex(stringValue, out var index)) {
                Logger.Info($"String '{stringValue}' not found in static indices, encoding dynamically.");
                var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(stringValue);
                if (utf8Bytes.Length > ushort.MaxValue - 3) {
                    throw new ArgumentOutOfRangeException($"String is too long to encode: {stringValue}");
                }

                var result = new byte[2 + 2 + utf8Bytes.Length];
                // Sentinel index ushort.MaxValue (65535) indicates dynamic string
                Array.Copy(BitConverter.GetBytes(ushort.MaxValue), 0, result, 0, 2);
                // String length
                Array.Copy(BitConverter.GetBytes((ushort) utf8Bytes.Length), 0, result, 2, 2);
                // String bytes
                Array.Copy(utf8Bytes, 0, result, 4, utf8Bytes.Length);
                return result;
            }

            return BitConverter.GetBytes(index);
        }

        byte[] EncodeVector2(Vector2 vec2Value) {
            return BitConverter.GetBytes(vec2Value.X)
                               .Concat(BitConverter.GetBytes(vec2Value.Y))
                               .ToArray();
        }

        byte[] EncodeVector3(Vector3 vec3Value) {
            return BitConverter.GetBytes(vec3Value.X)
                               .Concat(BitConverter.GetBytes(vec3Value.Y))
                               .Concat(BitConverter.GetBytes(vec3Value.Z))
                               .ToArray();
        }
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

        if (!SaveDataMapping.Instance.PlayerDataVarProperties.TryGetValue(name, out var varProps)) {
            throw new InvalidOperationException(
                $"Could not decode save data value with name: \"{name}\", missing variable properties"
            );
        }

        var type = varProps.VarType;
        switch (type) {
            case "System.Boolean" when encodedValue.Length != 1:
                throw new ArgumentOutOfRangeException(
                    $"Encoded value has incorrect value length for bool: {encodedValue.Length}"
                );
            case "System.Boolean":
                return encodedValue[0] == 1;
            case "System.Single" when encodedValue.Length != 4:
                throw new ArgumentOutOfRangeException(
                    $"Encoded value has incorrect value length for float: {encodedValue.Length}"
                );
            case "System.Single":
                return BitConverter.ToSingle(encodedValue, 0);
            case "System.Int32" when encodedValue.Length == 8:
                return (int) BitConverter.ToInt64(encodedValue, 0);
            case "System.Int32" when encodedValue.Length == 2:
                return (int) BitConverter.ToInt16(encodedValue, 0);
            case "System.Int32" when encodedValue.Length == 1:
                return (int) encodedValue[0];
            case "System.Int32" when encodedValue.Length != 4:
                throw new ArgumentOutOfRangeException(
                    $"Encoded value has incorrect value length for int: {encodedValue.Length}"
                );
            case "System.Int32":
                return BitConverter.ToInt32(encodedValue, 0);
            case "System.UInt64" when encodedValue.Length == 4:
                return (ulong) BitConverter.ToUInt32(encodedValue, 0);
            case "System.UInt64" when encodedValue.Length == 2:
                return (ulong) BitConverter.ToUInt16(encodedValue, 0);
            case "System.UInt64" when encodedValue.Length == 1:
                return (ulong) encodedValue[0];
            case "System.UInt64" when encodedValue.Length != 8:
                throw new ArgumentOutOfRangeException(
                    $"Encoded value has incorrect value length for ulong: {encodedValue.Length}"
                );
            case "System.UInt64":
                return BitConverter.ToUInt64(encodedValue, 0);
            case "System.Int64" when encodedValue.Length == 4:
                return (long) BitConverter.ToInt32(encodedValue, 0);
            case "System.Int64" when encodedValue.Length == 2:
                return (long) BitConverter.ToInt16(encodedValue, 0);
            case "System.Int64" when encodedValue.Length == 1:
                return (long) encodedValue[0];
            case "System.Int64" when encodedValue.Length != 8:
                throw new ArgumentOutOfRangeException(
                    $"Encoded value has incorrect value length for long: {encodedValue.Length}"
                );
            case "System.Int64":
                return BitConverter.ToInt64(encodedValue, 0);
            case "System.UInt32" when encodedValue.Length == 8:
                return (uint) BitConverter.ToUInt64(encodedValue, 0);
            case "System.UInt32" when encodedValue.Length == 2:
                return (uint) BitConverter.ToUInt16(encodedValue, 0);
            case "System.UInt32" when encodedValue.Length == 1:
                return (uint) encodedValue[0];
            case "System.UInt32" when encodedValue.Length != 4:
                throw new ArgumentOutOfRangeException(
                    $"Encoded value has incorrect value length for uint: {encodedValue.Length}"
                );
            case "System.UInt32":
                return BitConverter.ToUInt32(encodedValue, 0);
            case "System.UInt16" when encodedValue.Length == 8:
                return (ushort) BitConverter.ToUInt64(encodedValue, 0);
            case "System.UInt16" when encodedValue.Length == 4:
                return (ushort) BitConverter.ToUInt32(encodedValue, 0);
            case "System.UInt16" when encodedValue.Length == 1:
                return (ushort) encodedValue[0];
            case "System.UInt16" when encodedValue.Length != 2:
                throw new ArgumentOutOfRangeException(
                    $"Encoded value has incorrect value length for ushort: {encodedValue.Length}"
                );
            case "System.UInt16":
                return BitConverter.ToUInt16(encodedValue, 0);
            case "System.Int16" when encodedValue.Length == 8:
                return (short) BitConverter.ToInt64(encodedValue, 0);
            case "System.Int16" when encodedValue.Length == 4:
                return (short) BitConverter.ToInt32(encodedValue, 0);
            case "System.Int16" when encodedValue.Length == 1:
                return (short) encodedValue[0];
            case "System.Int16" when encodedValue.Length != 2:
                throw new ArgumentOutOfRangeException(
                    $"Encoded value has incorrect value length for short: {encodedValue.Length}"
                );
            case "System.Int16":
                return BitConverter.ToInt16(encodedValue, 0);
            case "System.Double" when encodedValue.Length != 8:
                throw new ArgumentOutOfRangeException(
                    $"Encoded value has incorrect value length for double: {encodedValue.Length}"
                );
            case "System.Double":
                return BitConverter.ToDouble(encodedValue, 0);
            case "System.String":
                return DecodeString(encodedValue, 0);
            case "SSMP.Math.Vector2" when encodedValue.Length != 8:
                throw new ArgumentOutOfRangeException(
                    $"Encoded value has incorrect value length for Vector2: {encodedValue.Length}"
                );
            case "SSMP.Math.Vector2":
                return new Vector2(
                    BitConverter.ToSingle(encodedValue, 0),
                    BitConverter.ToSingle(encodedValue, 4)
                );
            case "SSMP.Math.Vector3" when encodedValue.Length != 12:
                throw new ArgumentOutOfRangeException(
                    $"Encoded value has incorrect value length for Vector3: {encodedValue.Length}"
                );
            case "SSMP.Math.Vector3":
                return new Vector3(
                    BitConverter.ToSingle(encodedValue, 0),
                    BitConverter.ToSingle(encodedValue, 4),
                    BitConverter.ToSingle(encodedValue, 8)
                );
            case "System.Collections.Generic.List`1[System.String]": {
                var length = BitConverter.ToUInt16(encodedValue, 0);

                var list = new List<string>();
                var offset = 2;
                for (var i = 0; i < length; i++) {
                    var sceneIndex = BitConverter.ToUInt16(encodedValue, offset);
                    if (sceneIndex == ushort.MaxValue) {
                        var strLen = BitConverter.ToUInt16(encodedValue, offset + 2);
                        var decodedStr = System.Text.Encoding.UTF8.GetString(encodedValue, offset + 4, strLen);
                        list.Add(decodedStr);
                        offset += 4 + strLen;
                    } else {
                        if (!TryGetStringName(sceneIndex, out var sceneName)) {
                            throw new ArgumentException(
                                $"Could not decode string in list from save update: {sceneIndex}"
                            );
                        }

                        list.Add(sceneName);
                        offset += 2;
                    }
                }

                return list;
            }
            case "HashSet<string>": {
                var length = BitConverter.ToUInt16(encodedValue, 0);

                var hashSet = new HashSet<string>();
                var offset = 2;
                for (var i = 0; i < length; i++) {
                    var sceneIndex = BitConverter.ToUInt16(encodedValue, offset);
                    if (sceneIndex == ushort.MaxValue) {
                        var strLen = BitConverter.ToUInt16(encodedValue, offset + 2);
                        var decodedStr = System.Text.Encoding.UTF8.GetString(encodedValue, offset + 4, strLen);
                        hashSet.Add(decodedStr);
                        offset += 4 + strLen;
                    } else {
                        if (!TryGetStringName(sceneIndex, out var sceneName)) {
                            throw new ArgumentException(
                                $"Could not decode string in hashset from save update: {sceneIndex}"
                            );
                        }

                        hashSet.Add(sceneName);
                        offset += 2;
                    }
                }

                return hashSet;
            }
            case "SSMP.Serialization.BossSequenceDoorCompletion": {
                var byte1 = encodedValue[0];
                var byte2 = encodedValue[1];

                var bools = GetBoolsFromByte(byte1);

                return new BossSequenceDoorCompletion {
                    CanUnlock = bools[0],
                    Unlocked = bools[1],
                    Completed = bools[2],
                    AllBindings = bools[3],
                    NoHits = bools[4],
                    BoundNail = bools[5],
                    BoundShell = bools[6],
                    BoundCharms = bools[7],
                    BoundSoul = byte2 == 1
                };
            }
            case "SSMP.Serialization.BossStatueCompletion": {
                var bools = GetBoolsFromByte(encodedValue[0]);

                return new BossStatueCompletion {
                    HasBeenSeen = bools[0],
                    IsUnlocked = bools[1],
                    CompletedTier1 = bools[2],
                    CompletedTier2 = bools[3],
                    CompletedTier3 = bools[4],
                    SeenTier3Unlock = bools[5],
                    UsingAltVersion = bools[6]
                };
            }
            case "System.Collections.Generic.List`1[SSMP.Math.Vector3]": {
                var length = BitConverter.ToUInt16(encodedValue, 0);

                var list = new List<Vector3>();
                for (var i = 0; i < length; i++) {
                    // Decode the floats of the vector with offset indices 2, 6, and 10 because we already read 2
                    // bytes as the length. The index is multiplied by 12 as this is the length of a single float
                    var value = new Vector3(
                        BitConverter.ToSingle(encodedValue, 2 + i * 12),
                        BitConverter.ToSingle(encodedValue, 6 + i * 12),
                        BitConverter.ToSingle(encodedValue, 10 + i * 12)
                    );

                    list.Add(value);
                }

                return list;
            }
            case "SSMP.Serialization.MapZone" when encodedValue.Length != 1:
                throw new ArgumentOutOfRangeException(
                    $"Encoded value has incorrect value length for MapZone: {encodedValue.Length}"
                );
            case "SSMP.Serialization.MapZone":
                return (MapZone) encodedValue[0];
            case "System.Collections.Generic.List`1[System.Int32]": {
                var length = encodedValue[0];

                var list = new List<int>();
                for (var i = 0; i < length; i++) {
                    list.Add(encodedValue[i + 1]);
                }

                return list;
            }
            case "System.Byte[]": {
                var length = BitConverter.ToUInt16(encodedValue, 0);
                if (encodedValue.Length != length + 2) {
                    throw new ArgumentOutOfRangeException(
                        $"Encoded value has incorrect value length for byte[]: {encodedValue.Length}"
                    );
                }

                return encodedValue.Skip(2).Take(length).ToArray();
            }
        }

        throw new ArgumentException($"Could not decode type: {type}");

        // Decode a string from the given byte array and start index in that array
        string? DecodeString(byte[] encoded, int startIndex) {
            var sceneIndex = BitConverter.ToUInt16(encoded, startIndex);

            if (sceneIndex == ushort.MaxValue) {
                var strLen = BitConverter.ToUInt16(encoded, startIndex + 2);
                if (strLen == ushort.MaxValue) {
                    return null;
                }

                return System.Text.Encoding.UTF8.GetString(encoded, startIndex + 4, strLen);
            }

            return !TryGetStringName(sceneIndex, out var value)
                ? throw new ArgumentException($"Could not decode string from save update: {encodedValue}")
                : value;
        }
    }

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

            CheckEncodeAddData(name, SaveDataMapping.Instance.PlayerDataIndices, decodedObject);
        }

        var sceneData = saveData.SceneData;
        foreach (var geoRockData in sceneData.GeoRockData) {
            CheckEncodeAddData(geoRockData.GetKey(), SaveDataMapping.Instance.GeoRockIndices, geoRockData.HitsLeft);
        }

        foreach (var persistentBoolData in sceneData.PersistentBoolData) {
            CheckEncodeAddData(
                persistentBoolData.GetKey(),
                SaveDataMapping.Instance.PersistentBoolIndices,
                persistentBoolData.Activated
            );
        }

        foreach (var persistentIntData in sceneData.PersistentIntData) {
            CheckEncodeAddData(
                persistentIntData.GetKey(),
                SaveDataMapping.Instance.PersistentIntIndices,
                persistentIntData.Value
            );
        }

        return encodedSaveData;

        void CheckEncodeAddData<TKey>(TKey key, BiLookup<TKey, ushort> lookup, object? decodedObject) {
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
    }

    /// <summary>
    /// Convert the given dictionary of raw indices and byte arrays to <see cref="ModSaveFile.SaveData"/>.
    /// </summary>
    /// <param name="encodedSaveData">The dictionary containing raw values.</param>
    /// <returns>An instance of save data with the converted values.</returns>
    internal static ModSaveFile.SaveData ConvertFromServerSaveData(Dictionary<ushort, byte[]> encodedSaveData) {
        var saveData = new ModSaveFile.SaveData();

        foreach (var index in encodedSaveData.Keys) {
            var encodedValue = encodedSaveData[index];

            if (CheckDecodeAddData(
                    index,
                    SaveDataMapping.Instance.PlayerDataIndices,
                    pdName => DecodeSaveDataValue(pdName, encodedValue),
                    (pdName, decodedObj) => saveData.PlayerDataEntries.Add(
                        new ModSaveFile.PlayerDataEntry {
                            Name = pdName,
                            Value = decodedObj
                        }
                    )
                )) {
                continue;
            }

            if (CheckDecodeAddData(
                    index,
                    SaveDataMapping.Instance.GeoRockIndices,
                    _ => encodedValue[0],
                    (persistentItemData, decodedObj) => saveData.SceneData.GeoRockData.Add(
                        new ModSaveFile.GeoRockData {
                            Id = persistentItemData.Id,
                            SceneName = persistentItemData.SceneName,
                            HitsLeft = decodedObj
                        }
                    )
                )) {
                continue;
            }

            if (CheckDecodeAddData(
                    index,
                    SaveDataMapping.Instance.PersistentBoolIndices,
                    _ => encodedValue[0] == 1,
                    (persistentItemData, decodedObj) => saveData.SceneData.PersistentBoolData.Add(
                        new ModSaveFile.PersistentBoolData {
                            Id = persistentItemData.Id,
                            SceneName = persistentItemData.SceneName,
                            Activated = decodedObj
                        }
                    )
                )) {
                continue;
            }

            if (!CheckDecodeAddData(
                    index,
                    SaveDataMapping.Instance.PersistentIntIndices,
                    _ => (int) encodedValue[0],
                    (persistentItemData, decodedObj) => saveData.SceneData.PersistentIntData.Add(
                        new ModSaveFile.PersistentIntData {
                            Id = persistentItemData.Id,
                            SceneName = persistentItemData.SceneName,
                            Value = decodedObj
                        }
                    )
                )) {
                Logger.Warn($"Could not decode/find key for index: {index}");
            }
        }

        return saveData;

        bool CheckDecodeAddData<TKey, TDecoded>(
            ushort index,
            BiLookup<TKey, ushort> lookup,
            Func<TKey, TDecoded?> decodeFunc,
            Action<TKey, TDecoded?> addAction
        ) {
            if (!lookup.TryGetValue(index, out var key)) {
                return false;
            }

            TDecoded? decodedObj;
            try {
                decodedObj = decodeFunc.Invoke(key);
            } catch (Exception e) {
                Logger.Warn($"Could not decode save data value with key: {key}, exception:\n{e}");
                return false;
            }

            addAction.Invoke(key, decodedObj);

            return true;
        }
    }
}

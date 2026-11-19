using System;
using UnityEngine;

namespace SSMP.Util;

/// <summary>
/// Class for utilities on serialising game types with the same serialiser the game uses for its saves.
/// </summary>
internal static class JsonUtil {
    /// <summary>
    /// Serialise a value to JSON. The value is wrapped in a <see cref="JsonBox{T}"/>, because JsonUtility only
    /// serialises objects at the top level and the value can be an array or list.
    /// </summary>
    /// <param name="value">The value to serialise.</param>
    /// <param name="type">The type of the field that holds the value.</param>
    /// <returns>The JSON string, or an empty string for a null value.</returns>
    public static string ToJson(object? value, Type type) {
        if (value == null) {
            return "";
        }

        var box = Activator.CreateInstance(typeof(JsonBox<>).MakeGenericType(type));
        box.GetType().GetField(nameof(JsonBox<object>.value)).SetValue(box, value);
        return JsonUtility.ToJson(box);
    }

    /// <summary>
    /// Deserialise a value from JSON created with <see cref="ToJson"/>.
    /// </summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="type">The type of the field that holds the value.</param>
    /// <returns>The deserialised value, or null for an empty string.</returns>
    public static object? FromJson(string json, Type type) {
        if (json.Length == 0) {
            return null;
        }

        var box = JsonUtility.FromJson(json, typeof(JsonBox<>).MakeGenericType(type));
        return box.GetType().GetField(nameof(JsonBox<object>.value)).GetValue(box);
    }

    /// <summary>
    /// Wrapper to serialise any type, including arrays and lists, with JsonUtility.
    /// </summary>
    [Serializable]
    private class JsonBox<T> {
        // ReSharper disable once InconsistentNaming
        public T value = default!;
    }
}

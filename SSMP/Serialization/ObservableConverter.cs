using System;
using Newtonsoft.Json;
using SSMP.Util;

namespace SSMP.Serialization;

/// <summary>
/// A <see cref="JsonConverter"/> for <see cref="Observable{T}"/> that serializes and deserializes the underlying
/// value directly.
/// </summary>
public class ObservableConverter : JsonConverter 
{
    /// <inheritdoc />
    public override bool CanConvert(Type objectType) {
        return objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(Observable<>);
    }

    /// <inheritdoc />
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) {
        if (value == null) {
            writer.WriteNull();
            return;
        }

        var valueProp = value.GetType().GetProperty("Value");
        var innerValue = valueProp?.GetValue(value);
        serializer.Serialize(writer, innerValue);
    }

    /// <inheritdoc />
    public override object? ReadJson(
        JsonReader reader, 
        Type objectType, 
        object? existingValue, 
        JsonSerializer serializer
    ) {
        var innerType = objectType.GetGenericArguments()[0];
        var innerValue = serializer.Deserialize(reader, innerType);
        
        // Create a new Observable<T> with the deserialized value
        return Activator.CreateInstance(objectType, innerValue);
    }
}

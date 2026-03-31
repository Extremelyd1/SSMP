using System;
using System.Collections.Generic;
using SSMP.Game.Settings;
using SSMP.Logging;
using SSMP.Util;

namespace SSMP.Networking.Packet.Data;

/// <summary>
/// Packet data for both client-bound and server-bound server settings update.
/// </summary>
internal class ServerSettingsUpdate : IPacketData {
    // TODO: optimize this by only sending the values that actually changed

    /// <inheritdoc />
    public bool IsReliable => true;

    /// <inheritdoc />
    public bool DropReliableDataIfNewerExists => true;

    /// <summary>
    /// The server settings instance.
    /// </summary>
    public ServerSettings ServerSettings { get; set; } = null!;
    
    /// <summary>Maps syncable property types to their packet read handlers.</summary>
    private static readonly Dictionary<Type, Func<IPacket, object>> Readers = new() {
        [typeof(bool)] = p => p.ReadBool(),
        [typeof(byte)] = p => p.ReadByte(),
    };

    /// <summary>Maps syncable property types to their packet write handlers.</summary>
    private static readonly Dictionary<Type, Action<IPacket, object>> Writers = new() {
        [typeof(bool)] = (p, v) => p.Write((bool) v),
        [typeof(byte)] = (p, v) => p.Write((byte) v),
    };

    /// <inheritdoc />
    public void WriteData(IPacket packet) {
        foreach (var prop in ServerSettings.GetType().GetProperties()) {
            if (!ObservableReflection.IsSyncableProperty(prop)) continue;

            var type = ObservableReflection.UnwrapType(prop.PropertyType);

            if (!Writers.TryGetValue(type, out var write)) {
                Logger.Error($"No write handler for property type: {prop.PropertyType}");
                continue;
            }

            write(packet, ObservableReflection.GetUnwrappedPropertyValue(prop, ServerSettings)!);
        }
    }

    /// <inheritdoc />
    public void ReadData(IPacket packet) {
        ServerSettings = new ServerSettings();

        foreach (var prop in ServerSettings.GetType().GetProperties()) {
            if (!ObservableReflection.IsSyncableProperty(prop)) continue;

            var type = ObservableReflection.UnwrapType(prop.PropertyType);

            if (!Readers.TryGetValue(type, out var read)) {
                Logger.Error($"No read handler for property type: {prop.PropertyType}");
                continue;
            }

            if (!ObservableReflection.TrySetPropertyValue(prop, ServerSettings, read(packet))) {
                Logger.Error($"Could not set reflected property value for: {prop.Name}");
            }
        }
    }
}

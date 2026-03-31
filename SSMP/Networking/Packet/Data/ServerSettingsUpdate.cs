using SSMP.Game.Settings;
using SSMP.Logging;

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

    /// <inheritdoc />
    public void WriteData(IPacket packet) {
        foreach (var prop in ServerSettings.GetType().GetProperties()) {
            if (!prop.CanRead || !prop.CanWrite || prop.DeclaringType != typeof(ServerSettings)) {
                continue;
            }

            if (prop.PropertyType == typeof(bool)) {
                packet.Write((bool) prop.GetValue(ServerSettings, null));
            } else if (prop.PropertyType == typeof(byte)) {
                packet.Write((byte) prop.GetValue(ServerSettings, null));
            } else {
                Logger.Error($"No write handler for property type: {prop.PropertyType}");
            }
        }
    }

    /// <inheritdoc />
    public void ReadData(IPacket packet) {
        ServerSettings = new ServerSettings();

        foreach (var prop in ServerSettings.GetType().GetProperties()) {
            if (!prop.CanRead || !prop.CanWrite || prop.DeclaringType != typeof(ServerSettings)) {
                continue;
            }

            if (prop.PropertyType == typeof(bool)) {
                prop.SetValue(ServerSettings, packet.ReadBool(), null);
            } else if (prop.PropertyType == typeof(byte)) {
                prop.SetValue(ServerSettings, packet.ReadByte(), null);
            } else {
                Logger.Error($"No read handler for property type: {prop.PropertyType}");
            }
        }
    }
}

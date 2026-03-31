using System;
using SSMP.Api.Server;
using SSMP.Ui.Menu;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable StringLiteralTypo

namespace SSMP.Game.Settings;

/// <inheritdoc cref="IServerSettings" />
public class ServerSettings : IServerSettings, IEquatable<ServerSettings> {
    /// <inheritdoc />
    public event Action<string>? OnChanged;

    /// <inheritdoc />
    [SettingAlias("pvp")]
    [ModMenuSetting("PvP", "Player versus Player damage")]
    public bool IsPvpEnabled {
        get;
        set {
            if (field == value) return;
            field = value;
            OnChanged?.Invoke(nameof(IsPvpEnabled));
        }
    }

    /// <inheritdoc />
    [SettingAlias("globalmapicons")]
    [ModMenuSetting("Global Map Icons", "Always show map icons for all players")]
    public bool AlwaysShowMapIcons {
        get;
        set {
            if (field == value) return;
            field = value;
            OnChanged?.Invoke(nameof(AlwaysShowMapIcons));
        }
    }

    /// <inheritdoc />
    [SettingAlias("compassicon", "compassicons")]
    [ModMenuSetting("Compass Map Icons", "Only show map icons when Compass is equipped")]
    public bool OnlyBroadcastMapIconWithCompass {
        get;
        init {
            if (field == value) return;
            field = value;
            OnChanged?.Invoke(nameof(OnlyBroadcastMapIconWithCompass));
        }
    } = true;

    /// <inheritdoc />
    [SettingAlias("names")]
    [ModMenuSetting("Show Names", "Show names of player above their characters")]
    public bool DisplayNames {
        get;
        init {
            if (field == value) return;
            field = value;
            OnChanged?.Invoke(nameof(DisplayNames));
        }
    } = true;

    /// <inheritdoc />
    [SettingAlias("teams")]
    [ModMenuSetting("Teams", "Whether players can join teams")]
    public bool TeamsEnabled {
        get;
        set {
            if (field == value) return;
            field = value;
            OnChanged?.Invoke(nameof(TeamsEnabled));
        }
    }

    /// <inheritdoc />
    [SettingAlias("skins")]
    [ModMenuSetting("Skins", "Whether players can have skins")]
    public bool AllowSkins {
        get;
        init {
            if (field == value) return;
            field = value;
            OnChanged?.Invoke(nameof(AllowSkins));
        }
    } = true;

    /// <summary>
    /// Set all properties in this <see cref="ServerSettings"/> instance to the values from the given
    /// <see cref="ServerSettings"/> instance.
    /// </summary>
    /// <param name="serverSettings">The instance to copy from.</param>
    public void SetAllProperties(ServerSettings serverSettings) {
        foreach (var prop in GetType().GetProperties()) {
            if (!prop.CanRead || !prop.CanWrite || prop.DeclaringType != typeof(ServerSettings)) {
                continue;
            }

            prop.SetValue(this, prop.GetValue(serverSettings));
        }
    }

    /// <summary>
    /// Get a copy of this instance of the server settings.
    /// </summary>
    /// <returns>A new instance of the server settings with the same values as this instance.</returns>
    public ServerSettings GetCopy() {
        var serverSettings = new ServerSettings();
        serverSettings.SetAllProperties(this);

        return serverSettings;
    }

    /// <inheritdoc />
    public bool Equals(ServerSettings other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        foreach (var prop in GetType().GetProperties()) {
            if (!prop.CanRead || prop.DeclaringType != typeof(ServerSettings)) {
                continue;
            }

            if (!Equals(prop.GetValue(this), prop.GetValue(other))) {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) {
        if (ReferenceEquals(null, obj)) {
            return false;
        }

        if (ReferenceEquals(this, obj)) {
            return true;
        }

        if (obj.GetType() != GetType()) {
            return false;
        }

        return Equals((ServerSettings) obj);
    }

    /// <inheritdoc />
    public override int GetHashCode() {
        unchecked {
            var hashCode = 0;
            var first = true;
            foreach (var prop in GetType().GetProperties()) {
                if (!prop.CanRead || prop.DeclaringType != typeof(ServerSettings)) {
                    continue;
                }

                var propHashCode = prop.GetValue(this)?.GetHashCode() ?? 0;

                if (first) {
                    hashCode = propHashCode;
                    first = false;
                    continue;
                }

                hashCode = (hashCode * 397) ^ propHashCode;
            }

            return hashCode;
        }
    }

    /// <summary>
    /// Indicates whether one <see cref="ServerSettings"/> is equal to another <see cref="ServerSettings"/>.
    /// </summary>
    public static bool operator ==(ServerSettings? left, ServerSettings? right) {
        return Equals(left, right);
    }

    /// <summary>
    /// Indicates whether one <see cref="ServerSettings"/> is not equal to another <see cref="ServerSettings"/>.
    /// </summary>
    public static bool operator !=(ServerSettings? left, ServerSettings? right) {
        return !Equals(left, right);
    }
}

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
    public event Action<string>? ChangeEvent;

    /// <inheritdoc />
    [SettingAlias("pvp")]
    [ModMenuSetting("PvP", "Player versus Player damage")]
    public bool IsPvpEnabled {
        get;
        set {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(IsPvpEnabled));
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
            ChangeEvent?.Invoke(nameof(AlwaysShowMapIcons));
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
            ChangeEvent?.Invoke(nameof(OnlyBroadcastMapIconWithCompass));
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
            ChangeEvent?.Invoke(nameof(DisplayNames));
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
            ChangeEvent?.Invoke(nameof(TeamsEnabled));
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
            ChangeEvent?.Invoke(nameof(AllowSkins));
        }
    } = true;

    // /// <inheritdoc />
    // [SettingAlias("parries")]
    // [ModMenuSetting("Parries", "Whether parrying certain player attacks is possible")]
    // public bool AllowParries { get; set; } = true;
    
    /// <inheritdoc />
    [SettingAlias("needledmg")]
    [ModMenuSetting("Needle Damage", "The number of masks of damage that a player's needle swing deals")]
    public byte NeedleDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(NeedleDamage));
        }
    } = 1;

    /// <inheritdoc />
    [SettingAlias("needlestrikedmg", "strikedmg", "artdmg")]
    [ModMenuSetting("Needle Strike Damage", "The number of masks of damage that Needle Strikes deal")]
    public byte NeedleStrikeDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(NeedleStrikeDamage));
        }
    } = 2;

    /// <inheritdoc />
    [SettingAlias("voltfilamentdmg", "voltdmg", "filamentdmg", "voltmodifier", "voltmod")]
    [ModMenuSetting("Volt Filament Damage Modifier (Half)", "The number of extra half-masks of damage that silk skills with volt filament should deal")]
    public byte VoltFilamentDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(VoltFilamentDamage));
        }
    } = 1;

    /// <inheritdoc />
    [SettingAlias("shamandmg", "shamanmodifier", "shamanmod")]
    [ModMenuSetting("Shaman Crest Damage Modifier (Half)", "The number of extra half-masks of damage that silk skills on shaman crest should deal")]
    public byte ShamanDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(ShamanDamage));
        }
    } = 1;

    /// <inheritdoc />
    [SettingAlias("crossstitchdmg", "stitchdmg", "parrydmg")]
    [ModMenuSetting("Cross Stitch Damage", "The number of masks of damage that Cross Stitch deals")]
    public byte CrossStitchDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(CrossStitchDamage));
        }
    } = 1;

    /// <inheritdoc />
    [SettingAlias("palenailsdmg", "palenaildmg", "paledmg")]
    [ModMenuSetting("Pale Nails Damage", "The number of masks of damage that Pale Nails deals")]
    public byte PaleNailsDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(PaleNailsDamage));
        }
    } = 1;

    /// <inheritdoc />
    [SettingAlias("runeragedmg")]
    [ModMenuSetting("Rune Rage Damage", "The number of masks of damage that Rune Rage deals")]
    public byte RuneRageDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(RuneRageDamage));
        }
    } = 1;

    /// <inheritdoc />
    [SettingAlias("sharpdartdmg", "dartdmg")]
    [ModMenuSetting("Sharpdart Damage", "The number of masks of damage that Sharpdart deals")]
    public byte SharpDartDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(SharpDartDamage));
        }
    } = 1;

    /// <inheritdoc />
    [SettingAlias("silkspeardmg", "speardmg")]
    [ModMenuSetting("Silk Spear Damage", "The number of masks of damage that Silk Spear deals")]
    public byte SilkSpearDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(SilkSpearDamage));
        }
    } = 1;

    /// <inheritdoc />
    [SettingAlias("threadstormdmg", "stormdmg")]
    [ModMenuSetting("Thread Storm Damage", "The number of masks of damage that Thread Storm deals")]
    public byte ThreadStormDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(ThreadStormDamage));
        }
    } = 1;

    /// <inheritdoc />
    [SettingAlias("wardingbelldmg", "bindbelldmg", "belldmg")]
    [ModMenuSetting("Warding Bell Damage", "The number of masks of damage that the Warding Bell deals")]
    public byte WardingBellDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(WardingBellDamage));
        }
    } = 1;

    /// <inheritdoc />
    [SettingAlias("clawmirrordmg", "mirrordmg", "mirror1dmg")]
    [ModMenuSetting("Claw Mirror Damage", "The number of masks of damage that the base Claw Mirror deals")]
    public byte ClawMirrorDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(ClawMirrorDamage));
        }
    } = 1;

    /// <inheritdoc />
    [SettingAlias("clawmirrorupgradeddmg", "mirror2dmg")]
    [ModMenuSetting("Claw Mirror Upgraded Damage", "The number of masks of damage that the upgraded Claw Mirror deals")]
    public byte ClawMirrorUpgradedDamage {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangeEvent?.Invoke(nameof(ClawMirrorUpgradedDamage));
        }
    } = 1;

    //
    // /// <inheritdoc />
    // [SettingAlias("sporeshroomdmg")]
    // [ModMenuSetting("Spore Shroom Damage", "The number of masks of damage that a Spore Shroom cloud deals")]
    // public byte SporeShroomDamage { get; set; } = 1;
    //
    // /// <inheritdoc />
    // [SettingAlias("sporedungshroomdmg", "dungshroomdmg")]
    // [ModMenuSetting("Spore-Dung Shroom Damage", "The number of masks of damage that a Spore Shroom cloud with Defender's Crest deals")]
    // public byte SporeDungShroomDamage { get; set; } = 1;
    //
    // /// <inheritdoc />
    // [SettingAlias("thornsofagonydamage", "thornsofagonydmg", "thornsdamage", "thornsdmg")]
    // [ModMenuSetting("Thorns of Agongy Damage", "The number of masks of damage that the Thorns of Agony lash deals")]
    // public byte ThornOfAgonyDamage { get; set; } = 1;
    //
    // /// <inheritdoc />
    // [SettingAlias("sharpshadowdmg")]
    // [ModMenuSetting("Sharp Shadow Damage", "The number of masks of damage that a Sharp Shadow dash deals")]
    // public byte SharpShadowDamage { get; set; } = 1;

    /// <summary>
    /// Set all properties in this <see cref="ServerSettings"/> instance to the values from the given
    /// <see cref="ServerSettings"/> instance.
    /// </summary>
    /// <param name="serverSettings">The instance to copy from.</param>
    public void SetAllProperties(ServerSettings serverSettings) {
        // Use reflection to copy over all properties into this object
        foreach (var prop in GetType().GetProperties()) {
            if (!prop.CanRead || !prop.CanWrite) {
                continue;
            }

            prop.SetValue(this, prop.GetValue(serverSettings, null), null);
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
            if (!prop.CanRead) {
                continue;
            }
    
            if (prop.GetValue(this) != prop.GetValue(other)) {
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
                if (!prop.CanRead) {
                    continue;
                }
                
                var propHashCode = prop.GetValue(this).GetHashCode();
    
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
    /// <param name="left">The first <see cref="ServerSettings"/> to compare.</param>
    /// <param name="right">The second <see cref="ServerSettings"/> to compare.</param>
    /// <returns>true if <paramref name="left"/> is equal to <paramref name="right"/>; false otherwise.</returns>
    public static bool operator ==(ServerSettings? left, ServerSettings? right) {
        return Equals(left, right);
    }
    
    /// <summary>
    /// Indicates whether one <see cref="ServerSettings"/> is not equal to another <see cref="ServerSettings"/>.
    /// </summary>
    /// <param name="left">The first <see cref="ServerSettings"/> to compare.</param>
    /// <param name="right">The second <see cref="ServerSettings"/> to compare.</param>
    /// <returns>true if <paramref name="left"/> is not equal to <paramref name="right"/>; false otherwise.</returns>
    public static bool operator !=(ServerSettings? left, ServerSettings? right) {
        return !Equals(left, right);
    }
}

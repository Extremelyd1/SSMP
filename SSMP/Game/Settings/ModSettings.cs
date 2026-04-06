using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SSMP.Api.Client;
using SSMP.Serialization;
using SSMP.Ui.Menu;
using SSMP.Util;

namespace SSMP.Game.Settings;

/// <summary>
/// Settings class that stores user preferences.
/// </summary>
internal class ModSettings : IModSettings {
    /// <summary>
    /// The name of the file containing the mod settings.
    /// </summary>
    private const string ModSettingsFileName = "modsettings.json";

    /// <inheritdoc/>
    public event System.Action<string>? ChangedEvent;
    
    /// <summary>
    /// The authentication key for the user.
    /// </summary>
    public string? AuthKey { get; set; }

    /// <summary>
    /// The keybinds for SSMP.
    /// </summary>
    [JsonConverter(typeof(PlayerActionSetConverter))]
    public Keybinds Keybinds { get; } = new();

    /// <inheritdoc/>
    public string ConnectAddress {
        get;
        set {
            if (field == value) return;
            field = value;
            ChangedEvent?.Invoke(nameof(ConnectAddress));
        }
    } = "";

    /// <inheritdoc/>
    public int ConnectPort {
        get;
        set {
            if (field == value) return;
            field = value;
            ChangedEvent?.Invoke(nameof(ConnectPort));
        }
    } = -1;

    /// <inheritdoc/>
    public string Username {
        get;
        set {
            if (field == value) return;
            field = value;
            ChangedEvent?.Invoke(nameof(Username));
        }
    } = "";

    /// <inheritdoc/>
    public bool DisplayPing {
        get;
        init {
            if (field == value) return;
            field = value;
            ChangedEvent?.Invoke(nameof(DisplayPing));
        }
    } = true;

    /// <summary>
    /// Set of addon names for addons that are disabled by the user.
    /// </summary>
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
    public HashSet<string> DisabledAddons { get; set; } = [];

    /// <inheritdoc/>
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
    public bool FullSynchronisation {
        get;
        set {
            if (field == value) return;
            field = value;
            ChangedEvent?.Invoke(nameof(FullSynchronisation));
        }
    }

    /// <summary>
    /// The last used server settings in a hosted server.
    /// </summary>
    public ServerSettings? ServerSettings { get; set; }

    /// <summary>
    /// The settings for the MatchMaking Server (MMS).
    /// </summary>
    public MmsSettings MmsSettings { get; set; } = new();
    
    /// <summary>
    /// Load the mod settings from file or create a new instance.
    /// </summary>
    /// <returns>The mod settings instance.</returns>
    public static ModSettings Load() {
        var path = FileUtil.GetConfigPath();
        var filePath = Path.Combine(path, ModSettingsFileName);
        if (!Directory.Exists(path)) {
            return New();
        }

        // Try to load the mod settings from the file or construct a new instance if the util returns null
        var modSettings = FileUtil.LoadObjectFromJsonFile<ModSettings>(filePath);

        return modSettings ?? New();

        ModSettings New() {
            var newModSettings = new ModSettings();
            newModSettings.Save();
            return newModSettings;
        }
    }

    /// <summary>
    /// Save the mod settings to file.
    /// </summary>
    public void Save() {
        var path = FileUtil.GetConfigPath();
        var filePath = Path.Combine(path, ModSettingsFileName);

        if (!Directory.Exists(path)) {
            Directory.CreateDirectory(path);
        }

        FileUtil.WriteObjectToJsonFile(this, filePath);
    }
}

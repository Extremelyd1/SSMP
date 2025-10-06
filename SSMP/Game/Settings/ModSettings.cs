using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SSMP.Serialization;
using SSMP.Ui.Menu;
using SSMP.Util;

namespace SSMP.Game.Settings;

/// <summary>
/// Settings class that stores user preferences.
/// </summary>
internal class ModSettings {
    /// <summary>
    /// The name of the file containing the mod settings.
    /// </summary>
    private const string ModSettingsFileName = "modsettings.json";
    
    /// <summary>
    /// The authentication key for the user.
    /// </summary>
    public string? AuthKey { get; set; }

    /// <summary>
    /// The keybinds for SSMP.
    /// </summary>
    [JsonConverter(typeof(PlayerActionSetConverter))]
    public Keybinds Keybinds { get; set; } = new();

    /// <summary>
    /// The last used address to join a server.
    /// </summary>
    public string ConnectAddress { get; set; } = "";

    /// <summary>
    /// The last used port to join a server.
    /// </summary>
    public int ConnectPort { get; set; } = -1;

    /// <summary>
    /// The last used username to join a server.
    /// </summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// Whether to display a UI element for the ping.
    /// </summary>
    public bool DisplayPing { get; set; }

    /// <summary>
    /// Set of addon names for addons that are disabled by the user.
    /// </summary>
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
    public HashSet<string> DisabledAddons { get; set; } = [];

    /// <summary>
    /// Whether full synchronisation of bosses, enemies, worlds, and saves is enabled.
    /// </summary>
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
    public bool FullSynchronisation { get; set; } = false;

    /// <summary>
    /// The last used server settings in a hosted server.
    /// </summary>
    public ServerSettings? ServerSettings { get; set; }
    
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

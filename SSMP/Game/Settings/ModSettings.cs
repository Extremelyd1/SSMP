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
internal class ModSettings : ObservableBase {
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
    public Keybinds Keybinds { get; } = new();

    /// <summary>
    /// The last used address to join a server.
    /// </summary>
    public Observable<string> ConnectAddress { get; } = new("");

    /// <summary>
    /// The last used port to join a server.
    /// </summary>
    public Observable<int> ConnectPort { get; } = new(-1);

    /// <summary>
    /// The last used username to join a server.
    /// </summary>
    public Observable<string> Username { get; } = new("");

    /// <summary>
    /// Whether to display a UI element for the ping.
    /// </summary>
    public Observable<bool> DisplayPing { get; } = new(true);

    /// <summary>
    /// Set of addon names for addons that are disabled by the user.
    /// </summary>
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
    public HashSet<string> DisabledAddons { get; set; } = [];

    /// <summary>
    /// Whether full synchronization of bosses, enemies, worlds, and saves is enabled.
    /// </summary>
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
    public Observable<bool> FullSynchronisation { get; } = new(false);

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

        modSettings?.AcceptChanges();

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
        AcceptChanges();
    }
}

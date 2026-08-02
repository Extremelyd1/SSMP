using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SSMP.Game.Command.Server;
using SSMP.Game.Settings;
using SSMP.Networking.Packet;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;
using SSMP.Networking.Transport.HolePunch;
using SSMP.Networking.Transport.SteamP2P;
using SSMP.Networking.Transport.UDP;
using SSMP.Game.Server.Save;
using SSMP.Game.Client.Save;
using SSMP.Hooks;
using SSMP.Ui;
using SSMP.Util;

namespace SSMP.Game.Server;

/// <summary>
/// Specialization of <see cref="ServerManager"/> that adds handlers for the mod specific things.
/// </summary>
internal class ModServerManager : ServerManager {
    /// <summary>
    /// The UiManager instance for registering events for starting and stopping a server.
    /// </summary>
    private readonly UiManager _uiManager;

    /// <summary>
    /// The mod settings instance for retrieving the auth key of the local player to set player save data when
    /// hosting a server.
    /// </summary>
    private readonly ModSettings _modSettings;

    /// <summary>
    /// The settings command.
    /// </summary>
    private readonly SettingsCommand _settingsCommand;


    /// <summary>
    /// The NetServer instance to check whether the server is started.
    /// </summary>
    private readonly NetServer _netServer;

    public ModServerManager(
        NetServer netServer,
        PacketManager packetManager,
        ServerSettings serverSettings,
        UiManager uiManager,
        ModSettings modSettings
    ) : base(netServer, packetManager, serverSettings) {
        _netServer = netServer;
        _uiManager = uiManager;
        _modSettings = modSettings;
        _settingsCommand = new SettingsCommand(this, InternalServerSettings);
    }

    /// <inheritdoc />
    public override void Initialize() {
        base.Initialize();

        // Start addon loading, since all addons that are also mods should be registered during the Awake phase of
        // their MonoBehaviour
        AddonManager.LoadAddons();

        // Register handlers for UI events
        _uiManager.RequestServerStartHostEvent += (_, port, _, transportType, _) =>
            OnRequestServerStartHost(port, _modSettings.FullSynchronisation, transportType);
        _uiManager.RequestServerStopHostEvent += Stop;
        PlayerConnectEvent += _ => UpdateMatchmakingRemotePlayerCount();
        PlayerDisconnectEvent += _ => UpdateMatchmakingRemotePlayerCount();
        ServerShutdownEvent += () => UpdateMatchmakingRemotePlayerCount(0);

        EventHooks.GameManagerSaveGame += OnGameSave;

        // Register application quit handler
        // ModHooks.ApplicationQuitHook += Stop;
    }

    /// <summary>
    /// Callback method for when the UI requests the server to be started as a host.
    /// </summary>
    /// <param name="port">The port to start the server on.</param>
    /// <param name="fullSynchronisation">Whether full synchronisation is enabled.</param>
    /// <param name="transportType">The type of transport to use.</param>
    private void OnRequestServerStartHost(int port, bool fullSynchronisation, TransportType transportType) {
        if (fullSynchronisation) {
            // Get the global save data from the save manager, which obtains the global save data from the loaded
            // save file that the user selected
            ServerSaveData.GlobalSaveData = SaveManager.GetCurrentSaveData(true);

            // Load remote players' player-specific data from disk for the current profile ID
            var profileId = global::GameManager.instance.profileID;
            var modSavePath = Path.Combine(FileUtil.GetConfigPath(), $"user{profileId}.modsav");
            if (File.Exists(modSavePath)) {
                try {
                    var json = File.ReadAllText(modSavePath);
                    var modSaveFile = JsonConvert.DeserializeObject<ModSaveFile>(json);
                    if (modSaveFile != null) {
                        var serverSave = modSaveFile.ToServerSaveData();
                        ServerSaveData.PlayerSaveData = serverSave.PlayerSaveData;
                        if (serverSave.GlobalSaveData.Count > 0) {
                            ServerSaveData.GlobalSaveData = serverSave.GlobalSaveData;
                        }

                        Logging.Logger.Info($"Loaded remote players' save data from: {modSavePath}");
                    }
                } catch (Exception e) {
                    Logging.Logger.Error($"Could not load remote players' save data: {e}");
                }
            } else {
                ServerSaveData.PlayerSaveData = new Dictionary<string, Dictionary<ushort, byte[]>>();
                Logging.Logger.Info(
                    $"No remote player save file found at: {modSavePath}, initialized empty player save data."
                );
            }

            // Lastly, we get the player save data from the save manager, which obtains the player save data from the
            // loaded save file that the user selected. We add this data to the server save as the local player
            ServerSaveData.PlayerSaveData[_modSettings.AuthKey!] = SaveManager.GetCurrentSaveData(false);
        }

        IEncryptedTransportServer transportServer = transportType switch {
            TransportType.Udp => new UdpEncryptedTransportServer(),
            TransportType.Steam => new SteamEncryptedTransportServer(),
            TransportType.HolePunch => CreateHolePunchServer(),
            _ => throw new ArgumentOutOfRangeException(nameof(transportType), transportType, null)
        };

        Start(port, fullSynchronisation, transportServer);
        UpdateMatchmakingRemotePlayerCount();
    }

    /// <summary>
    /// Creates a HolePunch server with the MmsClient for lobby cleanup on shutdown.
    /// </summary>
    private HolePunchEncryptedTransportServer CreateHolePunchServer() {
        return new HolePunchEncryptedTransportServer(_uiManager.ConnectInterface.MmsClient);
    }

    /// <inheritdoc />
    protected override void RegisterCommands() {
        base.RegisterCommands();

        CommandManager.RegisterCommand(_settingsCommand);
    }

    /// <inheritdoc />
    protected override void DeregisterCommands() {
        base.DeregisterCommands();

        CommandManager.DeregisterCommand(_settingsCommand);

        EventHooks.GameManagerSaveGame -= OnGameSave;
    }

    /// <summary>
    /// Pushes the current remote-player count to MMS heartbeat state.
    /// </summary>
    /// <param name="count">The number of players to set in the update, or -1 if the number needs to be retrieved
    /// from the server.</param>
    private void UpdateMatchmakingRemotePlayerCount(int count = -1) {
        if (count != -1) {
            _uiManager.ConnectInterface.MmsClient.SetConnectedPlayers(count);
            return;
        }

        var hostAuthKey = _modSettings.AuthKey;
        var remotePlayerCount = hostAuthKey == null
            ? 0
            : Players.Count(player => player.AuthKey != hostAuthKey);
        _uiManager.ConnectInterface.MmsClient.SetConnectedPlayers(remotePlayerCount);
    }

    /// <summary>
    /// Intercepts native save events to serialize remote player-specific save data to disk.
    /// </summary>
    /// <param name="saveSlot">The save slot index.</param>
    private void OnGameSave(int saveSlot) {
        if (!_netServer.IsStarted || !FullSynchronisation) {
            return;
        }

        try {
            Logging.Logger.Info($"Intercepted native save for slot {saveSlot}. Saving remote players' save data...");

            // Create a copy of ServerSaveData for serialization
            var modSaveFile = ModSaveFile.FromServerSaveData(ServerSaveData);

            // Filter out the host player's auth key to avoid duplicate/redundant data in the remote players' file
            var hostAuthKey = _modSettings.AuthKey;
            if (hostAuthKey != null) {
                modSaveFile.PlayerSaveData.Remove(hostAuthKey);
            }

            var configPath = FileUtil.GetConfigPath();
            if (!Directory.Exists(configPath)) {
                Directory.CreateDirectory(configPath);
            }

            var modSavePath = Path.Combine(configPath, $"user{saveSlot}.modsav");
            var json = JsonConvert.SerializeObject(modSaveFile, Formatting.Indented);
            File.WriteAllText(modSavePath, json);

            Logging.Logger.Info($"Remote players' save data successfully written to {modSavePath}");
        } catch (Exception e) {
            Logging.Logger.Error($"Could not save remote players' save data to disk: {e}");
        }
    }
}

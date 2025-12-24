using System;
using SSMP.Game.Command.Server;
using SSMP.Game.Settings;
using SSMP.Networking.Packet;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;
using SSMP.Networking.Transport.HolePunch;
using SSMP.Networking.Transport.SteamP2P;
using SSMP.Networking.Transport.UDP;
using SSMP.Ui;

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
    
    // /// <summary>
    // /// Save data that was loaded from selecting a save file. Will be retroactively applied to a server, if one was
    // /// requested to be started after selecting a save file.
    // /// </summary>
    // private ServerSaveData? _loadedLocalSaveData;
    
    public ModServerManager(
        NetServer netServer,
        PacketManager packetManager,
        ServerSettings serverSettings,
        UiManager uiManager,
        ModSettings modSettings
   ) : base(netServer, packetManager, serverSettings) {
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
        _uiManager.RequestServerStartHostEvent += (_, port, _, transportType) => 
            OnRequestServerStartHost(port, _modSettings.FullSynchronisation, transportType);
        _uiManager.RequestServerStopHostEvent += Stop;

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
        // if (fullSynchronisation) {
        //     // Get the global save data from the save manager, which obtains the global save data from the loaded
        //     // save file that the user selected
        //     ServerSaveData.GlobalSaveData = SaveManager.GetCurrentSaveData(true);
        //
        //     // Then we import the player save data from the (potentially) loaded modded save file from the user selected
        //     // save file
        //     if (_loadedLocalSaveData != null) {
        //         ServerSaveData.PlayerSaveData = _loadedLocalSaveData.PlayerSaveData;
        //     }
        //
        //     // Lastly, we get the player save data from the save manager, which obtains the player save data from the
        //     // loaded save file that the user selected. We add this data to the server save as the local player
        //     ServerSaveData.PlayerSaveData[_modSettings.AuthKey!] = SaveManager.GetCurrentSaveData(false);
        // }

        IEncryptedTransportServer transportServer = transportType switch {
            TransportType.Udp => new UdpEncryptedTransportServer(),
            TransportType.Steam => new SteamEncryptedTransportServer(),
            TransportType.HolePunch => CreateHolePunchServer(),
            _ => throw new ArgumentOutOfRangeException(nameof(transportType), transportType, null)
        };

        Start(port, fullSynchronisation, transportServer);
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
    }
}

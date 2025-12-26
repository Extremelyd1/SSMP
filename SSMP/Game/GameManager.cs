using SSMP.Game.Client;
using SSMP.Game.Server;
using SSMP.Game.Settings;
using SSMP.Networking.Client;
using SSMP.Networking.Packet;
using SSMP.Networking.Server;
using SSMP.Ui;
using SSMP.Ui.Resources;
using SSMP.Util;

namespace SSMP.Game;

/// <summary>
/// Instantiates all necessary classes to start multiplayer activities.
/// </summary>
internal class GameManager {
    /// <summary>
    /// The UI manager instance for the mod.
    /// </summary>
    private readonly UiManager _uiManager;

    /// <summary>
    /// The client manager instance for the mod.
    /// </summary>
    private readonly ClientManager _clientManager;
    /// <summary>
    /// The server manager instance for the mod.
    /// </summary>
    private readonly ModServerManager _serverManager;
    
    /// <summary>
    /// Constructs this GameManager instance by instantiating all other necessary classes.
    /// </summary>
    public GameManager() {
        var modSettings = ModSettings.Load();

        var packetManager = new PacketManager();

        var netClient = new NetClient(packetManager);
        var netServer = new NetServer(packetManager);

        var clientServerSettings = new ServerSettings();
        if (modSettings.ServerSettings == null) {
            modSettings.ServerSettings = new ServerSettings();
        }
        var serverServerSettings = modSettings.ServerSettings;

        _uiManager = new UiManager(
            modSettings,
            netClient
        );

        _serverManager = new ModServerManager(
            netServer,
            packetManager,
            serverServerSettings,
            _uiManager,
            modSettings
        );

        _clientManager = new ClientManager(
            netClient,
            packetManager,
            _uiManager,
            clientServerSettings,
            modSettings
        );
    }

    /// <summary>
    /// Initialize all the managers and static utilities.
    /// </summary>
    public void Initialize() {
        ThreadUtil.Instantiate();

        TextureManager.LoadTextures();

        // Initialize Steam if available
        if (SteamManager.Initialize()) {
            // Register shutdown hook to leave lobby when server stops (but keep Steam running for restarts)
            _serverManager.ServerShutdownEvent += SteamManager.LeaveLobby;
        }

        _uiManager.Initialize();
        
        _serverManager.Initialize();
        _clientManager.Initialize(_serverManager);
    }
}

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
    public static GameManager Instance { get; private set; } = null!;
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
        Instance = this;
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
            // Hook lobby cleanup to UI's stop request (explicit user action), NOT ServerShutdownEvent
            // ServerShutdownEvent fires on server restarts too, which would prematurely clean up lobbies
            _uiManager.RequestServerStopHostEvent += () => {
                SteamManager.LeaveLobby();
                // Also close MMS lobby registration if any (for public Steam lobbies)
                _uiManager.ConnectInterface.MmsClient.CloseLobby();
            };
        }

        _uiManager.Initialize();
        _serverManager.Initialize();
        _clientManager.Initialize(_serverManager);
    }

    /// <summary>
    /// Shuts down the game manager and all its subsystems.
    /// </summary>
    public void Shutdown() {
        Logging.Logger.Info("GameManager: Shutting down...");

        // Stop client first to disconnect from any server
        _clientManager.Disconnect();

        // Stop server if hosting
        _serverManager.Stop();
        
        // Clean up Steam if initialized
        if (SteamManager.IsInitialized) {
            SteamManager.Shutdown();
        }
    }
}

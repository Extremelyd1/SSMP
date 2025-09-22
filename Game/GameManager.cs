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
    /// The net client instance for the mod.
    /// </summary>
    public readonly NetClient NetClient;
    /// <summary>
    /// The client manager instance for the mod.
    /// </summary>
    public readonly ClientManager ClientManager;
    /// <summary>
    /// The server manager instance for the mod.
    /// </summary>
    public readonly ModServerManager ServerManager;
    
    /// <summary>
    /// Constructs this GameManager instance by instantiating all other necessary classes.
    /// </summary>
    /// <param name="modSettings">The loaded ModSettings instance or null if no such instance could be
    /// loaded.</param>
    public GameManager(ModSettings modSettings) {
        ThreadUtil.Instantiate();

        TextureManager.LoadTextures();

        var packetManager = new PacketManager();

        NetClient = new NetClient(packetManager);
        var netServer = new NetServer(packetManager);

        var clientServerSettings = new ServerSettings();
        if (modSettings.ServerSettings == null) {
            modSettings.ServerSettings = new ServerSettings();
        }
        var serverServerSettings = modSettings.ServerSettings;

        var uiManager = new UiManager(
            modSettings,
            NetClient
        );
        uiManager.Initialize();

        ServerManager = new ModServerManager(
            netServer,
            packetManager,
            serverServerSettings,
            uiManager,
            modSettings
        );
        ServerManager.Initialize();

        ClientManager = new ClientManager(
            NetClient,
            packetManager,
            uiManager,
            clientServerSettings,
            modSettings
        );
        ClientManager.Initialize(ServerManager);
    }
}

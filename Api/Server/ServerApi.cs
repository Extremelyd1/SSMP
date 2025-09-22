using SSMP.Api.Command.Server;
using SSMP.Api.Eventing;
using SSMP.Api.Server.Networking;

namespace SSMP.Api.Server;

/// <summary>
/// Server API interface implementation.
/// </summary>
internal class ServerApi : IServerApi {
    /// <inheritdoc/>
    public IServerManager ServerManager { get; }

    /// <inheritdoc/>
    public IServerCommandManager CommandManager { get; }

    /// <inheritdoc/>
    public INetServer NetServer { get; }

    /// <inheritdoc/>
    public IEventAggregator EventAggregator { get; }

    public ServerApi(
        IServerManager serverManager,
        IServerCommandManager commandManager,
        INetServer netServer,
        IEventAggregator eventAggregator
    ) {
        ServerManager = serverManager;
        CommandManager = commandManager;
        NetServer = netServer;
        EventAggregator = eventAggregator;
    }
}

using SSMP.Api.Client.Networking;
using SSMP.Api.Command.Client;
using SSMP.Api.Eventing;

namespace SSMP.Api.Client;

/// <summary>
/// Client API interface implementation.
/// </summary>
internal class ClientApi : IClientApi {
    /// <inheritdoc/>
    public IClientManager ClientManager { get; }

    /// <inheritdoc/>
    public IClientCommandManager CommandManager { get; }

    /// <inheritdoc/>
    public IUiManager UiManager { get; }

    /// <inheritdoc/>
    public INetClient NetClient { get; }

    /// <inheritdoc/>
    public IEventAggregator EventAggregator { get; }

    public ClientApi(
        IClientManager clientManager,
        IClientCommandManager commandManager,
        IUiManager uiManager,
        INetClient netClient,
        IEventAggregator eventAggregator
    ) {
        ClientManager = clientManager;
        CommandManager = commandManager;
        UiManager = uiManager;
        NetClient = netClient;
        EventAggregator = eventAggregator;
    }
}

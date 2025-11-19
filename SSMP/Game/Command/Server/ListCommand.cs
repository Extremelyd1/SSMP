using System.Linq;
using SSMP.Api.Command.Server;
using SSMP.Game.Server;
using SSMP.Api.Command;

namespace SSMP.Game.Command.Server;

/// <summary>
/// Command for listing all connected players on the server.
/// </summary>
internal class ListCommand : IServerCommand, ICommandWithDescription {
    /// <inheritdoc />
    public string Trigger => "/list";

    /// <inheritdoc />
    public string[] Aliases => [];

    /// <inheritdoc />
    public string Description => "List the names of the currently connected players.";

    /// <inheritdoc />
    public bool AuthorizedOnly => false;

    /// <summary>
    /// The server manager instance.
    /// </summary>
    private readonly ServerManager _serverManager;

    public ListCommand(ServerManager serverManager) {
        _serverManager = serverManager;
    }

    /// <inheritdoc />
    public void Execute(ICommandSender commandSender, string[] arguments) {
        var players = _serverManager.Players;

        var playerNames = string.Join(", ", players.Select(p => p.Username));

        commandSender.SendMessage($"Online players ({players.Count}): {playerNames}");
    }
}

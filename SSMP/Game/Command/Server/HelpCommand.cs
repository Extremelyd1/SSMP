using System.Linq;
using SSMP.Api.Command;
using SSMP.Api.Command.Server;
using SSMP.Game.Server;

namespace SSMP.Game.Command.Server;

/// <summary>
/// Command that lists the currently registered server commands available to the sender.
/// </summary>
internal class HelpCommand : IServerCommand, ICommandWithDescription {
    /// <inheritdoc />
    public string Trigger => "/help";

    /// <inheritdoc />
    public string[] Aliases => ["/commands", "/?"];

    /// <inheritdoc />
    public string Description => "Show the list of available commands.";

    /// <inheritdoc />
    public bool AuthorizedOnly => false;

    private readonly ServerManager _serverManager;

    public HelpCommand(ServerManager serverManager) {
        _serverManager = serverManager;
    }

    /// <inheritdoc />
    public void Execute(ICommandSender commandSender, string[] arguments) {
        var cmds = _serverManager
            .GetRegisteredCommands()
            .Where(c => commandSender.IsAuthorized || !c.AuthorizedOnly)
            .OrderBy(c => c.Trigger)
            .ToList();

        if (cmds.Count == 0) {
            commandSender.SendMessage("No commands available.");
            return;
        }

        var lines = cmds
            .Select(c => {
                var descText = (c as ICommandWithDescription)?.Description ?? string.Empty;
                var desc = string.IsNullOrWhiteSpace(descText)
                    ? string.Empty
                    : $" &7: &6{descText}";
                return $" &7- &a{c.Trigger}{desc}";
            })
            .Prepend($"&eAvailable commands &7(&f{cmds.Count()}&7):");
        commandSender.SendMessage(string.Join("\n", lines));
    }
}

using System.Collections.Generic;
using System.Linq;
using SSMP.Api.Command.Server;
using SSMP.Game.Server;

namespace SSMP.Game.Command.Server;

/// <summary>
/// Lists available server commands for the sender.
/// </summary>
internal class HelpCommand : IServerCommand {
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
        var commands = GetAvailableCommands(commandSender);

        if (commands.Count == 0) {
            commandSender.SendMessage("No commands available.");
            return;
        }

        SendCommandList(commandSender, commands);
    }

    private List<IServerCommand> GetAvailableCommands(ICommandSender sender) {
        return _serverManager
            .GetRegisteredCommands()
            .Where(cmd => sender.IsAuthorized || !cmd.AuthorizedOnly)
            .OrderBy(cmd => cmd.Trigger)
            .ToList();
    }

    private static void SendCommandList(ICommandSender sender, List<IServerCommand> commands) {
        sender.SendMessage($"&6Available commands &8(&f{commands.Count}&8)&r:");

        foreach (var command in commands) {
            var description = GetCommandDescription(command);
            var line = string.IsNullOrEmpty(description)
                ? $"&a - &b{command.Trigger}"
                : $"&a - &b{command.Trigger}&8 : &7{description}";
            
            sender.SendMessage(line);
        }
    }

    private static string GetCommandDescription(IServerCommand command) {
        return (command as ICommandWithDescription)?.Description ?? string.Empty;
    }
}

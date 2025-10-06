using SSMP.Api.Command.Server;
using SSMP.Game.Server;

namespace SSMPServer.Command;

/// <summary>
/// Command to exit and shutdown the server.
/// </summary>
internal class ExitCommand : IServerCommand {
    /// <inheritdoc />
    public string Trigger => "/exit";

    /// <inheritdoc />
    public string[] Aliases => [];

    /// <inheritdoc />
    public bool AuthorizedOnly => true;

    /// <summary>
    /// The server manager instance.
    /// </summary>
    private readonly ServerManager _serverManager;

    /// <summary>
    /// Construct the exit command with the given server manager.
    /// </summary>
    /// <param name="serverManager">The server manager instance.</param>
    public ExitCommand(ServerManager serverManager) {
        _serverManager = serverManager;
    }

    /// <inheritdoc />
    public void Execute(ICommandSender commandSender, string[] arguments) {
        if (commandSender.Type == CommandSenderType.Console) {
            _serverManager.Stop();

            commandSender.SendMessage("Exiting server...");
            Environment.Exit(5);

            return;
        }

        commandSender.SendMessage("This command can only be execute as the console");
    }
}

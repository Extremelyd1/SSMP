using SSMP.Api.Command.Server;
using SSMP.Game.Command.Server;
using SSMP.Game.Server;
using SSMP.Game.Settings;

namespace SSMPServer.Command;

/// <summary>
/// The settings command for the console program.
/// </summary>
internal class ConsoleSettingsCommand : SettingsCommand {
    public ConsoleSettingsCommand(
        ServerManager serverManager,
        ServerSettings serverSettings
    ) : base(serverManager, serverSettings) {
    }

    /// <inheritdoc />
    public override void Execute(ICommandSender commandSender, string[] args) {
        base.Execute(commandSender, args);

        ConfigManager.SaveServerSettings(ServerSettings);
    }
}

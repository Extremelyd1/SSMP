using SSMP.Api.Command.Client;
using SSMP.Logging;

namespace SSMP.Game.Command.Client;

/// <summary>
/// Command for executing debug-related things.
/// </summary>
internal class DebugCommand : IClientCommand {
    /// <inheritdoc />
    public string Trigger => "/debug";

    /// <inheritdoc />
    public string[] Aliases => ["/dbg"];

    /// <inheritdoc />
    public void Execute(string[] arguments) {
        var sprintFsm = HeroController.instance.sprintFSM;

        for (var i = 0; i < sprintFsm.FsmStates.Length; i++) {
            var stateName = sprintFsm.FsmStates[i].name;
            
            Logger.Info($"{i}: {stateName}");
        }
    }
}

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

    private void Test() {
        tk2dSpriteAnimationClip clip = null!;

        Logger.Info($"Animation clip: {clip.name}, fps: {clip.fps}, frames length: {clip.frames.Length}");
        for (var i = 0; i < clip.frames.Length; i++) {
            var frame = clip.frames[i];
            if (frame.triggerEvent) {
                var secondsUntilFrame = (i + 1) / clip.fps;
                
                Logger.Info($"Frame {i} has triggerEvent, seconds until frame: {secondsUntilFrame}");
            }
        }
    }
}

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
        OutputBindFsmStateNames();
    }

    private void OutputBindFsmStateNames() {
        var heroFsms = HeroController.instance.GetComponents<PlayMakerFSM>();
        foreach (var heroFsm in heroFsms) {
            if (heroFsm.FsmName == "Bind") {
                for (var i = 0; i < heroFsm.FsmStates.Length; i++) {
                    var state = heroFsm.FsmStates[i];
                    
                    Logger.Info($"{i}: {state.name}");
                }

                break;
            }
        }
    }

    private void ListHeroControllerFsmNames() {
        var hc = HeroController.instance;
        Logger.Info($"sprintFSM: {hc.sprintFSM.name}, {hc.sprintFSM.fsm.name}");
        Logger.Info($"sprintFSM: {hc.toolsFSM.name}, {hc.toolsFSM.fsm.name}");
        Logger.Info($"sprintFSM: {hc.mantleFSM.name}, {hc.mantleFSM.fsm.name}");
        Logger.Info($"sprintFSM: {hc.umbrellaFSM.name}, {hc.umbrellaFSM.fsm.name}");
        Logger.Info($"sprintFSM: {hc.silkSpecialFSM.name}, {hc.silkSpecialFSM.fsm.name}");
        Logger.Info($"sprintFSM: {hc.crestAttacksFSM.name}, {hc.crestAttacksFSM.fsm.name}");
        Logger.Info($"sprintFSM: {hc.harpoonDashFSM.name}, {hc.harpoonDashFSM.fsm.name}");
        Logger.Info($"sprintFSM: {hc.superJumpFSM.name}, {hc.superJumpFSM.fsm.name}");
        Logger.Info($"sprintFSM: {hc.bellBindFSM.name}, {hc.bellBindFSM.fsm.name}");
        Logger.Info($"sprintFSM: {hc.wallScrambleFSM.name}, {hc.wallScrambleFSM.fsm.name}");
    }

    private void EquipCloaklessCrest() {
        ToolItemManager.AutoEquip(ToolItemManager.GetCrestByName("Cloakless"), true, true);
    }

    private void EquipCursedCrest() {
        ToolItemManager.AutoEquip(ToolItemManager.GetCrestByName("Cursed"), true, true);
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

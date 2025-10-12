using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the dash slash with the Reaper crest equipped.
/// </summary>
internal class DashSlashReaper : SlashBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Call the base function with the correct parameters
        Play(playerObject, crestType, effectInfo, SlashType.Dash);
        
        // Also play an additional sound from the Sprint FSM
        var sprintFsm = HeroController.instance.sprintFSM;
        var playAudioAction = sprintFsm.GetFirstAction<PlayAudioEvent>("Reaper Upper");
        AudioUtil.PlayAudioEventAtPlayerObject(playAudioAction, playerObject);
    }
}

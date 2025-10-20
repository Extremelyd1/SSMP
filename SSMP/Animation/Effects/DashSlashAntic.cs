using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the start of a generic dash slash. Used with Hunter, Reaper, Beast, and Shaman crests.
/// </summary>
internal class DashSlashAntic : DamageAnimationEffect {
    /// <summary>
    /// Static instance for access by multiple animation clips in <see cref="AnimationManager"/>.
    /// </summary>
    private static DashSlashAntic? _instance;
    /// <inheritdoc cref="_instance" />
    public static DashSlashAntic Instance => _instance ??= new DashSlashAntic();

    /// <summary>
    /// Private constructor to ensure only the above static members can construct this class.
    /// </summary>
    private DashSlashAntic() {
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var sprintFsm = HeroController.instance.sprintFSM;
        PlayAudioEvent playAudioAction;
        switch (crestType) {
            case CrestType.Hunter:
            case CrestType.Witch:
                playAudioAction = sprintFsm.GetFirstAction<PlayAudioEvent>("Attack Antic");
                AudioUtil.PlayAudioEventAtPlayerObject(playAudioAction, playerObject);
                break;
            case CrestType.Reaper:
                var playRandomAudioClipAction = sprintFsm.GetFirstAction<PlayRandomAudioClipTableV2>("Reaper Antic");
                AudioUtil.PlayRandomAudioClipAtPlayerObject(playRandomAudioClipAction, playerObject);
                break;
            case CrestType.Beast:
                playAudioAction = sprintFsm.GetFirstAction<PlayAudioEvent>("Warrior Antic");
                AudioUtil.PlayAudioEventAtPlayerObject(playAudioAction, playerObject);
                break;
            case CrestType.Architect:
                playAudioAction = sprintFsm.GetFirstAction<PlayAudioEvent>("Drill Charge Start");
                AudioUtil.PlayAudioEventAtPlayerObject(playAudioAction, playerObject);
                break;
            case CrestType.Shaman:
                playAudioAction = sprintFsm.GetFirstAction<PlayAudioEvent>("Shaman Antic");
                AudioUtil.PlayAudioEventAtPlayerObject(playAudioAction, playerObject);
                break;
            default:
                return;
        }
    }

    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return null;
    }
}

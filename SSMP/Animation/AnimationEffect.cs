using HutongGames.PlayMaker.Actions;
using SSMP.Game.Settings;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation;

/// <summary>
/// Abstract base class for animation effects.
/// </summary>
internal abstract class AnimationEffect : IAnimationEffect {
    /// <summary>
    /// The current <see cref="ServerSettings"/> instance.
    /// </summary>
    protected ServerSettings ServerSettings = null!;

    /// <inheritdoc/>
    public abstract void Play(GameObject playerObject, CrestType crestType, ushort playerId, byte[]? effectInfo);

    /// <inheritdoc/>
    public abstract byte[]? GetEffectInfo();

    /// <inheritdoc/>
    public void SetServerSettings(ServerSettings serverSettings) {
        ServerSettings = serverSettings;
    }

    /// <summary>
    /// Locate the damages_enemy FSM and change the attack direction to the given direciton. This will ensure that
    /// enemies are getting knocked back in the correct direction from remote player's attacks.
    /// </summary>
    /// <param name="targetObject">The target GameObject to change.</param>
    /// <param name="direction">The direction in float that the damage is coming from.</param>
    protected static void ChangeAttackDirection(GameObject targetObject, float direction) {
        var damageFsm = targetObject.LocateMyFSM("damages_enemy");
        if (damageFsm == null) {
            return;
        }
        
        // Find the variable that controls the slash direction for damaging enemies
        var directionVar = damageFsm.FsmVariables.GetFsmFloat("direction");
        directionVar.Value = direction;
    }


    /// <summary>
    /// Plays a one-shot audio clip at the specified GameObject source.
    /// </summary>
    /// <param name="source">The object to play the sound at.</param>
    /// <param name="audio">The audio clip to be played.</param>
    protected static void PlaySound(GameObject source, AudioPlayerOneShotSingle audio) {
        AudioUtil.PlayAudioOneShotSingleAtPlayerObject(audio, source);
    }

    /// <summary>
    /// Plays a specified audio event at the specified GameObject source.
    /// </summary>
    /// <param name="source">The object to play the sound at.</param>
    /// <param name="audio">The FSM action with the audio clip to be played.</param>
    protected static void PlaySound(GameObject source, PlayAudioEvent audio) {
        AudioUtil.PlayAudioEventAtPlayerObject(audio, source);
    }

    /// <summary>
    /// Plays a random sound effect at the specified GameObject source
    /// </summary>
    /// <param name="source">The object to play the sound at.</param>
    /// <param name="getAction">The FSM action with the audio table.</param>
    /// <param name="playAction">The FSM action with the audio playing function.</param>
    protected static void PlaySound(GameObject source, GetRandomAudioClipFromTable getAction, PlayAudioEvent playAction) {
        AudioUtil.PlayAudioEventWithRandomAudioClipFromTableAtPlayerObject(
                playAction,
                getAction,
                source
            );
    }
}

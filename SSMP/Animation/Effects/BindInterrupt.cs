using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Animation.Effects;

/// <summary>
/// Class for the animation effect of a bind (healing) being interrupted by an attack.
/// </summary>
internal class BindInterrupt : Bind {
    /// <summary>
    /// Static instance for access by multiple animation clips in <see cref="AnimationManager"/>.
    /// </summary>
    public static readonly BindInterrupt Instance = new();

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var flags = new Flags(effectInfo);

        // Create bind effects object
        if (!CreateObjects(playerObject, out var bindEffects)) {
            return;
        }

        // Stop any effects currently playing, this effect "interrupts" everything else
        ForceStopAllEffects(bindEffects);

        // Play a warding bell explosion if they had it equipped, otherwise do the normal one
        if (flags.BindBell) {
            PlayBellExplode(bindEffects);
        } else {
            PlayBindInterrupt(bindEffects);
        }
    }

    /// <summary>
    /// Plays the normal bind interrupt animation
    /// </summary>
    private void PlayBindInterrupt(GameObject bindEffects) {
        // Find prefab
        var stateName = "Remove Silk?";
        var bindBurstSpawner = GetOrFindBindFsm().GetFirstAction<SpawnObjectFromGlobalPool>(stateName);

        // Spawn in effect
        var burst = EffectUtils.SpawnGlobalPoolObject(bindBurstSpawner, bindEffects.transform, 5f);
        if (burst == null) {
            Logger.Warn("Unable to create bind interrupt effect");
            return;
        }

        // Play audio
        var audio = GetOrFindBindFsm().GetFirstAction<PlayAudioEvent>(stateName);
        AudioUtil.PlayAudio(audio, bindEffects.transform.parent.gameObject);

        // Remove haze and camera controls
        burst.DestroyGameObjectInChildren("haze2");
        burst.DestroyComponentsInChildren<CameraControlAnimationEvents>();
    }

    /// <summary>
    /// Creates a Warding Bell explosion
    /// </summary>
    private void PlayBellExplode(GameObject bindEffects) {
        // Locate warding bell FSM
        var bellFsm = HeroController.instance.bellBindFSM;

        if (bellFsm == null) {
            Logger.Warn("Unable to find warding bell fsm");
            return;
        }

        if (bellFsm.FsmStates.Length == 1) {
            bellFsm.Init();
        }

        var stateName = "Burst";
        var spawner = bellFsm.GetFirstAction<SpawnObjectFromGlobalPool>(stateName);

        // Spawn warding bell
        var bindBell = EffectUtils.SpawnGlobalPoolObject(spawner, bindEffects.transform, 5f);
        if (bindBell == null) {
            return;
        }

        // Play sound
        var audio = bellFsm.GetFirstAction<PlayAudioEvent>(stateName);
        AudioUtil.PlayAudio(audio, bindBell);


        // Remove camera control and haze
        bindBell.DestroyComponentsInChildren<CameraControlAnimationEvents>();
        bindBell.DestroyGameObjectInChildren("haze2 (1)");

        // Add hitbox if appropriate
        if (ServerSettings.IsPvpEnabled && ShouldDoDamage) {
            var damager = bindBell.FindGameObjectInChildren("damager");
            if (damager != null) {
                AddDamageHeroComponent(damager, ServerSettings.WardingBellDamage);
            } else {
                Logger.Warn("Unable to add damager to warding bell burst");
            }
        }
    }
}

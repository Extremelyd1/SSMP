using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Animation.Effects;

/// <summary>
/// Class for the animation effect of a bind (healing) being interupted by an attack.
/// </summary>
internal class BindInterupt : Bind {
    /// <summary>
    /// Static instance for access by multiple animation clips in <see cref="AnimationManager"/>.
    /// </summary>
    private static BindInterupt? _instance;

    /// <inheritdoc cref="_instance" />
    public static BindInterupt Instance => _instance ??= new BindInterupt();

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, ushort playerId, byte[]? effectInfo) {
        Flags flags = new Flags(effectInfo);

        if (!CreateObjects(playerObject, out var bindEffects)) {
            return;
        }

        ForceStopAllEffects(bindEffects);

        if (flags.BindBell) {
            PlayBellExplode(bindEffects);
        } else {
            PlayBindInterupt(bindEffects);
        }
    }

    /// <summary>
    /// Plays the normal bind interput animation
    /// </summary>
    private void PlayBindInterupt(GameObject bindEffects) {
        // Find prefab
        var bindBurstSpawner = GetOrFindBindFsm().GetFirstAction<SpawnObjectFromGlobalPool>("Remove Silk?");
        if (bindBurstSpawner == null) {
            Logger.Warn("Unable to find bind burst effect spawner");
            return;
        }

        // Spawn in effect
        var burst = EffectUtils.SpawnGlobalPoolObject(bindBurstSpawner, bindEffects.transform, 5f);
        if (burst == null) {
            Logger.Warn("Unable to create bind burst effect");
            return;
        }

        // Play audio
        var audio = GetOrFindBindFsm().GetFirstAction<PlayAudioEvent>("Remove Silk?");
        PlaySound(bindEffects.transform.parent.gameObject, audio);

        // Remove haze and camera controls
        var haze = burst.FindGameObjectInChildren("haze2");
        if (haze != null) {
            GameObject.Destroy(haze);
        }

        var shaker = burst.GetComponentInChildren<CameraControlAnimationEvents>();
        if (shaker != null) {
            Component.DestroyImmediate(shaker);
        }
    }

    /// <summary>
    /// Creates a Warding Bell explosion
    /// </summary>
    private void PlayBellExplode(GameObject bindEffects) {
        Logger.Debug("Playing Bell Burst");
        
        // Initialize warding bell FSM if it isn't already.
        // This fills it in with the template
        var bellFsm = HeroController.instance.bellBindFSM;
        if (!bellFsm.fsm.initialized) {
            HeroController.instance.bellBindFSM.Init();
        }

        if (bellFsm == null) {
            Logger.Warn("Unable to find warding bell fsm");
            return;
        }

        var spawner = bellFsm.GetFirstAction<SpawnObjectFromGlobalPool>("Burst");

        if (spawner == null) {
            Logger.Warn("Unable to find warding bell spawner");
            return;
        }

        // Spawn warding bell
        var bindBell = EffectUtils.SpawnGlobalPoolObject(spawner, bindEffects.transform, 5f);
        if (bindBell == null) {
            return;
        }

        // Play sound
        var audio = bellFsm.GetFirstAction<PlayAudioEvent>("Burst");
        PlaySound(bindBell, audio);


        // Remove camera control and haze
        var shaker = bindBell.GetComponentInChildren<CameraControlAnimationEvents>();
        if (shaker != null) {
            Component.DestroyImmediate(shaker);
        }

        var haze = bindBell.FindGameObjectInChildren("haze2 (1)");
        if (haze != null) {
            GameObject.Destroy(haze);
        }

        // Add hitbox if appropriate
        if (ServerSettings.IsPvpEnabled && ShouldDoDamage) {
            var damager = bindBell.FindGameObjectInChildren("damager");
            if (damager != null) {
                AddDamageHeroComponent(damager);
            } else {
                Logger.Warn("Unable to add damager to warding bell burst");
            }
        }
    }
}

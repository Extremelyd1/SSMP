using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using static UnityEngine.ParticleSystem;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Animation.Effects;

internal class BindFail : Bind {
    /// <summary>
    /// Static instance for access by multiple animation clips in <see cref="AnimationManager"/>.
    /// </summary>
    private static BindFail? _instance;
    /// <inheritdoc cref="_instance" />
    public static BindFail Instance => _instance ??= new BindFail();
    public override void Play(GameObject playerObject, CrestType crestType, ushort playerId, byte[]? effectInfo) {
        Flags flags = new Flags(effectInfo);

        if (!CreateObjects(playerObject, out var bindEffects)) {
            return;
        }

        ForceStopAllEffects(bindEffects);

        if (flags.BindBell) {
            PlayBellBurst(bindEffects);
        } else {
            PlayBindBurst(bindEffects);
        }
    }

    private void PlayBindBurst(GameObject bindEffects) {
        var bellBurstSpawner = GetOrFindBindFsm().GetFirstAction<SpawnObjectFromGlobalPool>("Remove Silk?");
        if (bellBurstSpawner == null) {
            Logger.Warn("Unable to find Bell Burst spawner");
            return;
        }

        var globalBellBurst = bellBurstSpawner.gameObject.Value;
        var burst = EffectUtils.SpawnGlobalPoolObject(globalBellBurst, bindEffects.transform);

        if (burst == null) {
            Logger.Warn("Unable to create Bell Burst");
            return;
        }

        burst.DestroyAfterTime(5f);
        if (ServerSettings.IsPvpEnabled && ShouldDoDamage) {
            AddDamageHeroComponent(burst);
        }

        var haze = burst.FindGameObjectInChildren("haze2");
        if (haze != null) {
            GameObject.Destroy(haze);
        }

        var shaker = burst.GetComponentInChildren<CameraControlAnimationEvents>();
        if (shaker != null) {
            Component.DestroyImmediate(shaker);
        }
    }

    private void PlayBellBurst(GameObject bindEffects) {
        Logger.Info("Playing Bell Burst");
        
        var bellFsm = HeroController.instance.bellBindFSM;
        if (!bellFsm.fsm.initialized) {
            HeroController.instance.bellBindFSM.Init();
        }

        if (bellFsm == null) {
            Logger.Warn("Unable to find bind bell fsm");
            return;
        }

        var audio = bellFsm.GetFirstAction<PlayAudioEvent>("Burst");
        var spawner = bellFsm.GetFirstAction<SpawnObjectFromGlobalPool>("Burst");

        if (spawner == null) {
            Logger.Warn("Unable to find bind bell spawner");
            return;
        }

        var bindBell = EffectUtils.SpawnGlobalPoolObject(spawner.gameObject.Value, bindEffects.transform);
        var shaker = bindBell.GetComponentInChildren<CameraControlAnimationEvents>();
        if (shaker != null) {
            Component.DestroyImmediate(shaker);
        }

        var haze = bindBell.FindGameObjectInChildren("haze2 (1)");
        if (haze != null) {
            GameObject.Destroy(haze);
        }

        if (ServerSettings.IsPvpEnabled && ShouldDoDamage) {
            var damager = bindBell.FindGameObjectInChildren("damager");
            if (damager != null) {
                AddDamageHeroComponent(damager);
            } else {
                Logger.Warn("Unable to add damager to bind bell burst");
            }
        }
    }
}

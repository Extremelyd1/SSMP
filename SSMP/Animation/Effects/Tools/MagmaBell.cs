using System;
using System.Collections;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Tools;

/// <summary>
/// Class for the tool effect of Magma Bell (fire protection).
/// </summary>
internal class MagmaBell : AnimationEffect {
    /// <summary>
    /// Name of the magma bell starting object name.
    /// </summary>
    private const string MagmaBellStartName = "Magma Bell Start";

    /// <summary>
    /// Name of the magma bell recharging object name.
    /// </summary>
    private const string MagmaBellRechargeName = "Magma Bell Recharge";

    public static readonly MagmaBell Instance = new();

    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return null;
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Two parts: 1. when hit and 2. when recovering after some delay
        if (effectInfo is [1]) {
            PlayRecharge(playerObject);
            return;
        }


        // Find existing effect
        var effects = GetPlayerEffects(playerObject);
        var magmaStart = effects.FindGameObjectInChildren(MagmaBellStartName);

        // Create effect if needed
        if (!magmaStart) {
            var prefab = HeroController.instance.lavaBellEffectPrefab;

            magmaStart = EffectUtils.SpawnGlobalPoolObject(prefab, effects.transform, 0, true);
            if (!magmaStart) return;

            magmaStart.transform.localPosition = Vector3.zero;
            magmaStart.transform.localScale = new Vector3(0.5f, 0.5f, 1);
            magmaStart.name = MagmaBellStartName;
            magmaStart.DestroyComponent<CameraControlAnimationEvents>();
        }

        // Toggle effect
        magmaStart.SetActive(false);
        magmaStart.SetActive(true);
    }

    /// <summary>
    /// Plays the recharge animation.
    /// </summary>
    /// <param name="playerObject">The player to use the animation on.</param>
    private static void PlayRecharge(GameObject playerObject) {
        // Find existing effect
        var effects = GetPlayerEffects(playerObject);
        var magmaRecharge = effects.FindGameObjectInChildren(MagmaBellRechargeName);

        // Create effect if needed
        if (!magmaRecharge) {
            var prefab = HeroController.instance.lavaBellRechargeEffectPrefab;

            magmaRecharge = EffectUtils.SpawnGlobalPoolObject(prefab, effects.transform, 0, true);
            if (!magmaRecharge) return;

            magmaRecharge.transform.localPosition = Vector3.zero;
            magmaRecharge.name = MagmaBellRechargeName;
            magmaRecharge.DestroyComponent<CameraControlAnimationEvents>();
        }

        // Toggle effect
        magmaRecharge.SetActive(false);
        magmaRecharge.SetActive(true);
    }

    /// <summary>
    /// Adds a hook for when the bell is recharged.
    /// </summary>
    /// <param name="onTrigger">The hook to run.</param>
    public static void HookRecharge(Action onTrigger) {
        // Create coroutine since we have to wait for the prefab to be set
        static IEnumerator DoHook(Action onTrigger) {
            // Wait for prefab to be spawned
            yield return null;

            var prefab = HeroController.instance.spawnedLavaBellRechargeEffect;
            if (prefab == null) {
                yield break;
            }

            // Add enable hook
            var hook = prefab.AddComponent<UnityMessageListener>();

            hook.Enabled += onTrigger;
        }

        MonoBehaviourUtil.Instance.StartCoroutine(DoHook(onTrigger));
    }
}

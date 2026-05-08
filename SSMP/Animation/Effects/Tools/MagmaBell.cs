using System.Collections;
using GlobalSettings;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Tools;

/// <summary>
/// Class for the tool effect of Magma Bell (fire protection).
/// </summary>
internal class MagmaBell : BaseTool {
    /// <summary>
    /// Name of the magma bell starting object name.
    /// </summary>
    private const string MagmaBellStartName = "Magma Bell Start";

    /// <summary>
    /// Name of the magma bell recharging object name.
    /// </summary>
    private const string MagmaBellRechargeName = "Magma Bell Recharge";

    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return null;
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Two parts: 1. when hit and 2. when recovering after some delay

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

        // Start the recharge effect
        MonoBehaviourUtil.Instance.StartCoroutine(PlayRecharge(playerObject));
    }

    /// <summary>
    /// Plays the recharge animation.
    /// </summary>
    /// <param name="playerObject">The player to use the animation on.</param>
    private static IEnumerator PlayRecharge(GameObject playerObject) {
        // Wait for bell to recharge
        yield return new WaitForSeconds(Gameplay.LavaBellCooldownTime - 1);

        // Player has exited the scene, don't play.
        if (!playerObject.activeInHierarchy) yield break;

        // Find existing effect
        var effects = GetPlayerEffects(playerObject);
        var magmaRecharge = effects.FindGameObjectInChildren(MagmaBellRechargeName);

        // Create effect if needed
        if (!magmaRecharge) {
            var prefab = HeroController.instance.lavaBellRechargeEffectPrefab;

            magmaRecharge = EffectUtils.SpawnGlobalPoolObject(prefab, effects.transform, 0, true);
            if (!magmaRecharge) yield break;

            magmaRecharge.transform.localPosition = Vector3.zero;
            magmaRecharge.name = MagmaBellRechargeName;
            magmaRecharge.DestroyComponent<CameraControlAnimationEvents>();
        }

        // Toggle effect
        magmaRecharge.SetActive(false);
        magmaRecharge.SetActive(true);
    }
}

using System;
using System.Collections;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.Tools;

/// <summary>
/// Class for the tool effect of Magnetite Dice (chance to negate damage).
/// </summary>
internal class MagnetiteDice : AnimationEffect {
    /// <summary>
    /// Name of the magnetite dice effect object.
    /// </summary>
    private const string DiceName = "dice_shield_effect";

    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return null;
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Get existing effect
        var effects = GetPlayerEffects(playerObject);
        var dice = effects.FindGameObjectInChildren(DiceName);

        // Set up effect if needed
        if (dice == null) {
            var localDice = HeroController.instance.spawnedLuckyDiceShieldEffect;
            if (localDice == null) return;

            dice = Object.Instantiate(localDice, effects.transform);
            dice.transform.localPosition = new Vector3(0, 0, -0.02f);
            dice.transform.localScale = new Vector3(0.5f, 0.5f, 1);

            dice.DestroyComponent<CameraControlAnimationEvents>();
            dice.DestroyGameObjectInChildren("Vignette Cutout");
        }

        // Toggle effect
        dice.SetActive(false);
        dice.SetActive(true);
    }

    /// <summary>
    /// Adds a hook for when the dice are enabled.
    /// </summary>
    /// <param name="onTrigger">The hook to run.</param>
    public static void Hook(Action onTrigger) {
        // Create coroutine since we have to wait for the prefab to be set
        static IEnumerator DoHook(Action onTrigger) {
            // Wait for prefab to be spawned
            yield return null;

            var prefab = HeroController.instance.spawnedLuckyDiceShieldEffect;
            if (prefab == null) {
                yield break;
            }

            // Add enable hook
            var hook = prefab.AddComponent<UnityMessageListener>();

            hook.Enabled += onTrigger;
        }

        MonoBehaviourUtil.Instance.StartCoroutine(DoHook(onTrigger));
    }

    /// <summary>
    /// Removes the hook from the dice.
    /// </summary>
    public static void Unhook() {
        var prefab = HeroController.SilentInstance?.spawnedLuckyDiceShieldEffect;
        if (prefab == null) {
            return;
        }

        prefab.DestroyComponent<UnityMessageListener>();
    }
}

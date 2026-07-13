using System.Collections;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Tools;

/// <summary>
/// Class for the tool effect of the Straight Pin.
/// </summary>
internal class StraightPin : BaseAttackTool {
    /// <summary>
    /// Cached prefab for the attacking straight pin.
    /// </summary>
    private GameObject? _modifiedPrefab;

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Get original prefab
        var toolRoot = ToolItemManager.GetToolByName("Straight Pin");
        if (toolRoot is not ToolItemBasic tool) return;

        var poisoned = EffectIsPoisoned(effectInfo);

        // Set up modified prefab
        if (!_modifiedPrefab) {
            var prefab = tool.usageOptions.ThrowPrefab;
            _modifiedPrefab = EffectUtils.SpawnGlobalPoolObject(prefab, playerObject.transform, 0);
            if (!_modifiedPrefab) return;

            _modifiedPrefab.SetActive(false);
            _modifiedPrefab.name = "STRAIGHT PIN";
        }

        // Spawn in prefab
        _modifiedPrefab.transform.rotation = Quaternion.identity;
        var pin = _modifiedPrefab.Spawn(playerObject.transform.position);

        // Set scale
        var scale = playerObject.transform.localScale.x * -1;
        if (EffectIsOnWall(effectInfo)) {
            scale *= -1;
        }

        pin.transform.localScale = new Vector3(scale, 1, 1);

        // Set damage settings
        SetDamageHeroState(pin, ServerSettings.StraightPinDamage);

        // Set initial velocity
        if (pin.TryGetComponent<Rigidbody2D>(out var body)) {
            body.linearVelocityX = tool.usageOptions.ThrowVelocity.x * scale;
        }

        // Set poison settings and deflection
        if (pin.TryGetComponent<ToolPin>(out var controller)) {
            SetPinPoison(controller, poisoned);

            // Allows deflecting pins, but causes some side effects that make it look a bit worse (disappears immediately after hitting walls)
            controller.tinked = ServerSettings.IsPvpEnabled && ShouldDoDamage;
        }
    }

    /// <summary>
    /// Sets the poison status of a pin-based tool.
    /// </summary>
    /// <param name="controller">The pin controller.</param>
    /// <param name="isPoison">True if the pin should be poisoned, false if not.</param>
    public static void SetPinPoison(ToolPin controller, bool isPoison) {
        // Run at the end of the frame to ensure it's off
        static IEnumerator DoPoisonSet(ToolPin controller, bool isPoison) {
            yield return null;

            var main = controller.ptShatter.main;
            controller.isPoison = isPoison;

            // Toggle poison effect
            if (isPoison) {
                if ((bool) controller.getTintFrom) {
                    controller.sprite.EnableKeyword("CAN_HUESHIFT");
                    controller.sprite.SetFloat(PoisonTintBase.HueShiftPropId, controller.getTintFrom.PoisonHueShift);
                } else {
                    controller.sprite.EnableKeyword("RECOLOUR");
                    controller.sprite.color = controller.poisonTint;
                }
                main.startColor = controller.poisonTint;
                controller.ptPoisonIdle.Play();
            } else {
                controller.sprite.DisableKeyword("CAN_HUESHIFT");
                controller.sprite.DisableKeyword("RECOLOUR");
                controller.sprite.color = Color.white;
                main.startColor = controller.ptShatterDefaultColour;
                controller.ptPoisonIdle.Stop();
            }
        }

        MonoBehaviourUtil.Instance.StartCoroutine(DoPoisonSet(controller, isPoison));
    }
}

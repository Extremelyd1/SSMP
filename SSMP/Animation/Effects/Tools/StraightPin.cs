using SSMP.Internals;
using UnityEngine;

namespace SSMP.Animation.Effects.Tools;

internal class StraightPin : BaseTool {
    /// <summary>
    /// Cached prefab for the attacking straight pin
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
            _modifiedPrefab = EffectUtils.SpawnGlobalPoolObject(prefab, playerObject.transform, 0, false);
            if (!_modifiedPrefab) return;

            _modifiedPrefab.SetActive(false);
            _modifiedPrefab.name = "STRAIGHT PIN";

            AddDamageHeroComponent(_modifiedPrefab);
        }

        // Spawn in prefab
        var pin = _modifiedPrefab.Spawn(playerObject.transform.position);

        // Set scale
        var scale = playerObject.transform.localScale.x * -1;
        if (EffectIsOnWall(effectInfo)) {
            scale *= -1;
        }

        pin.transform.localScale = new Vector3(scale, 1, 1);

        // Set damage settings
        if (pin.TryGetComponent<DamageHero>(out var damager)) {
            damager.enabled = ShouldDoDamage && ServerSettings.IsPvpEnabled;
            damager.SetDamageAmount(1);
        }

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
}

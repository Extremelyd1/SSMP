using SSMP.Internals;
using UnityEngine;

namespace SSMP.Animation.Effects.Tools;

internal class StraightPin : BaseTool {

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
        pin.transform.localScale = new Vector3(playerObject.transform.localScale.x * -1, 1, 1);

        // Set damage settings
        if (pin.TryGetComponent<DamageHero>(out var damager)) {
            damager.enabled = ShouldDoDamage && ServerSettings.IsPvpEnabled;
            damager.SetDamageAmount(1);
        }

        // Set initial velocity
        if (pin.TryGetComponent<Rigidbody2D>(out var body)) {
            body.linearVelocityX = tool.usageOptions.ThrowVelocity.x * playerObject.transform.localScale.x * -1;
        }

        // Set poison settings
        SetPinPoison(pin, poisoned);
    }
}

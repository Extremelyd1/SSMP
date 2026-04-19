using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Movement;

internal class DoubleJump : DamageAnimationEffect {
    private const string JumpEffectName = "double_jump_feathers";

    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return [
            (byte)(ToolItemManager.IsToolEquipped("Brolly Spike") ? 1 : 0)
        ];
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Find or create effects
        var effects = playerObject.FindGameObjectInChildren("Effects");
        if (effects == null) {
            effects = new GameObject();
            effects.transform.SetParentReset(playerObject.transform);
        }

        // Find or create jump effect
        var effect = effects.FindGameObjectInChildren(JumpEffectName);
        if (effect == null) {
            var localEffect = HeroController.instance.doubleJumpEffectPrefab;
            effect = EffectUtils.SpawnGlobalPoolObject(localEffect, effects.transform, 0, true);
            if (effect == null) return;
            effect.name = JumpEffectName;

            // Remove components from newly created object
            effect.DestroyGameObjectInChildren("haze flash");
            effect.DestroyGameObjectInChildren("jump_double_glow");
            effect.DestroyComponent<Animator>();
            effect.SetActiveChildren(true);
        }

        // Refresh effect
        effect.SetActive(false);
        effect.SetActive(true);

        // Play sawtooth circlet if appropriate
        if (effectInfo is [1]) {
            UmbrellaInflate.Instance.PlayCirclet(playerObject);
        }
    }
}

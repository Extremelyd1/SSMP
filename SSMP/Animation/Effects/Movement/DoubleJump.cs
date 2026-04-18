using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Movement;

internal class DoubleJump : AnimationEffect {
    private const string JumpEffectName = "double_jump_feathers";

    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return null;
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var effects = playerObject.FindGameObjectInChildren("Effects");
        if (effects == null) {
            effects = new GameObject();
            effects.transform.SetParentReset(playerObject.transform);
        }

        var effect = effects.FindGameObjectInChildren(JumpEffectName);
        if (effect == null) {
            var localEffect = HeroController.instance.doubleJumpEffectPrefab;
            effect = EffectUtils.SpawnGlobalPoolObject(localEffect, effects.transform, 0, true);
            if (effect == null) return;
            effect.name = JumpEffectName;

            effect.DestroyGameObjectInChildren("haze flash");
            effect.DestroyGameObjectInChildren("jump_double_glow");
            effect.DestroyComponent<Animator>();
            effect.SetActiveChildren(true);
        }

        effect.SetActive(false);
        effect.SetActive(true);

    }
}

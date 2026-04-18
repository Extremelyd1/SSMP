using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects;

internal class DoubleJump : AnimationEffect {
    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return null;
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var localEffect = HeroController.instance.doubleJumpEffectPrefab;

        var effect = EffectUtils.SpawnGlobalPoolObject(localEffect, playerObject.transform, 2f);
        if (effect == null) return;

        effect.DestroyGameObjectInChildren("haze flash");
        effect.DestroyGameObjectInChildren("jump_double_glow");
    }
}

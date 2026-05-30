using SSMP.Animation.Effects.Tools;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Movement;

/// <summary>
/// Class for the animation effect of Faydown Cloak (double jump).
/// </summary>
internal class FaydownCloak : DamageAnimationEffect {
    /// <summary>
    /// The name of the game object for the effect for checking if it exists on a player object already.
    /// </summary>
    private const string JumpEffectName = "double_jump_feathers";

    /// <inheritdoc/>
    public override byte[] GetEffectInfo() {
        return [
            (byte)(ToolItemManager.IsToolEquipped("Brolly Spike") ? 1 : 0)
        ];
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Find or create effects
        var effects = GetPlayerEffects(playerObject);

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
            SawtoothCirclet.PlayCirclet(playerObject, ShouldDoDamage && ServerSettings.IsPvpEnabled, ServerSettings);
        }
    }
}

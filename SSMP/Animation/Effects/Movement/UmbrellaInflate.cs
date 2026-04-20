using SSMP.Animation.Effects.Tools;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Movement;

internal class UmbrellaInflate : DamageAnimationEffect {

    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return [
            (byte)(ToolItemManager.IsToolEquipped("Brolly Spike") ? 1 : 0)
        ];
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Get or create effect
        var created = TryGetEffect(playerObject, "umbrella_inflate_effect", out var effect);
        if (!effect) {
            return;
        }

        // Set up effect if created
        if (!created) {
            effect.transform.localPosition = new Vector3(0, -0.24f, 0);
            effect.transform.localScale = Vector3.one;

            effect.DestroyGameObjectInChildren("umbrella_float_fx_burst0002");
        }

        // Refresh particles
        effect.SetActive(false);
        effect.SetActive(true);

        // Enable sawtooth circlet if appropriate
        if (effectInfo is [1]) {
            SawtoothCirclet.PlayCirclet(playerObject, ShouldDoDamage && ServerSettings.IsPvpEnabled);
        }

    }
}

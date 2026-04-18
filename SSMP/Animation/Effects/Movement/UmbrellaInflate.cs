using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Movement;

internal class UmbrellaInflate : AnimationEffect {
    private const string UmbrellaInflateName = "umbrella_inflate_effect";

    public override byte[]? GetEffectInfo() {
        return null;
    }

    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var effects = playerObject.FindGameObjectInChildren("Effects");
        if (effects == null) {
            effects = new GameObject();
            effects.transform.SetParentReset(playerObject.transform);
        }

        var effect = effects.FindGameObjectInChildren(UmbrellaInflateName);
        if (effect == null) {
            var localEffect = HeroController.instance.umbrellaEffect;
            effect = Object.Instantiate(localEffect, effects.transform);
            effect.name = UmbrellaInflateName;
            effect.transform.localPosition = new Vector3(0, -0.24f, 0);
            effect.transform.localScale = Vector3.one;

            effect.DestroyGameObjectInChildren("umbrella_float_fx_burst0002");
        }

        effect.SetActive(false);
        effect.SetActive(true);
    }
}

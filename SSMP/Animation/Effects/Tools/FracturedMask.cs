using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Tools;

/// <summary>
/// Class for the tool effect of Fractured Mask (extra health point).
/// </summary>
internal class FracturedMask : AnimationEffect {
    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return null;
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Find effect
        var fsm = HeroController.instance.gameObject
            .FindGameObjectInChildren("Charm Effects")?
            .FindGameObjectInChildren("Fractured Mask Break")?
            .LocateMyFSM("Spawn Effect");

        if (fsm == null) return;

        // Spawn in the shatter particles
        var localMaskShatter = fsm.GetFirstAction<CreateObject>("Instantiate Effect");
        var mask = EffectUtils.SpawnGlobalPoolObject(
            localMaskShatter.gameObject.Value, 
            playerObject.transform, 
            5
        );

        mask?.DestroyComponent<CameraControlAnimationEvents>();
    }
}

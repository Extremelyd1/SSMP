using System;
using System.Collections.Generic;
using System.Text;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Tools;

internal class FracturedMask : BaseTool {
    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return null;
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var fsm = HeroController.instance.gameObject
            .FindGameObjectInChildren("Charm Effects")?
            .FindGameObjectInChildren("Fractured Mask Break")?
            .LocateMyFSM("Spawn Effect");

        if (fsm == null) return;

        var localMaskShatter = fsm.GetFirstAction<CreateObject>("Instantiate Effect");
        var mask = EffectUtils.SpawnGlobalPoolObject(localMaskShatter.gameObject.Value, playerObject.transform, 5);

        mask?.DestroyComponent<CameraControlAnimationEvents>();
    }
}

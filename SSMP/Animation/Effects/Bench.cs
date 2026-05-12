using SSMP.Animation.Effects.SilkSkills;
using SSMP.Animation.Effects.Tools;
using SSMP.Internals;
using UnityEngine;

namespace SSMP.Animation.Effects;

internal class Bench : AnimationEffect {
    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return null;
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Stop all tool/silk skill effects
        PaleNails.DespawnAllPlayerNails(playerObject);
        FleaBrew.StopBrew(playerObject);
    }
}

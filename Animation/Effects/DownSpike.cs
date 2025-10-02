using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the down slash animation. Also called down spike, which is used on some crests, but not
/// all. Distinctly different from down slash.
/// </summary>
internal class DownSpike : DownSpikeBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, byte[]? effectInfo) {
        // Call the base function of down spike with the correct parameters
        Play(playerObject, effectInfo, DownSpikeType.Normal);
    }
}

using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the normal slash.
/// </summary>
internal class Slash : SlashBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, byte[]? effectInfo) {
        // Call the base function with the correct parameters
        Play(playerObject, effectInfo, SlashType.Normal);
    }
}

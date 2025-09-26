using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the alternative slash animation (when Hornet swings her nail).
/// This is the slash effect that occurs the most.
/// </summary>
internal class AltSlash : SlashBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, byte[]? effectInfo) {
        // Call the base function with the correct parameters
        Play(playerObject, effectInfo, SlashType.Alt);
    }
}

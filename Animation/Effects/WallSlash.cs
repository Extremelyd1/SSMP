using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the wall slash animation (when Hornet swings her nail whilst grabbing on a sliding down
/// a wall).
/// </summary>
internal class WallSlash : SlashBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, byte[]? effectInfo) {
        // Call the base function with the correct parameters
        Play(playerObject, effectInfo, SlashType.Wall);
    }
}

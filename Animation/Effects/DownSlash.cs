using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the downwards slash. Not the down spike as with the Hunter crest equipped.
/// </summary>
internal class DownSlash : SlashBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, byte[]? effectInfo) {
        // Call the base function with the correct parameters
        Play(playerObject, effectInfo, SlashType.Down);
    }
}

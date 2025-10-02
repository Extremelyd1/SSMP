using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the alternate downwards slash. Not the alternate down spike as with the Hunter crest
/// equipped.
/// </summary>
internal class DownAltSlash : SlashBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, byte[]? effectInfo) {
        // Call the base function with the correct parameters
        Play(playerObject, effectInfo, SlashType.DownAlt);
    }
}

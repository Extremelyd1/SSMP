using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the upwards slash.
/// </summary>
internal class UpSlash : SlashBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, byte[]? effectInfo) {
        // Call the base function with the correct parameters
        Play(playerObject, effectInfo, SlashType.Up);
    }
}

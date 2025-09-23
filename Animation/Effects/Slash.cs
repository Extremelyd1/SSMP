using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the normal nail slash.
/// </summary>
internal class Slash : SlashBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, bool[] effectInfo) {
        // Call the base function with the correct parameters
        // Alternate slash is used here, because while the animation clip is called "Slash", it uses the alternate
        // slash instance
        Play(playerObject, effectInfo, HeroController.instance.alternateSlash, SlashType.Normal);
    }
}

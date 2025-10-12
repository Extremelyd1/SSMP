using SSMP.Internals;
using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the normal slash.
/// </summary>
internal class Slash : SlashBase {
    /// <summary>
    /// The slash type for the slash animation effect.
    /// </summary>
    private readonly SlashType _slashType;

    /// <summary>
    /// Construct the slash animation effect for the given slash type.
    /// </summary>
    /// <param name="slashType">The slash type.</param>
    public Slash(SlashType slashType) {
        _slashType = slashType;
    }
    
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Call the base function with the correct parameters
        Play(playerObject, crestType, effectInfo, _slashType);
    }
}

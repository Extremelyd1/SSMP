using SSMP.Game.Settings;
using UnityEngine;

namespace SSMP.Animation;

/// <summary>
/// Interface containing methods for handling animation effects that complement player animation.
/// </summary>
internal interface IAnimationEffect {
    /// <summary>
    /// Plays the animation effect for the given player object and with additional byte data array.
    /// </summary>
    /// <param name="playerObject">The GameObject representing the player.</param>
    /// <param name="effectInfo">A byte array containing effect info.</param>
    void Play(GameObject playerObject, byte[]? effectInfo);

    /// <summary>
    /// Get the effect info corresponding to this effect.
    /// </summary>
    /// <returns>A byte array containing effect info.</returns>
    byte[]? GetEffectInfo();

    /// <summary>
    /// Set the server settings so we can access it while playing the animation.
    /// </summary>
    /// <param name="serverSettings">The <see cref="ServerSettings"/> instance.</param>
    void SetServerSettings(ServerSettings serverSettings);
}

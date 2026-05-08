using GlobalSettings;

namespace SSMP.Animation.Effects.Tools;

/// <summary>
/// Base class for animations effects of tools that can deal damage to other players.
/// </summary>
internal abstract class BaseTool : DamageAnimationEffect {
    /// <summary>
    /// Determines whether the players tools have the Pollip Pouch poison effect.
    /// </summary>
    /// <returns>True if the player has the Pollip Pouch equipped, otherwise false.</returns>
    protected static bool HasPoison() {
        return Gameplay.PoisonPouchTool.IsEquipped;
    }
}

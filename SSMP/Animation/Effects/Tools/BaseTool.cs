using System;
using System.Collections.Generic;
using System.Text;
using GlobalSettings;

namespace SSMP.Animation.Effects.Tools;

internal abstract class BaseTool : DamageAnimationEffect {
    /// <summary>
    /// Determines whether the players tools have the Pollip Pouch poison effect.
    /// </summary>
    /// <returns>True if the player has the Pollip Pouch equipped.</returns>
    protected static bool HasPoison() {
        return Gameplay.PoisonPouchTool.IsEquipped;
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using GlobalSettings;

namespace SSMP.Animation.Effects.Tools;

internal abstract class BaseTool : DamageAnimationEffect {
    protected static bool HasPoison() {
        return Gameplay.PoisonPouchTool.IsEquipped;
    }
}

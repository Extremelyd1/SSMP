using System.Collections.Generic;
using GlobalSettings;

namespace SSMP.Animation.Effects.Tools;

/// <summary>
/// Base class for animation effects of attack tools.
/// </summary>
internal abstract class BaseAttackTool : DamageAnimationEffect {
    /// <summary>
    /// Map of attack tool names to their corresponding enum value.
    /// </summary>
    public static readonly Dictionary<string, AnimationClip> ToolNameMap = new() {
        { "Straight Pin", AnimationClip.ToolStraightPin },
        { "Tri Pin", AnimationClip.ToolThreefoldPin } ,
        //{ "Sting Shard", AnimationClip.ToolStingShard } ,
        { "Tack", AnimationClip.ToolTacks } ,
        { "Harpoon", AnimationClip.ToolLongpin } ,
        //{ "Curve Claws", AnimationClip.ToolCurveclaw } ,
        //{ "Curve Claws Upgraded", AnimationClip.ToolCurvesickle } ,
        //{ "Shakra Ring", AnimationClip.ToolThrowingRing } ,
        //{ "Pimpilo", AnimationClip.ToolPimpillo } ,
        //{ "Conch Drill", AnimationClip.ToolConchcutter } ,
        //{ "Cogwork Saw", AnimationClip.ToolCogworkWheel } ,
        //{ "Cogwork Flier", AnimationClip.ToolCogfly } ,

         //These are in the tool FSM. They might be handled differently
        //{ "WebShot Weaver", AnimationClip.ToolSilkshotOriginal } ,
        //{ "WebShot Architect", AnimationClip.ToolSilkshotArchitect } ,
        //{ "WebShot Forge", AnimationClip.ToolSilkshotForge } ,
        //{ "Screw Attack", AnimationClip.ToolDelversDrill } ,
        //{ "Rosary Cannon", AnimationClip.ToolRosaryCannon } ,
        //{ "Lightning Rod", AnimationClip.ToolVoltvesselSpear } ,
        //{ "Flintstone", AnimationClip.ToolFlintslate } ,
        //{ "Silk Snare", AnimationClip.ToolSnareSetter } ,
        //{ "Flea Brew", AnimationClip.ToolFleaBrew },
        //{ "Lifeblood Syringe", AnimationClip.ToolPlasmiumPhial },
        //{ "Extractor", AnimationClip.ToolNeedlePhial }
    };

    /// <summary>
    /// Gets important tool information, such as poison status and the tool being used.
    /// </summary>
    /// <returns>The tool info.</returns>
    public static byte[] GetToolInfo() {
        return [
            (byte) (HasPoison() ? 1 : 0),
            (byte) (HeroController.instance.IsOnWall() ? 1 : 0)
        ];
    }

    /// <inheritdoc/>
    public override byte[] GetEffectInfo() {
        return GetToolInfo();
    }

    /// <summary>
    /// Determines whether the local player's tools have the Pollip Pouch poison effect.
    /// </summary>
    /// <returns>True if the player has the Pollip Pouch equipped, otherwise false.</returns>
    protected static bool HasPoison() {
        return Gameplay.PoisonPouchTool.IsEquipped;
    }

    /// <summary>
    /// Determines if an effect should have the poison properties.
    /// </summary>
    /// <param name="effectInfo">The effect info sent over the network.</param>
    /// <returns>True if the effect should use poison, false otherwise.</returns>
    protected static bool EffectIsPoisoned(byte[]? effectInfo) {
        return effectInfo != null && effectInfo.Length > 0 && effectInfo[0] == 1;
    }

    /// <summary>
    /// Determines if the remote player is on a wall.
    /// </summary>
    /// <param name="effectInfo">The effect info sent over the network.</param>
    /// <returns>True if the player is on a wall, false otherwise.</returns>
    protected static bool EffectIsOnWall(byte[]? effectInfo) {
        return effectInfo != null && effectInfo.Length > 2 && effectInfo[2] == 1;
    }
}

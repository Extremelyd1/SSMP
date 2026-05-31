using System.Collections;
using System.Collections.Generic;
using GlobalSettings;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Animation.Effects.Tools;

internal class BaseTool : DamageAnimationEffect {

    /// <summary>
    /// Cached instance of the class.
    /// </summary>
    public static readonly BaseTool Instance = new();

    /// <summary>
    /// Map of attack tools to their corresponding effect classes.
    /// </summary>
    private static readonly Dictionary<AttackTool, BaseTool> ToolEffectMap = new() {
        //{ AttackTool.FleaBrew, FleaBrew.Instance },
        { AttackTool.StraightPin, new StraightPin() },
        { AttackTool.ThreefoldPin, new ThreefoldPin() },
        { AttackTool.LongPin, new LongPin() },
        { AttackTool.Tacks, new Tacks() }
    };

    /// <summary>
    /// Map of attack tool names to their corresponding enum value.
    /// </summary>
    private static readonly Dictionary<string, AttackTool> ToolNameMap = new() {
        { "Straight Pin", AttackTool.StraightPin },
        { "Tri Pin", AttackTool.ThreefoldPin } ,
        //{ "Sting Shard", AttackTool.StingShard } ,
        { "Tack", AttackTool.Tacks } ,
        { "Harpoon", AttackTool.LongPin } ,
        //{ "Curve Claws", AttackTool.Curveclaw } ,
        //{ "Curve Claws Upgraded", AttackTool.Curvesickle } ,
        //{ "Shakra Ring", AttackTool.ThrowingRing } ,
        //{ "Pimpilo", AttackTool.Pimpillo } ,
        //{ "Conch Drill", AttackTool.Conchcutter } ,
        //{ "Cogwork Saw", AttackTool.CogworkWheel } ,
        //{ "Cogwork Flier", AttackTool.Cogfly } ,

        // These are in the tool FSM. They might be handled differently
        ////{ "WebShot Weaver", AttackTool.SilkshotOriginal } ,
        ////{ "WebShot Architect", AttackTool.SilkshotArchitect } ,
        ////{ "WebShot Forge", AttackTool.SilkshotForge } ,
        ////{ "Screw Attack", AttackTool.DelversDrill } ,
        ////{ "Rosary Cannon", AttackTool.RosaryCannon } ,
        ////{ "Lightning Rod", AttackTool.VoltvesselSpear } ,
        ////{ "Flintstone", AttackTool.Flintslate } ,
        ////{ "Silk Snare", AttackTool.SnareSetter } ,
        ////{ "Flea Brew", AttackTool.FleaBrew },
        ////{ "Lifeblood Syringe", AttackTool.PlasmiumPhial },
        ////{ "Extractor", AttackTool.NeedlePhial }
    };

    /// <summary>
    /// Gets important tool information, such as poison status and the tool being used.
    /// </summary>
    /// <param name="toolName">The name of the tool being used.</param>
    /// <returns>The tool info.</returns>
    public static byte[]? GetToolInfo(string toolName) {
        if (!ToolNameMap.TryGetValue(toolName, out var tool)) {
            return Instance.GetEffectInfo();
        }

        return [
            Instance.GetEffectInfo()![0],
            (byte) tool,
            (byte) (HeroController.instance.IsOnWall() ? 1 : 0)
        ];
    }

    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return [
            (byte) (HasPoison() ? 1 : 0),
        ];
    }

    /// <summary>
    /// Determines whether the players tools have the Pollip Pouch poison effect.
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

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        if (effectInfo == null || effectInfo.Length < 2) {
            return;
        }

        var toolType = (AttackTool) effectInfo[1];

        if (ToolEffectMap.TryGetValue(toolType, out var tool)) {
            Logger.Info($"Found tool in map for tool {toolType}");
            tool.ServerSettings = ServerSettings;
            tool.SetShouldDoDamage(ShouldDoDamage);
            tool.Play(playerObject, crestType, effectInfo);
        } else {
            Logger.Info($"No tool in map for tool {toolType}");
        }
    }

    /// <summary>
    /// Sets the poison status of a pin-based tool.
    /// </summary>
    /// <param name="controller">The pin controller.</param>
    /// <param name="isPoison">True if the pin should be poisoned, false if not.</param>
    protected static void SetPinPoison(ToolPin controller, bool isPoison) {

        // Run at the end of the frame to ensure it's off
        static IEnumerator DoPoisonSet(ToolPin controller, bool isPoison) {
            yield return null;

            var main = controller.ptShatter.main;
            controller.isPoison = isPoison;

            // Toggle poison effect
            if (isPoison) {
                if ((bool) controller.getTintFrom) {
                    controller.sprite.EnableKeyword("CAN_HUESHIFT");
                    controller.sprite.SetFloat(PoisonTintBase.HueShiftPropId, controller.getTintFrom.PoisonHueShift);
                } else {
                    controller.sprite.EnableKeyword("RECOLOUR");
                    controller.sprite.color = controller.poisonTint;
                }
                main.startColor = controller.poisonTint;
                controller.ptPoisonIdle.Play();
            } else {
                controller.sprite.DisableKeyword("CAN_HUESHIFT");
                controller.sprite.DisableKeyword("RECOLOUR");
                controller.sprite.color = Color.white;
                main.startColor = controller.ptShatterDefaultColour;
                controller.ptPoisonIdle.Stop();
            }
        }

        MonoBehaviourUtil.Instance.StartCoroutine(DoPoisonSet(controller, isPoison));
    }
}

internal enum AttackTool {
    StraightPin,
    ThreefoldPin,
    StingShard,
    Tacks,
    LongPin,
    Curveclaw,
    Curvesickle,
    ThrowingRing,
    Pimpillo,
    Conchcutter,
    CogworkWheel,
    // Probably have to handle differently
    Cogfly,

    // Tools in the tool FSM trigger multiple times.
    // To avoid duplicates, they should be handled manually.
    SilkshotOriginal, // In tool FSM
    SilkshotArchitect, // In tool FSM
    SilkshotForge, // In tool FSM
    DelversDrill, // In tool FSM
    RosaryCannon, // In tool FSM
    VoltvesselSpear, // In tool FSM
    VoltvesselBalls, // In tool FSM
    Flintslate, // In tool FSM
    SnareSetter, // In tool FSM
    FleaBrew, // In tool FSM
    PlasmiumPhial, // In tool FSM
    NeedlePhial // In tool FSM
}

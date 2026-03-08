using System;
using System.Collections.Generic;
using System.Text;
using GlobalSettings;
using HarmonyLib;
using SSMP.Animation;
using SSMP.Animation.Effects;
using SSMP.Game.Settings;
using SSMP.Internals;
using UnityEngine;
using static UnityEngine.Rendering.RayTracingAccelerationStructure;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Testing;

public class PlayerAnimation : MonoBehaviour {

    CrestType DetermineCrest() {
        var playerCrest = HeroController.instance.playerData.CurrentCrestID! ?? "";
        //if (playerCrest == null) return CrestType.Hunter;

        if (playerCrest == Gameplay.HunterCrest.name) return CrestType.Hunter;
        if (playerCrest == Gameplay.HunterCrest2.name) return CrestType.HunterV2;
        if (playerCrest == Gameplay.HunterCrest3.name) return CrestType.HunterV3;
        if (playerCrest == Gameplay.ReaperCrest.name) return CrestType.Reaper;

        if (playerCrest == Gameplay.WandererCrest.name) return CrestType.Wanderer;
        if (playerCrest == Gameplay.CloaklessCrest.name) return CrestType.Cloakless;
        if (playerCrest == Gameplay.WarriorCrest.name) return CrestType.Beast;
        if (playerCrest == Gameplay.ToolmasterCrest.name) return CrestType.Architect;
        if (playerCrest == Gameplay.CursedCrest.name) return CrestType.Cursed;
        if (playerCrest == Gameplay.SpellCrest.name) return CrestType.Shaman;
        if (playerCrest == Gameplay.WitchCrest.name) return CrestType.Witch;

        return CrestType.Hunter;
    }

    public bool IsEquipped(string toolName) {
        return ToolItemManager.IsToolEquipped(toolName);
    }

    public void StartAnimation() {
        gameObject.SetActive(true);


        string clipName = "BindCharge Ground";
        CrestType crest = DetermineCrest();

        var hornet = HeroController.instance.gameObject;
        var position = hornet.transform.position;
        var playerPosition = new Vector3(position.x + 10, position.y, position.z);
        transform.position = playerPosition;

        var playerObject = transform.GetChild(0).gameObject;
        var bind = new Bind();

        var info = bind.GetEffectInfo();
        Logger.Info(string.Join(", ", info));

        //bind.UsingClawMirrors = ToolItemManager.IsToolEquipped("Dazzle Blind");
        //bind.ClawMirrorsUpgraded = ToolItemManager.IsToolEquipped("Dazzle Blind Upgraded");
        //bind.UsingBindBell = ToolItemManager.IsToolEquipped("Bell Bind");
        //bind.UsingMultiBind = ToolItemManager.IsToolEquipped("Multibind");
        //bind.UsingQuickBind = ToolItemManager.IsToolEquipped("Quickbind");
        //bind.UsingReserveBind = ToolItemManager.IsToolEquipped("Reserve Bind");
        //bind.InAir = !HeroController.instance.onFlatGround;
        //bind.Maggoted = HeroController.instance.cState.isMaggoted;

        SetSettings(bind);
        bind.Play(playerObject, crest, info);
        
        var spriteAnimator = playerObject.GetComponent<tk2dSpriteAnimator>();

        var clip = spriteAnimator.GetClipByName(clipName);
        spriteAnimator.PlayFromFrame(clip, 0);
    }

    void SetSettings(AnimationEffect bind) {
        var plugin = GameObject.FindFirstObjectByType<SSMPPlugin>();
        var traverse = Traverse.Create(plugin).Field("_gameManager").Field("_clientManager").Field("_serverSettings");
        var settings = traverse.GetValue<ServerSettings>();

        bind.SetServerSettings(settings);
    }

    public void StopAnimation() {
        var clipName = "BindBurst Ground";

        var playerObject = transform.GetChild(0).gameObject;

        var bind = new BindBurst();
        SetSettings(bind);
        bind.SetShouldDoDamage(true);

        var info = bind.GetEffectInfo();
        CrestType crest = DetermineCrest();

        bind.Play(playerObject, crest, info);

        var spriteAnimator = playerObject.GetComponent<tk2dSpriteAnimator>();

        var clip = spriteAnimator.GetClipByName(clipName);
        spriteAnimator.PlayFromFrame(clip, 0);
    }
}

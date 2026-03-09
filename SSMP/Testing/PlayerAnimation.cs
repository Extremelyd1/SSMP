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

    void SetSettings(AnimationEffect bind) {
        var plugin = GameObject.FindFirstObjectByType<SSMPPlugin>();
        var traverse = Traverse.Create(plugin).Field("_gameManager").Field("_clientManager").Field("_serverSettings");
        var settings = traverse.GetValue<ServerSettings>();

        bind.SetServerSettings(settings);
    }

    void Init() {
        gameObject.SetActive(true);
        ToolItemManager.SetEquippedCrest(Gameplay.CursedCrest.name);

        var hornet = HeroController.instance.gameObject;
        var position = hornet.transform.position;
        var playerPosition = new Vector3(position.x + 10, position.y, position.z);
        transform.position = playerPosition;
    }

    public void StartPreAnimation() {
        Init();
        CrestType crest = DetermineCrest();

        if (crest != CrestType.Shaman) {
            return;
        }
        string clipName = "BindCharge Ground";

        var playerObject = transform.GetChild(0).gameObject;
        var bind = new Bind { ShamanDoneFalling = false };

        var info = bind.GetEffectInfo();

        SetSettings(bind);
        bind.Play(playerObject, crest, info);

        var spriteAnimator = playerObject.GetComponent<tk2dSpriteAnimator>();

        var clip = spriteAnimator.GetClipByName(clipName);
        spriteAnimator.PlayFromFrame(clip, 0);
    }

    public void StartAnimation() {

        Init();

        string clipName = "BindCharge Ground";
        CrestType crest = DetermineCrest();

        var playerObject = transform.GetChild(0).gameObject;
        var bind = new Bind { ShamanDoneFalling = true };

        var info = bind.GetEffectInfo();

        SetSettings(bind);
        bind.Play(playerObject, crest, info);
        
        var spriteAnimator = playerObject.GetComponent<tk2dSpriteAnimator>();

        var clip = spriteAnimator.GetClipByName(clipName);
        spriteAnimator.PlayFromFrame(clip, 0);
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

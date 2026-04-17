using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

internal class SilkSpear : BaseSilkSkill {
    private const string SpearObjectName = "Needle Throw";
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var spear = GetSilkSpear(playerObject);
        if (!spear) return;

        var parent = spear.FindGameObjectInChildren("needle_throw_simple");
        if (!parent) return;


        // Set volt settings
        var volt = IsVolt(effectInfo);

        var zapThread = parent
            .FindGameObjectInChildren("thread")?
            .FindGameObjectInChildren("zap thread");

        if (zapThread) zapThread.SetActive(volt);

        var needle = parent.FindGameObjectInChildren("needle");

        var zapNeedle = needle?.FindGameObjectInChildren("Zap Effect Activator");
        if (zapNeedle) {
            zapNeedle.SetActive(volt);
            zapNeedle.SetActiveChildren(volt);
        }

        // Set shaman settings
        var isShaman = crestType == CrestType.Shaman;

        var shamanParent = parent.FindGameObjectInChildren("Rune Effect Activator");
        if (shamanParent) {
            shamanParent.SetActive(isShaman);

            var shamanRune = shamanParent.FindGameObjectInChildren("Shaman Rune");
            if (shamanRune) {
                shamanRune.SetActive(isShaman);

                var zapRune = shamanRune.FindGameObjectInChildren("Zap Rune");
                if (zapRune) zapRune.SetActive(volt);
            }
        }

        // Set damager
        var damager = needle?.FindGameObjectInChildren("Needle Damage");
        if (damager) {
            SetDamageHeroState(damager, 1);
        } else {
            Logger.Warn("Unable to set damager for Silk Spear");
        }

        // Enable spear
        spear.SetActive(false);
        spear.SetActive(true);

        // Play audio
        PlayHornetAttackSound(playerObject);
        var fsm = GetSkillFSM();

        var throwAudio = fsm.GetAction<PlayAudioEvent>("Start Throw", 1);
        if (throwAudio != null) AudioUtil.PlayAudio(throwAudio, playerObject);

        if (volt) {
            var voltAudio = fsm.GetAction<PlayAudioEvent>("Silkspear Zap FX", 1);
            if (voltAudio != null) AudioUtil.PlayAudio(voltAudio, playerObject);
        }
    }

    private static GameObject? GetSilkSpear(GameObject playerObject) {
        // Find existing silk spear
        var silkAttacks = GetPlayerSilkAttacks(playerObject);
        var spear = silkAttacks.FindGameObjectInChildren(SpearObjectName);
        if (spear) return spear;

        // Find on own silk attacks
        if (!TryGetLocalSilkAttacks(out var localSilkAttacks)) {
            return null;
        }

        var localSpear = localSilkAttacks.FindGameObjectInChildren(SpearObjectName);
        if (localSpear == null) {
            return null;
        }

        // Create new spear
        spear = Object.Instantiate(localSpear, silkAttacks.transform);
        spear.name = SpearObjectName;

        spear.DestroyComponent<ToolEquipChecker>();
        spear.DestroyComponentsInChildren<HeroShamanRuneEffect>();
        spear.DestroyComponentsInChildren<FollowCamera>();

        var child = spear.FindGameObjectInChildren("needle_throw_simple");
        if (child) {
            child.DestroyComponent<CameraControlAnimationEvents>();
            child.DestroyComponent<CaptureAnimationEvent>();

            var bloom1 = child
                .FindGameObjectInChildren("Rune Effect Activator")?
                .FindGameObjectInChildren("Shaman Rune")?
                .FindGameObjectInChildren("Rune Effect")?
                .FindGameObjectInChildren("Shaman Rune")?
                .FindGameObjectInChildren("Shaman Rune Camera Bloom");

            var bloom2 = child
                .FindGameObjectInChildren("Rune Effect Activator")?
                .FindGameObjectInChildren("Shaman Rune")?
                .FindGameObjectInChildren("Zap Rune")?
                .FindGameObjectInChildren("Rune Effect")?
                .FindGameObjectInChildren("Shaman Rune")?
                .FindGameObjectInChildren("Shaman Rune Camera Bloom");

            if (bloom1) {
                Object.Destroy(bloom1);
            }
        }

        return spear;
    }
}

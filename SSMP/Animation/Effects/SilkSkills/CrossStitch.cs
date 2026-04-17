using System.Collections;
using System.Diagnostics.CodeAnalysis;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

internal class CrossStitch : BaseSilkSkill {
    private const string ParryStanceFlashName = "Parry Stance Flash";
    private const string ParryClashName = "Parry Clash Effect";
    private const string ParrySlashName = "Parry Slash Effect";
    private const string ParryZapSlashName = "Parry Slash Effect Zap";

    public bool IsStarting = false;

    public static CrossStitch StartingInstance = new() { IsStarting = true };

    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var isShaman = crestType == CrestType.Shaman;
        var isVolt = IsVolt(effectInfo);

        // Determine which animation to play
        if (IsStarting) {
            PlayStart(playerObject, isShaman, isVolt);
            return;
        }

        // Play the clash, then play the dash
        MonoBehaviourUtil.Instance.StartCoroutine(PlayClash(playerObject, isShaman, isVolt));
    }

    private void PlayStart(GameObject playerObject, bool isShaman, bool isVolt) {
        var fsm = GetSkillFSM();

        // Play normal hornet noise
        var normalAudio = fsm.GetFirstAction<PlayRandomAudioClipTableV3>("Parry Start");
        if (normalAudio != null) AudioUtil.PlayAudio(normalAudio, playerObject);

        // Play stance audio
        var stanceAudio = fsm.GetAction<AudioPlayerOneShotSingle>("Parry Start", 15);
        if (stanceAudio != null) AudioUtil.PlayAudio(stanceAudio, playerObject);

        // Enable thread (or zap thread)
        if (TryGetParryThread(playerObject, isVolt, out var thread)) {
            thread.SetActive(false);
            thread.SetActive(true);
        }

        // Enable stance flash
        if (TryGetStanceFlash(playerObject, out var flash)) {
            flash.SetActiveChildren(isShaman);

            flash.SetActive(false);
            flash.SetActive(true);
        }
    }

    private IEnumerator PlayClash(GameObject playerObject, bool isShaman, bool isVolt) {
        // Play parry audio
        var fsm = GetSkillFSM();
        var clashAudio = fsm.GetFirstAction<AudioPlayerOneShotSingle>("Parry Clash");
        if (clashAudio != null) AudioUtil.PlayAudio(clashAudio, playerObject);

        // Activate clash object
        if (TryGetClashEffect(playerObject, out var clash)) {
            clash.SetActiveChildren(isShaman);
            
            clash.SetActive(false);
            clash.SetActive(true);
        }

        yield return new WaitForSeconds(0.5f);

        // Play dash effect
        MonoBehaviourUtil.Instance.StartCoroutine(PlayDash(playerObject, isShaman, isVolt));
    }

    private IEnumerator PlayDash(GameObject playerObject, bool isShaman, bool isVolt) {
        // Play louder sound
        PlayHornetAttackSound(playerObject);

        // Hide the player during animation
        HidePlayer(playerObject);

        // Get appropriate effect object from FSM (Parry Cross Slash)
        if (TryGetSlashEffect(playerObject, isVolt, out var slash)) {
            slash.SetActive(false);
            slash.SetActive(true);

            var runes = slash.FindGameObjectInChildren("Runes");

            if (runes != null) {
                runes.SetActive(isShaman);
            }

            var damager = slash.FindGameObjectInChildren("Enemy_Damager");
            if (damager != null) {
                SetDamageHeroState(damager);
            } else {
                Logger.Warn("Unable to set damager for Cross Stitch");
            }
        } else {
            Logger.Warn("Unable to set damager for Cross Stitch");
        }

        // Wait for animation to finish
        yield return new WaitForSeconds(0.75f);

        // Deactivate effect object
        if (slash) {
            slash.SetActive(false);
        }
    }

    private static bool TryGetParryThread(GameObject playerObject, bool zap, [MaybeNullWhen(false)] out GameObject thread) {
        // Find existing object
        var name = zap ? "Parry Thread Zap" : "Parry Thread";
        var created = FindOrCreateAttack(playerObject, name, out var threadObj);
        if (threadObj == null) {
            thread = null;
            return false;
        }

        thread = threadObj;
        if (!created) return true;

        if (zap) {
            var bloom = thread.FindGameObjectInChildren("light_effect_v02 (2)");
            if (bloom) {
                Object.Destroy(bloom);
            }

            thread.SetActiveChildren(true);
            thread.SetActive(false);
        }

        return true;
    }

    private static bool TryGetStanceFlash(GameObject playerObject, [MaybeNullWhen(false)] out GameObject flash) {
        var created = FindOrCreateAttack(playerObject, ParryStanceFlashName, out var flashObj);
        if (flashObj == null) {
            flash = null;
            return false;
        }

        flash = flashObj;
        if (!created) return true;

        flash.DestroyComponentsInChildren<HeroShamanRuneEffect>();
        flash.DestroyGameObjectInChildren("Shaman Flash Glow");

        return true;
    }

    private static bool TryGetClashEffect(GameObject playerObject, [MaybeNullWhen(false)] out GameObject clash) {
        var created = FindOrCreateAttack(playerObject, ParryClashName, out var clashObj);
        if (clashObj == null) {
            clash = null;
            return false;
        }

        clash = clashObj;
        if (!created) return true;

        clash.DestroyComponentsInChildren<HeroShamanRuneEffect>();

        return true;
    }

    private static bool TryGetSlashEffect(GameObject playerObject, bool isZap, [MaybeNullWhen(false)] out GameObject slash) {
        var name = isZap ? ParryZapSlashName : ParrySlashName;

        var attacks = GetPlayerSilkAttacks(playerObject);
        slash = attacks.FindGameObjectInChildren(name);

        if (slash) {
            return true;
        }

        var fsm = GetSkillFSM();
        var boolTest = fsm.GetAction<BoolTestToGameObject>("Parry Cross Slash", 8);
        if (boolTest == null) {
            return false;
        }

        GameObject localSlashObj;
        if (isZap) {
            localSlashObj = boolTest.TrueGameObject.Value;
        } else {
            localSlashObj = boolTest.FalseGameObject.Value;
        }

        slash = Object.Instantiate(localSlashObj, attacks.transform);
        slash.name = name;
        slash.transform.localPosition = new Vector3(1, 1, 0);

        slash.DestroyComponentsInChildren<HeroShamanRuneEffect>();
        slash.DestroyGameObjectInChildren("haze2");

        var runeFlash = slash
            .FindGameObjectInChildren("Runes")?
            .FindGameObjectInChildren("Runes Flash");

        if (runeFlash != null) {
            Object.Destroy(runeFlash);
        }
        
        // Enable damage collider
        var damager = slash.FindGameObjectInChildren("Enemy_Damager");
        if (damager != null && damager.TryGetComponent<PolygonCollider2D>(out var collider)) {
            collider.enabled = true;
        }

        return true;
    }
}

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

    /// <summary>
    /// Determines if this instance is for the starting animation
    /// </summary>
    public bool IsStarting = false;

    /// <summary>
    /// Reference to an instance for the starting animation
    /// </summary>
    public static CrossStitch StartingInstance = new() { IsStarting = true };

    /// <inheritdoc/>
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

    /// <summary>
    /// Plays the parry preparation animation
    /// </summary>
    /// <param name="playerObject">The player who used the skill</param>
    /// <param name="isShaman">If shaman effects should be used</param>
    /// <param name="isVolt">If volt filament effects should be used</param>
    private void PlayStart(GameObject playerObject, bool isShaman, bool isVolt) {
        var fsm = GetSkillFSM();

        // Play normal hornet noise
        var normalAudio = fsm.GetFirstAction<PlayRandomAudioClipTableV3>("Parry Start");
        if (normalAudio != null) AudioUtil.PlayAudio(normalAudio, playerObject);

        // Play stance audio
        var stanceAudio = fsm.GetAction<AudioPlayerOneShotSingle>("Parry Start", 15);
        if (stanceAudio != null) AudioUtil.PlayAudio(stanceAudio, playerObject);

        // Enable thread (or volt thread)
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

    /// <summary>
    /// Plays the post-hit clash effect, then the dash.
    /// </summary>
    /// <param name="playerObject">The player who used the skill</param>
    /// <param name="isShaman">If shaman effects should be used</param>
    /// <param name="isVolt">If volt filament effects should be used</param>
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

    /// <summary>
    /// Plays the cross stitch dashing animation that does damage (if appropriate)
    /// </summary>
    /// <param name="playerObject">The player who used the skill</param>
    /// <param name="isShaman">If shaman effects should be used</param>
    /// <param name="isVolt">If volt filament effects should be used</param>
    private IEnumerator PlayDash(GameObject playerObject, bool isShaman, bool isVolt) {
        // Play louder sound
        PlayHornetAttackSound(playerObject);

        // Hide the player during animation
        HidePlayer(playerObject);

        // Get appropriate effect object
        if (TryGetSlashEffect(playerObject, isVolt, out var slash)) {
            slash.SetActive(false);
            slash.SetActive(true);

            // Enable shaman effect if appropriate
            var runes = slash.FindGameObjectInChildren("Runes");
            runes?.SetActive(isShaman);

            // Add damager
            var damager = slash.FindGameObjectInChildren("Enemy_Damager");
            if (damager != null) {
                SetDamageHeroStateCalculated(damager, ServerSettings.CrossStitchDamage, isVolt, isShaman);
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

    /// <summary>
    /// Attempts to get the parry thread effect for the preparation animation
    /// </summary>
    /// <param name="playerObject">The player using the skill</param>
    /// <param name="isVolt">If volt filament effects should be used</param>
    /// <param name="thread">The effect, if found</param>
    /// <returns>true if found</returns>
    private static bool TryGetParryThread(GameObject playerObject, bool isVolt, [MaybeNullWhen(false)] out GameObject thread) {
        // Find existing object
        var name = isVolt ? "Parry Thread Zap" : "Parry Thread";
        var created = FindOrCreateSkill(playerObject, name, out var threadObj);
        if (threadObj == null) {
            thread = null;
            return false;
        }

        thread = threadObj;
        if (!created) return true;

        // Remove existing components if applicable
        if (isVolt) {
            thread.DestroyGameObjectInChildren("light_effect_v02 (2)");

            thread.SetActiveChildren(true);
            thread.SetActive(false);
        }

        return true;
    }

    /// <summary>
    /// Attempts to get the flash effect for the preparation animation
    /// </summary>
    /// <param name="playerObject">The player using the skill</param>
    /// <param name="flash">The effect, if found</param>
    /// <returns>true if found</returns>
    private static bool TryGetStanceFlash(GameObject playerObject, [MaybeNullWhen(false)] out GameObject flash) {
        // Find or create
        var created = FindOrCreateSkill(playerObject, "Parry Stance Flash", out var flashObj);
        if (flashObj == null) {
            flash = null;
            return false;
        }

        flash = flashObj;
        if (!created) return true;

        // Destroy components/children if created
        flash.DestroyComponentsInChildren<HeroShamanRuneEffect>();
        flash.DestroyGameObjectInChildren("Shaman Flash Glow");

        return true;
    }

    /// <summary>
    /// Attempts to get the parry activation clash effect
    /// </summary>
    /// <param name="playerObject">The player using the skill</param>
    /// <param name="clash">The effect, if found</param>
    /// <returns>true if found</returns>
    private static bool TryGetClashEffect(GameObject playerObject, [MaybeNullWhen(false)] out GameObject clash) {
        // Find or create
        var created = FindOrCreateSkill(playerObject, "Parry Clash Effect", out var clashObj);
        if (clashObj == null) {
            clash = null;
            return false;
        }

        clash = clashObj;
        if (!created) return true;

        // Remove components if created
        clash.DestroyComponentsInChildren<HeroShamanRuneEffect>();

        return true;
    }

    /// <summary>
    /// Attempts to get the parry activation slash dash effect
    /// </summary>
    /// <param name="playerObject">The player using the skill</param>
    /// <param name="isVolt">If volt filament effects should be used</param>
    /// <param name="slash">The effect, if found</param>
    /// <returns>true if found</returns>
    private static bool TryGetSlashEffect(GameObject playerObject, bool isVolt, [MaybeNullWhen(false)] out GameObject slash) {
        var name = isVolt ? "Parry Slash Effect Zap" : "Parry Slash Effect";

        // Find existing slash
        var attacks = GetPlayerSilkSkills(playerObject);
        slash = attacks.FindGameObjectInChildren(name);

        if (slash) {
            return true;
        }

        // Get the correct slash
        var fsm = GetSkillFSM();
        var boolTest = fsm.GetAction<BoolTestToGameObject>("Parry Cross Slash", 8);
        if (boolTest == null) {
            return false;
        }

        GameObject localSlashObj;
        if (isVolt) {
            localSlashObj = boolTest.TrueGameObject.Value;
        } else {
            localSlashObj = boolTest.FalseGameObject.Value;
        }

        // Create an instance of it, then prepare
        slash = Object.Instantiate(localSlashObj, attacks.transform);
        slash.name = name;
        slash.transform.localPosition = new Vector3(1, 1, 0);

        slash.DestroyComponentsInChildren<HeroShamanRuneEffect>();
        slash.DestroyGameObjectInChildren("haze2");

        slash.FindGameObjectInChildren("Runes")?
            .DestroyGameObjectInChildren("Runes Flash");
        
        // Enable damage collider
        var damager = slash.FindGameObjectInChildren("Enemy_Damager");
        if (damager != null && damager.TryGetComponent<PolygonCollider2D>(out var collider)) {
            collider.enabled = true;
        }

        return true;
    }
}

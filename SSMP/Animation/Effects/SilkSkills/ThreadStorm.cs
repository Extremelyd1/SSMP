using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

internal class ThreadStorm : BaseSilkSkill {

    /// <summary>
    /// A reference for players currently extending their thread storms.
    /// Used to prevent the effect from disappearing early.
    /// </summary>
    private static readonly Dictionary<int, int> PlayerExtensions = [];

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var volt = IsVolt(effectInfo);
        var isShaman = crestType == CrestType.Shaman;

        // Update number of extensions
        var playerId = playerObject.GetInstanceID();
        var extensions = PlayerExtensions.GetValueOrDefault(playerId, 0);
        PlayerExtensions[playerId] = extensions + 1;

        // Play extension if applicable
        if (extensions > 0) {
            MonoBehaviourUtil.Instance.StartCoroutine(PlayStormExtension(playerObject));
            return;
        }

        // Otherwise play the setup animation
        MonoBehaviourUtil.Instance.StartCoroutine(PlayStormSetup(playerObject, volt, isShaman));
    }

    /// <summary>
    /// Plays the main loop of the Thread Storm attack
    /// </summary>
    /// <param name="playerObject">The player object that used the attack</param>
    /// <param name="initial">If the extension is part of the original animation</param>
    private IEnumerator PlayStormExtension(GameObject playerObject, bool initial = false) {
        if (!TryGetThreadStorm(playerObject, out var threadStorm)) {
            yield break;
        }

        // Restart animation
        var animator = threadStorm.GetComponentInChildren<tk2dSpriteAnimator>();
        if (animator == null) {
            ForceStop(threadStorm);
            yield break;
        }

        animator.PlayFromFrame("AirSphere", 0);

        // Scale up
        var damager = threadStorm.FindGameObjectInChildren("Ball");
        if (!initial && damager != null) {
            damager.transform.localScale = new Vector3(1.9f, 1.9f, 1);
            AnimateScaleReset(damager);
        }

        // Play audio
        var fsm = GetSkillFSM();
        var extendAudio = fsm.GetFirstAction<AudioPlaySimple>("Extend");
        if (extendAudio.oneShotClip.Value is AudioClip clip) {
            AudioUtil.PlayAudio(clip, playerObject);
        }

        yield return new WaitForSeconds(0.65f);

        AttemptStop(playerObject, threadStorm);
    }

    /// <summary>
    /// Initializes and activates the Thread Storm attack, setting up sub-effects
    /// </summary>
    /// <param name="playerObject">The player object that used the attack.</param>
    /// <param name="isVolt">Determines if the volt filament effect should be enabled.</param>
    /// <param name="isShaman">Determines if the shaman crest effect should be displayed.</param>
    private IEnumerator PlayStormSetup(GameObject playerObject, bool isVolt, bool isShaman) {
        if (!TryGetThreadStorm(playerObject, out var threadStorm)) {
            yield break;
        }

        threadStorm.SetActive(true);
        threadStorm.transform.localScale = Vector3.one;

        // Set volt filament effect
        var damager = threadStorm.FindGameObjectInChildren("Ball");
        var voltObject = damager?.FindGameObjectInChildren("thread_sphere_effect_zap");

        if (voltObject) {
            voltObject.SetActive(false);
            voltObject.SetActive(isVolt);
        }

        // Enable shaman crest effect
        var shamanRune = threadStorm.FindGameObjectInChildren("Shaman Rune");
        if (shamanRune) {
            shamanRune.SetActive(isShaman);
        }

        // Play antic noise
        PlayHornetAttackSound(playerObject);

        // Set the damager
        if (damager) {
            damager.transform.localScale = new Vector3(0.8f, 0.8f, 1);
            AnimateScaleReset(damager);

            SetDamageHeroState(damager, 1);
            damager.SetActive(true);
        } else {
            Logger.Warn("Unable to set damager for Thread Storm");
        }

        // Play looping silk audio
        // Play the main effect
        MonoBehaviourUtil.Instance.StartCoroutine(PlayStormExtension(playerObject, true));
    }

    /// <summary>
    /// Animates the thread storm scale back to default
    /// </summary>
    /// <param name="ball">The "ball" child on the thread storm object</param>
    private static void AnimateScaleReset(GameObject ball) {
        ball.transform.ScaleTo(MonoBehaviourUtil.Instance, new Vector3(1.7f, 1.7f, 1), 0.1f);
    }

    /// <summary>
    /// Stops the effect if no more extensions have been received
    /// </summary>
    /// <param name="playerObject">The player object that used the attack</param>
    /// <param name="threadStorm">The thread storm effect object</param>
    private static void AttemptStop(GameObject playerObject, GameObject threadStorm) {
        // Decrement extension count
        var playerId = playerObject.GetInstanceID();
        var extensions = PlayerExtensions.GetValueOrDefault(playerId, 1);
        PlayerExtensions[playerId] = Mathf.Max(0, extensions - 1);

        // There are more extensions, don't deactivate yet
        if (extensions > 1) {
            return;
        }

        // Stop the effect
        ForceStop(threadStorm);

        // Play ending audio
        var fsm = GetSkillFSM();
        var endAudio = fsm.GetFirstAction<PlayAudioEvent>("A Sphere End");
        AudioUtil.PlayAudio(endAudio, playerObject);
    }

    /// <summary>
    /// Forces the thread storm to stop
    /// </summary>
    /// <param name="threadStorm">The thread storm effect's object</param>
    private static void ForceStop(GameObject threadStorm) {
        var audio = threadStorm.GetComponent<AudioSource>();
        audio.Stop();
        threadStorm.SetActive(false);
    }

    /// <summary>
    /// Gets or creates the Thread Storm effect
    /// </summary>
    /// <param name="playerObject">The object of the player that used the attack</param>
    /// <param name="threadStorm">The found or created Thread Storm effect object</param>
    /// <returns>false if threadStorm could not be created</returns>
    private static bool TryGetThreadStorm(
        GameObject playerObject,
        [MaybeNullWhen(false)] out GameObject threadStorm
    ) {
        // Find or create effect
        var created = FindOrCreateSkill(playerObject, "Sphere Ball", out threadStorm);
        if (!threadStorm) {
            return false;
        }

        if (created) {
            return true;
        }

        // Remove components that could interfere
        threadStorm.DestroyComponent<PlayMakerFSM>();
        threadStorm.DestroyComponent<HeroShamanRuneEffect>();
        threadStorm.DestroyComponent<ToolEquipChecker>();
        threadStorm.DestroyComponent<EventRegister>();

        // Play looping silk audio
        if (threadStorm.TryGetComponent<AudioSource>(out var audio)) {
            audio.playOnAwake = true;
        }

        // Set up shaman crest effects
        var shamanRune = threadStorm.FindGameObjectInChildren("Shaman Rune");
        if (shamanRune) {
            if (shamanRune.TryGetComponent<HeroShamanRuneEffect>(out var runeEffect)) {
                // Copy particles
                var preParticles = runeEffect.runeSpawnEffect;
                if (preParticles != null) {
                    var postParticles = EffectUtils.SpawnGlobalPoolObject(preParticles, shamanRune.transform, 0, true);
                    if (postParticles) {
                        postParticles.transform.localScale = new Vector3(3.5f, 3.5f, 1);
                    }
                }

                // Remove shaman manager
                Object.DestroyImmediate(runeEffect);
            }

            shamanRune.DestroyGameObjectInChildren("Shaman Rune Camera Bloom");
        }

        threadStorm.SetActive(false);
        threadStorm.SetActive(true);

        return true;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using GlobalSettings;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Org.BouncyCastle.Bcpg;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

internal class ThreadStorm : BaseSilkSkill {
    private const string SkillObjectName = "Sphere Ball";

    private static GameObject? _localThreadStorm;

    private static Dictionary<int, int> _playerExtensions = new();

    public static byte[] GetEffectFlags() {
        var voltFilament = ToolItemManager.GetToolByName("Zap Imbuement");

        return new byte[] {
            (byte)(voltFilament.IsEquipped ? 1 : 0)
        };
    }

    public override byte[]? GetEffectInfo() {
        return GetEffectFlags();
    }

    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var volt = effectInfo is [1];
        var isShaman = crestType == CrestType.Shaman;

        // Update number of extensions
        var playerId = playerObject.GetInstanceID();
        var extensions = _playerExtensions.GetValueOrDefault(playerId, 0);
        _playerExtensions[playerId] = extensions + 1;

        // Play extension if applicable
        if (extensions > 0) {
            Logger.Info("Playing extension");
            MonoBehaviourUtil.Instance.StartCoroutine(PlayStormExtension(playerObject));
            return;
        }

        // Otherwise play the setup animation
        MonoBehaviourUtil.Instance.StartCoroutine(PlayStormSetup(playerObject, volt, isShaman));
    }

    private IEnumerator PlayStormExtension(GameObject playerObject) {
        if (!TryGetThreadStorm(playerObject, out var threadStorm)) {
            yield break;
        }

        // The animation is kinda broken as it replays for each extension. Keep this unless we use a dedicated packet
        var playerAnimator = playerObject.GetComponent<tk2dSpriteAnimator>();
        if (playerAnimator) {
            playerAnimator.Stop();
        }

        // Restart animation
        var animator = threadStorm.GetComponentInChildren<tk2dSpriteAnimator>();
        if (animator == null) {
            ForceStop(threadStorm);
            yield break;
        }

        //animator.PlayFromFrame("AirSphere", 0);

        // Scale up
        var curveScale = threadStorm.GetComponent<CurveScaleAnimation>();
        if (curveScale != null) {
            curveScale.enabled = false;
            curveScale.enabled = true;
            curveScale.StartAnimation();
        }

        yield return new WaitForSeconds(0.65f);

        AttemptStop(playerObject, threadStorm);
    }

    private IEnumerator PlayStormSetup(GameObject playerObject, bool volt, bool isShaman) {
        if (!TryGetThreadStorm(playerObject, out var threadStorm)) {
            yield break;
        }

        threadStorm.SetActive(true);

        // Set volt filament effect
        var voltObject = threadStorm
            .FindGameObjectInChildren("Ball")?
            .FindGameObjectInChildren("thread_sphere_effect_zap");

        if (voltObject) {
            voltObject.SetActive(false);
            voltObject.SetActive(volt);
        }

        // Enable shaman crest effect
        var shamanRune = threadStorm.FindGameObjectInChildren("Shaman Rune");
        if (shamanRune) {
            shamanRune.SetActive(isShaman);
        }

        // Play antic noise
        var fsm = GetSkillFSM();
        var anticAudio = fsm.GetAction<PlayRandomAudioClipTable>("A Sphere Antic", 2);
        if (anticAudio != null) {
            AudioUtil.PlayAudio(anticAudio, playerObject);
        }

        // Set the damager
        var damager = threadStorm.FindGameObjectInChildren("Ball");
        if (damager) {
            SetDamageHeroState(damager, 1);
            damager.SetActive(true);
        } else {
            Logger.Warn("Unable to set damager for Thread Storm");
        }

        // Play silk audio
        var audio = threadStorm.GetComponent<AudioSource>();
        audio.Play();

        // Play the effect
        MonoBehaviourUtil.Instance.StartCoroutine(PlayStormExtension(playerObject));
    }

    private static void AttemptStop(GameObject playerObject, GameObject threadStorm) {
        var playerId = playerObject.GetInstanceID();
        var extensions = _playerExtensions.GetValueOrDefault(playerId, 1);
        _playerExtensions[playerId] = Mathf.Max(0, extensions - 1);

        if (extensions > 1) {
            return;
        }

        ForceStop(threadStorm);

        // Play ending audio
        var fsm = GetSkillFSM();
        var endAudio = fsm.GetFirstAction<PlayAudioEvent>("A Sphere End");
        AudioUtil.PlayAudio(endAudio, playerObject);
    }

    private static void ForceStop(GameObject threadStorm) {
        var audio = threadStorm.GetComponent<AudioSource>();
        audio.Stop();
        threadStorm.SetActive(false);

    }

    private static bool TryGetThreadStorm(
        GameObject playerObject,
        [MaybeNullWhen(false)] out GameObject threadStorm
    ) {
        // Find existing thread storm
        var parent = TryGetPlayerSilkAttacks(playerObject);
        threadStorm = parent.FindGameObjectInChildren(SkillObjectName);
        if (threadStorm) {
            return true;
        }

        // Not found, locate it on the player
        var localStorm = _localThreadStorm;
        if (localStorm == null) {
            // Get local silk attacks
            if (!TryGetLocalSilkAttacks(out var localSilkAttacks)) {
                return false;
            }

            // Find the thread storm
            localStorm = localSilkAttacks.FindGameObjectInChildren(SkillObjectName);
            if (localStorm == null) {
                Logger.Warn("Unable to get local Thread Storm object");
                return false;
            }

            _localThreadStorm = localStorm;
        }
        
        // Copy to the player object
        threadStorm = Object.Instantiate(localStorm, parent.transform);
        threadStorm.name = SkillObjectName;

        // Remove FSM
        if (threadStorm.TryGetComponent<PlayMakerFSM>(out var fsm)) {
            Object.Destroy(fsm);
        }

        // Remove shaman manager
        if (threadStorm.TryGetComponent<HeroShamanRuneEffect>(out var globalRuneEffect)) {
            Object.DestroyImmediate(globalRuneEffect);
        }

        if (threadStorm.TryGetComponent<ToolEquipChecker>(out var checker)) {
            Object.DestroyImmediate(checker);
        }

        if (threadStorm.TryGetComponent<EventRegister>(out var register)) {
            Object.DestroyImmediate(register);
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
                        postParticles.transform.localPosition = Vector3.zero;
                        postParticles.transform.localScale = new Vector3(3.5f, 3.5f, 1);
                    }
                }

                // Remove shaman manager
                Object.DestroyImmediate(runeEffect);
            }

            var bloom = shamanRune.FindGameObjectInChildren("Shaman Rune Camera Bloom");
            if (bloom) Object.DestroyImmediate(bloom);
        }

        // Set up scale animation. It plays when enabled.
        var curveScale = threadStorm.AddComponent<CurveScaleAnimation>();
        curveScale.duration = 0.3f;
        curveScale.playOnEnable = false;
        curveScale.curve = new([
            new Keyframe(0, 1),
            new Keyframe(0.5f, 2f),
            new Keyframe(1, 1),
        ]);
        curveScale.OnStart = new UnityEngine.Events.UnityEvent();
        curveScale.OnStop = new UnityEngine.Events.UnityEvent();
        curveScale.framerate = 30;
        curveScale.isRealtime = true;
        curveScale.playOnEnable = true;
        curveScale.enabled = false;
        curveScale.offset = new Vector3(0.1f, 0.1f, 0.1f);
        

        threadStorm.SetActive(true);

        return true;
    }
}

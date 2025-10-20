using System;
using System.Collections;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects;

/// <summary>
/// Class for the animation effect of bind (healing).
/// </summary>
internal class Bind : AnimationEffect {
    /// <summary>
    /// Cached FSM for Hornet's bind ability.
    /// </summary>
    private static PlayMakerFSM? _bindFsm;

    private static GameObject? _localBindEffects;
    
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        MonoBehaviourUtil.Instance.StartCoroutine(PlayBindEffect(playerObject, crestType));
    }

    private IEnumerator PlayBindEffect(GameObject playerObject, CrestType crestType) {
        var randomClipAction = GetOrFindBindFsm().GetFirstAction<GetRandomAudioClipFromTable>("Bind Start");
        var playAudioAction = GetOrFindBindFsm().GetFirstAction<PlayAudioEvent>("Bind Start");
        
        AudioUtil.PlayAudioEventWithRandomAudioClipFromTableAtPlayerObject(
            playAudioAction,
            randomClipAction,
            playerObject
        );

        var oneShotSingleAction = GetOrFindBindFsm().GetFirstAction<AudioPlayerOneShotSingle>("Check Grounded");
        AudioUtil.PlayAudioOneShotSingleAtPlayerObject(oneShotSingleAction, playerObject);

        _localBindEffects ??= HeroController.instance.gameObject.FindGameObjectInChildren("Bind Effects");
        if (_localBindEffects == null) {
            Logger.Warn("Could not find local Bind Effects object in hero object");
            yield break;
        }

        var bindEffects = playerObject.FindGameObjectInChildren("Bind Effects");
        if (bindEffects == null) {
            Logger.Warn("Player object does not have Bind Effects child, cannot play bind");
            yield break;
        }

        var bindSilkObj = bindEffects.FindGameObjectInChildren("Bind Silk");
        if (bindSilkObj == null) {
            var localBindSilkObj = _localBindEffects.FindGameObjectInChildren("Bind Silk");
            if (localBindSilkObj == null) {
                Logger.Warn("Could not find local Bind Silk object, cannot play bind");
                yield break;
            }

            bindSilkObj = Object.Instantiate(localBindSilkObj, bindEffects.transform);
        }

        var bindSilkMeshRenderer = bindSilkObj.GetComponent<MeshRenderer>();
        bindSilkMeshRenderer.enabled = true;

        var bindSilkAnimator = bindSilkObj.GetComponent<tk2dSpriteAnimator>();
        bindSilkAnimator.Play(bindSilkAnimator.GetClipByName("Bind Silk"));
        
        // TODO: if Beast is equipped, activate rage burst object (State: Rage Burst?)
        // TODO: if claw mirrors are equipped, do effects in state "Dazzle?"
        // TODO: if maggoted, do effects in state "Maggoted?"

        var playerAnimator = playerObject.GetComponent<tk2dSpriteAnimator>();
        var currentClip = playerAnimator.currentClip;
        Logger.Error($"Player Animator current clip for Bind: {currentClip.name}");
        // TODO: figure out when animation triggers happen

        // TODO: adjust bind time based on crest and tools
        var bindTime = 1.2f;

        yield return new WaitForSeconds(bindTime);

        bindSilkMeshRenderer.enabled = false;
        
        // TODO: activate object in state "Heal", last action
        
        
    }

    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        // Warding bell
        // Claw mirrors
        return null;
    }
    
    /// <summary>
    /// Get or find the Bind FSM on the hero object. Will be cached to <see cref="_bindFsm"/>.
    /// </summary>
    /// <returns>The FSM for Bind.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the FSM cannot be found, which shouldn't happen.
    /// </exception>
    private PlayMakerFSM GetOrFindBindFsm() {
        if (_bindFsm != null) {
            return _bindFsm;
        }

        var heroFsms = HeroController.instance.GetComponents<PlayMakerFSM>();
        foreach (var heroFsm in heroFsms) {
            if (heroFsm.FsmName == "Bind") {
                _bindFsm = heroFsm;
                return _bindFsm;
            }
        }

        throw new InvalidOperationException("Could not find Bind FSM on hero");
    }
}

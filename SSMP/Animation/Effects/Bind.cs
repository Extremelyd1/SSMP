using System;
using System.Collections;
using System.Linq;
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
internal class Bind : DamageAnimationEffect {

    protected const string BIND_BELL_NAME = "bind_bell_appear_instance";

    public bool ShamanDoneFalling = false;

    protected class Flags {
        public bool BindBell = false;
        public bool BaseMirror = false;
        public bool UpgradedMirror = false;
        public bool QuickBind = false;
        public bool ReserveBind = false;
        public bool Maggoted = false;

        public Flags(byte[]? info) {
            if (info == null) return;
            BindBell = info[0] == 1;
            BaseMirror = info[1] == 1;
            UpgradedMirror = info[2] == 1;
            QuickBind = info[3] == 1;
            ReserveBind = info[4] == 1;
            Maggoted = info[5] == 1;
        }
    }

    /// <summary>
    /// Cached FSM for Hornet's bind ability.
    /// </summary>
    protected static PlayMakerFSM? _bindFsm;

    protected static GameObject? _localBindEffects;

    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        Flags flags = new Flags(effectInfo);
        MonoBehaviourUtil.Instance.StartCoroutine(PlayBindEffect(playerObject, crestType, flags));
    }

    private IEnumerator PlayBindEffect(GameObject playerObject, CrestType crestType, Flags flags) {
        var randomClipAction = GetOrFindBindFsm().GetFirstAction<GetRandomAudioClipFromTable>("Bind Start");
        var playAudioAction = GetOrFindBindFsm().GetFirstAction<PlayAudioEvent>("Bind Start");

        if (!ShamanDoneFalling) {
            AudioUtil.PlayAudioEventWithRandomAudioClipFromTableAtPlayerObject(
                playAudioAction,
                randomClipAction,
                playerObject
            );
        }

        var oneShotSingleAction = GetOrFindBindFsm().GetFirstAction<AudioPlayerOneShotSingle>("Check Grounded");
        AudioUtil.PlayAudioOneShotSingleAtPlayerObject(oneShotSingleAction, playerObject);

        var created = CreateObjects(playerObject, out var bindEffects);
        if (!created) {
            yield break;
        }

        Logger.Info("Determining crest animation...");

        if (crestType == CrestType.Beast) {
            Logger.Info("Playing Beast Crest Animation");
            PlayBeastBindStart(bindEffects);
        } else if (crestType == CrestType.Cursed) {
            Logger.Info("Playing Cursed Crest Animation");
            PlayCursedFail(bindEffects);
            yield break;
        } else if (crestType == CrestType.Witch) {
            Logger.Info("Playing Witch Crest Animation");
            if (flags.Maggoted) {
                PlayWitchMaggoted(bindEffects);
                PlayMaggotCleanse(bindEffects, playerObject);
                yield break;
            } else {
                PlayWitchAnimationAntic(bindEffects);
                //PlayWitchAnimation(bindEffects);
            }

        } else if (crestType == CrestType.Shaman) {
            Logger.Info("Playing Shaman Crest Animation");
            if (!ShamanDoneFalling) {
                PlayShamanFall(bindEffects);
            } else {
                PlayShamanFallEnd(bindEffects);
                PlayNormalStart(bindEffects, flags);
                yield break;
            }

        } else {
            Logger.Info("Playing Default Animation");
            PlayNormalStart(bindEffects, flags);
        }

        // If bind bell, do effects in state "Bind Bell?" and "Bind Bell Disappear?"
        if (flags.BindBell) {
            StartBindBell(bindEffects);
        }

        // TODO: If using reserve bind, use reserve bind animation?

        // TODO: Quick Craft animations

        Logger.Info("Getting clip name for no reason...");
        var playerAnimator = playerObject.GetComponent<tk2dSpriteAnimator>();
        var currentClip = playerAnimator?.currentClip;
        Logger.Info($"Player Animator current clip for Bind: {currentClip?.name}");
    }

    /// <summary>
    /// Creates the bind bell
    /// </summary>
    private void StartBindBell(GameObject bindEffects) {
        var bindBell = bindEffects.FindGameObjectInChildren(BIND_BELL_NAME);
        
        if (bindBell == null) {
            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            var localBell = allObjects.FirstOrDefault(o => o.name == "bind_bell_appear");
            
            if (localBell == null) {
                Logger.Warn("Couldn't find bind bell object");
                return;
            }

            bindBell = GameObject.Instantiate(localBell, bindEffects.transform);
            bindBell.name = BIND_BELL_NAME;
            
            var follower = bindBell.GetComponent<FollowTransform>();
            follower.target = bindEffects.transform;
            follower.useHero = false;

            var delay = bindBell.AddComponentIfNotPresent<DeactivateAfterDelay>();
            delay.time = 5f;
        }

        bindBell.SetActive(false);
        bindBell.SetActive(true);
    }
    /// <summary>
    /// Plays the normal silk animation
    /// </summary>
    private void PlayNormalStart(GameObject bindEffects, Flags flags) {
        var bindSilkObj = CreateEffectIfNotExists(bindEffects, "Bind Silk");
        if (bindSilkObj == null) {
            return;
        }

        var bindSilkMeshRenderer = bindSilkObj.GetComponent<MeshRenderer>();
        bindSilkMeshRenderer.enabled = true;

        var bindSilkAnimator = bindSilkObj.GetComponent<tk2dSpriteAnimator>();

        Logger.Info("Playing Bind Silk animation");

        if (flags.QuickBind) bindSilkAnimator.Play(bindSilkAnimator.GetClipByName("Bind Silk Quick"));
        else bindSilkAnimator.Play(bindSilkAnimator.GetClipByName("Bind Silk"));

        bindSilkObj.SetActive(false);
        bindSilkObj.SetActive(true);
    }

    /// <summary>
    /// Plays the Beast Crest specific silk animation
    /// </summary>
    private void PlayBeastBindStart(GameObject bindEffects) {
        var beastAntic = CreateEffectIfNotExists(bindEffects, "Warrior_Bind_antic_silk");
        if (beastAntic == null) {
            return;
        }

        beastAntic.SetActive(false);
        beastAntic.SetActive(true);
    }

    private void PlayCursedFail(GameObject bindEffects) {
        var failAntic = bindEffects.FindGameObjectInChildren("cursed_bind_fail");

        tk2dSpriteAnimator animator;
        if (failAntic == null) {
            var effects = HeroController.instance.gameObject.FindGameObjectInChildren("Effects");
            if (effects == null) {
                Logger.Warn("Unable to find local effects object");
                return;
            }

            var localFailAntic = effects.FindGameObjectInChildren("Cursed Bind Hornet");
            if (localFailAntic == null) {
                Logger.Warn("Unable to find local cursed bind object");
                return;
            }

            failAntic = GameObject.Instantiate(localFailAntic, bindEffects.transform);
            failAntic.name = "cursed_bind_fail";
            animator = failAntic.GetComponent<tk2dSpriteAnimator>();
            animator.AnimationCompletedEvent += PlayNextCursedPart;
        } else {
            animator = failAntic.GetComponent<tk2dSpriteAnimator>();
        }

        failAntic.SetActive(false);
        failAntic.SetActive(true);

        animator.Play("Bind Cursed Start");
    }

    private void PlayNextCursedPart(tk2dSpriteAnimator animator, tk2dSpriteAnimationClip clip) {
        if (clip.name == "Bind Cursed Start") {
            animator.Play("Bind Cursed Mid");
        } else if (clip.name == "Bind Cursed Mid") {
            animator.Play("Bind Cursed End");
        }
    }

    private void PlayWitchMaggoted(GameObject bindEffects) {
        var maggotCleanse = CreateEffectIfNotExists(bindEffects, "Witch Bind Maggot Cleanse");
        if (maggotCleanse != null) {
            maggotCleanse.SetActive(false);
            maggotCleanse.SetActive(true);
        }
    }

    private void PlayWitchAnimationAntic(GameObject bindEffects) {
        var silkAntic = CreateEffectIfNotExists(bindEffects, "Whip_Bind_silk_antic");
        if (silkAntic == null) {
            return;
        }

        silkAntic.SetActive(false);
        silkAntic.SetActive(true);
    }

    /// <summary>
    /// Plays the maggot cleanse animation
    /// </summary>
    protected void PlayMaggotCleanse(GameObject bindEffects, GameObject playerObject) {
        Logger.Info("Playing Maggot Animation");
        var maggotBurst = GetOrFindBindFsm().GetAction<SpawnObjectFromGlobalPool>("Maggoted?", 2);
        var maggotFlash = GetOrFindBindFsm().GetAction<SpawnObjectFromGlobalPool>("Maggoted?", 4);
        var maggotAudio = GetOrFindBindFsm().GetFirstAction<PlayAudioEvent>("Maggoted?");

        if (maggotBurst != null) {
            maggotBurst.gameObject.Value.Spawn(bindEffects.transform, Vector3.zero);
        }
        if (maggotFlash != null) {
            maggotFlash.gameObject.Value.Spawn(bindEffects.transform, Vector3.zero);
        }
        if (maggotAudio != null) {
            AudioUtil.PlayAudioEventAtPlayerObject(
                maggotAudio,
                playerObject
            );
        }
    }

    /// <summary>
    /// Plays the Shaman Crest falling silk animation
    /// </summary>
    /// <param name="bindEffects"></param>
    private void PlayShamanFall(GameObject bindEffects) {
        var shamanAntic = CreateEffectIfNotExists(bindEffects, "Shaman_Bind_antic_silk");
        if (shamanAntic == null) {
            return;
        }

        var delay = shamanAntic.AddComponentIfNotPresent<DeactivateAfterDelay>();
        delay.time = 5;

        shamanAntic.SetActive(false);
        shamanAntic.SetActive(true);
    }

    /// <summary>
    /// Transitions from the falling silk animation to the normal silk animation
    /// </summary>
    private void PlayShamanFallEnd(GameObject bindEffects) {
        var shamanAntic = bindEffects.FindGameObjectInChildren("Shaman_Bind_antic_silk");
        if (shamanAntic == null) {
            return;
        }
        var animator = shamanAntic.GetComponent<Animator>();
        animator.Play("End");
        //var clip = animator.runtimeAnimatorController.animationClips[2];
        
        //shamanAntic.SetActive(false);
    }

    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        byte[] effectInfo = {
            (byte) (ToolItemManager.IsToolEquipped("Bell Bind") ? 1 : 0),
            (byte) (ToolItemManager.IsToolEquipped("Dazzle Bind") ? 1 : 0),
            (byte) (ToolItemManager.IsToolEquipped("Dazzle Bind Upgraded") ? 1 : 0),
            (byte) (ToolItemManager.IsToolEquipped("Quickbind") ? 1 : 0),
            (byte) (ToolItemManager.IsToolEquipped("Reserve Bind") ? 1 : 0),
            (byte) (HeroController.instance.cState.isMaggoted ? 1 : 0),
            //(byte) (HeroController.instance.onFlatGround ? 0 : 1)
        };

        return effectInfo;
    }

    /// <summary>
    /// Get or find the Bind FSM on the hero object. Will be cached to <see cref="_bindFsm"/>.
    /// </summary>
    /// <returns>The FSM for Bind.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the FSM cannot be found, which shouldn't happen.
    /// </exception>
    protected PlayMakerFSM GetOrFindBindFsm() {
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

    /// <summary>
    /// Attempts to locate and bind the 'Bind Effects' GameObject to the specified player object.
    /// </summary>
    /// <param name="playerObject">The player's object.</param>
    /// <param name="bindEffects">The player's 'Bind Effects' object, or null if not found.</param>
    /// <returns>true if the 'Bind Effects' GameObject is successfully found and bound; otherwise, false.</returns>
    protected bool CreateObjects(GameObject playerObject, out GameObject bindEffects) {
        _localBindEffects ??= HeroController.instance.gameObject.FindGameObjectInChildren("Bind Effects");
        if (_localBindEffects == null) {
            Logger.Warn("Could not find local Bind Effects object in hero object");
            bindEffects = null;
            return false;
        }

        bindEffects = playerObject.FindGameObjectInChildren("Bind Effects");
        if (bindEffects == null) {
            Logger.Warn("Player object does not have Bind Effects child, cannot play bind");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Finds and returns a bind effect with the specified name, creating it if it doesn't already exist.
    /// </summary>
    /// <param name="bindEffects">The player's Bind Effects object.</param>
    /// <param name="objectName">The name of the effect to find or create.</param>
    /// <returns>The existing or new effect, or null if the effect cannot be found or created</returns>
    protected GameObject? CreateEffectIfNotExists(GameObject bindEffects, string objectName) {
        var obj = bindEffects.FindGameObjectInChildren(objectName);
        if (obj == null) {
            var localObj = _localBindEffects.FindGameObjectInChildren(objectName);
            if (localObj == null) {
                Logger.Warn($"Could not find local {objectName} object, cannot play bind");
                return null;
            }

            obj = Object.Instantiate(localObj, bindEffects.transform);
            obj.name = objectName;
        }

        return obj;
    }
}

using System;
using System.Collections;
using GlobalSettings;
using SSMP.Util;
using SSMP.Internals;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects;

/// <summary>
/// Abstract base class for the animation effect of nail slashes.
/// </summary>
internal abstract class SlashBase : ParryableEffect {
    /// <inheritdoc/>
    public abstract override void Play(GameObject playerObject, byte[]? effectInfo);

    /// <inheritdoc/>
    public override byte[] GetEffectInfo() {
        var crestType = CrestTypeExt.FromInternal(PlayerData.instance.CurrentCrestID);
        if (crestType == CrestType.Beast) {
            return [(byte) crestType, (byte) (HeroController.instance.warriorState.IsInRageMode ? 1 : 0)];
        }
        
        return [(byte) crestType];
    }

    /// <summary>
    /// Plays the slash animation for the given player.
    /// </summary>
    /// <param name="playerObject">The GameObject representing the player.</param>
    /// <param name="effectInfo">A byte array containing effect info.</param>
    /// <param name="type">The type of nail slash.</param>
    protected void Play(GameObject playerObject, byte[]? effectInfo, SlashType type) {
        if (effectInfo == null || effectInfo.Length < 1) {
            Logger.Error("Could not get null or empty effect info for SlashBase");
            return;
        }

        var crestType = (CrestType) effectInfo[0];

        var isInBeastRageMode = false;
        if (crestType == CrestType.Beast) {
            isInBeastRageMode = effectInfo[1] == 1;
        }

        Play(playerObject, type, crestType, isInBeastRageMode);
    }

    /// <summary>
    /// Plays the slash animation for the given player.
    /// </summary>
    /// <param name="playerObject">The GameObject representing the player.</param>
    /// <param name="type">The type of nail slash.</param>
    /// <param name="crestType">The type of crest used by the player.</param>
    /// <param name="isInBeastRageMode">Whether the player is in rage mode with the Beast crest.</param>
    protected void Play(GameObject playerObject, SlashType type, CrestType crestType, bool isInBeastRageMode) {
        var toolCrest = ToolItemManager.GetCrestByName(crestType.ToInternal());
        if (toolCrest == null) {
            Logger.Error($"Could not find unknown ToolCrest with type: {crestType}, {crestType.ToInternal()}");
            return;
        }

        if (!GetConfigs(crestType, toolCrest, isInBeastRageMode, out var configGroup, out var overrideGroup)
            || configGroup == null) {
            return;
        }

        // For some reason the animation for the normal slash is used with the alt slash NailSlash component and
        // vice versa. So for the first two slash types, the NailSlash component is switched.
        // This is also the case for the normal down slash and alt down slash for the Wanderer crest.
        NailAttackBase? nailAttackBase = null;
        switch (type) {
            case SlashType.Normal:
                nailAttackBase = GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.AlternateSlash);
                break;
            case SlashType.NormalAlt:
                nailAttackBase = GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.NormalSlash);
                break;
            case SlashType.Up:
                nailAttackBase = GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.UpSlash);
                break;
            case SlashType.Wall:
                nailAttackBase = GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.WallSlash);
                break;
            case SlashType.Down:
                switch (crestType) {
                    case CrestType.Wanderer:
                        nailAttackBase = GetPropertyFromConfigGroup(
                            configGroup, overrideGroup, group => group.AltDownSlash);
                        break;
                    case CrestType.Reaper:
                    case CrestType.Witch:
                    case CrestType.Architect:
                        nailAttackBase = GetNailSlashInCrestObjectFromName(configGroup, "DownSlash New");
                        break;
                    case CrestType.Beast:
                        nailAttackBase = GetNailSlashInCrestObjectFromName(
                            configGroup,
                            isInBeastRageMode ? "SpinSlash Rage" : "SpinSlash"
                        );
                        break;
                    case CrestType.Shaman:
                        nailAttackBase = GetNailSlashInCrestObjectFromName(configGroup, "DownSlash");
                        break;
                }

                break;
            case SlashType.DownAlt:
                nailAttackBase = GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.DownSlash);
                break;
            default:
                Logger.Error($"Cannot play animation for unknown nail slash: {type}");
                break;
        }

        if (nailAttackBase == null) {
            Logger.Error("Cannot play animation with null NailSlash");
            return;
        }
        
        // TODO: below fix is hacky, but only in this animation effect do we know that the user has the Witch crest
        if (crestType == CrestType.Witch && type == SlashType.Down) {
            // Witch down slash animation uses animation clip from override animation library in config group
            // So we get the library from the already obtained config, get the clip by name and play it using the
            // player's sprite animator
            var overrideLib = configGroup.Config.heroAnimOverrideLib;
            if (overrideLib == null) {
                Logger.Warn("Witch crest down slash has null override animation lib");
            } else {
                var clip = overrideLib.GetClipByName("DownSpike");
                if (clip == null) {
                    Logger.Warn("Witch crest down slash override animation lib has no clip named 'DownSpike'");
                } else {
                    var spriteAnimator = playerObject.GetComponent<tk2dSpriteAnimator>();
                    spriteAnimator.Play(clip);                    
                }
            }
        }

        // Get the attacks gameObject from the player object
        var playerAttacks = playerObject.FindGameObjectInChildren("Attacks");

        var slashParent = new GameObject("Slash Parent");
        slashParent.transform.SetParent(playerAttacks.transform);
        slashParent.SetActive(false);
        slashParent.transform.localPosition = Vector3.zero;
        slashParent.transform.localScale = Vector3.one;

        // Instantiate the slash gameObject from the given prefab
        // and use the attack gameObject as transform reference
        var slashObj = Object.Instantiate(nailAttackBase.gameObject, slashParent.transform);

        var slash = slashObj.GetComponent<NailSlash>();
        var heroDownAttack = slashObj.GetComponent<HeroDownAttack>();
        var audio = slashObj.GetComponent<AudioSource>();
        var poly = slashObj.GetComponent<PolygonCollider2D>();
        var mesh = slashObj.GetComponent<MeshRenderer>();
        var anim = slashObj.GetComponent<tk2dSpriteAnimator>();
        var animName = slash.animName;

        Object.DestroyImmediate(slash);
        // For some attacks in crests, down slashes and down spikes, this component exists which will interfere
        // So we destroy it immediately
        if (heroDownAttack) {
            Object.DestroyImmediate(heroDownAttack);
        }

        slashParent.SetActive(true);
        
        audio.Play();
        mesh.enabled = true;

        var animTriggerCounter = 0;
        anim.AnimationEventTriggered = (_, _, _) => {
            ++animTriggerCounter;
            if (animTriggerCounter == 1) {
                poly.enabled = true;
            }

            if (animTriggerCounter == 2) {
                poly.enabled = false;
            }
        };
        anim.AnimationCompleted = (_, _) => {
            poly.enabled = false;
            mesh.enabled = false;
            anim.AnimationEventTriggered = null;

            if (crestType != CrestType.Shaman) {
                Object.Destroy(slashParent);
            }
        };

        var clipByName = anim.GetClipByName(animName);
        // TODO: FPS increase by Quickening from NailSlash
        anim.Play(clipByName, Mathf.Epsilon, clipByName.fps);
        
        // TODO: there is still another visual detail missing with the slashes with Shaman crest around Hornet's needle
        if (crestType == CrestType.Shaman) {
            MonoBehaviourUtil.Instance.StartCoroutine(PlayNailSlashTravel(slashObj, slashParent));
        }
        
        // TODO: nail imbued from NailAttackBase
    }

    /// <summary>
    /// Play the nail slash travel animation that is used for the movement of the nail slash when having the Shaman
    /// crest equipped. This is a coroutine that mimics a lot of the functionality of the NailSlashTravel class, but
    /// leaves out recoil and other player impacting behaviour.
    /// </summary>
    /// <param name="slashObj">The base slash object that should have the NailSlashTravel component.</param>
    /// <param name="slashParent">The slash parent, so we can destroy it later.</param>
    private IEnumerator PlayNailSlashTravel(GameObject slashObj, GameObject slashParent) {
        var travelComp = slashObj.GetComponent<NailSlashTravel>();

        travelComp.hasStarted = true;

        if (travelComp.particles) {
            travelComp.particles.Play(true);
        }

        yield return null;

        var transform = travelComp.transform;
        var worldPos = (Vector2) transform.position;

        if (travelComp.distanceFiller) {
            travelComp.distanceFiller.gameObject.SetActive(true);
        }

        travelComp.SetThunkerActive(true);
        
        // TODO: long needle tool distance multiplier (see NailSlashTravel.cs)

        for (var elapsed = 0f; elapsed < travelComp.travelDuration; elapsed += Time.deltaTime) {
            travelComp.elapsedT = elapsed / travelComp.travelDuration;

            // setPosition action in NailSlashTravel.cs
            var self = travelComp.travelDistance.MultiplyElements(
                new Vector2(Mathf.Sign(transform.lossyScale.x), 1f)
            );
            var vec2 = self.MultiplyElements(1f); // TODO: insert multiplier here

            var num = travelComp.travelCurve.Evaluate(travelComp.elapsedT);

            transform.SetPosition2D(worldPos + vec2 * num);

            if (travelComp.distanceFiller) {
                var newXScale = Mathf.Abs(vec2.x) * num;
                travelComp.distanceFiller.SetScaleX(newXScale);
                travelComp.distanceFiller.SetLocalPositionX(newXScale * 0.5f);
            }

            yield return null;
        }

        // TODO: this is a bit of a hack, but we only want to destroy the slash parent once the particles are done
        while (travelComp.particles && travelComp.particles.isPlaying) {
            yield return new WaitForSeconds(0.5f);
        }
        
        Object.Destroy(slashParent);
    }

    /// <summary>
    /// Get the hero controller config for the given parameters.
    /// </summary>
    /// <param name="crestType">The type of the crest used.</param>
    /// <param name="toolCrest">The ToolCrest instance for the used crest.</param>
    /// <param name="isInBeastRageMode">Whether the player is in rage mode with the Beast crest.</param>
    /// <param name="configGroup">If this method returns true, the config group for the given parameters. Otherwise,
    /// undefined or null.</param>
    /// <param name="overrideGroup">If this method returns true, the override config group if it exists. Otherwise,
    /// null.</param>
    /// <returns>True if the config could be found, otherwise false.</returns>
    protected bool GetConfigs(
        CrestType crestType, 
        ToolCrest toolCrest,
        bool isInBeastRageMode,
        out HeroController.ConfigGroup? configGroup,
        out HeroController.ConfigGroup? overrideGroup
    ) {
        configGroup = null;
        overrideGroup = null;
        
        foreach (var config in HeroController.instance.configs) {
            if (config.Config == toolCrest.HeroConfig) {
                configGroup = config;
                break;
            }
        }

        if (configGroup == null) {
            // TODO: maybe remove this warning if we simply default to the first config
            Logger.Warn($"Could not find ConfigGroup for ToolCrest with type: {crestType}, {crestType.ToInternal()}, falling back to default");

            configGroup = HeroController.instance.configs[0];
            if (configGroup == null) {
                Logger.Error("HeroController ConfigGroup array has null at position '0'!");
                return false;
            }
        }

        foreach (var specialConfig in HeroController.instance.specialConfigs) {
            if (specialConfig.Config == toolCrest.HeroConfig && 
                (specialConfig.Config != Gameplay.WarriorCrest.HeroConfig || isInBeastRageMode)) {
                overrideGroup = specialConfig;
                break;
            }
        }

        return true;
    }

    /// <summary>
    /// Get a property from either the given config group or its override group based on whether the override group
    /// has it defined. This is similar to behaviour in HeroController.
    /// </summary>
    /// <param name="configGroup">The normal config group to get the property from.</param>
    /// <param name="overrideGroup">The override config group to get the property from.</param>
    /// <param name="getPropertyFunc">The function to get the property given either config group.</param>
    /// <typeparam name="T">The type of the property to get.</typeparam>
    /// <returns>A nullable instance of the property.</returns>
    protected static T? GetPropertyFromConfigGroup<T>(
        HeroController.ConfigGroup configGroup,
        HeroController.ConfigGroup? overrideGroup,
        Func<HeroController.ConfigGroup, T?> getPropertyFunc
    ) {
        // If the override group is null, we get the value from the property from the normal group
        if (overrideGroup == null) {
            return getPropertyFunc.Invoke(configGroup);
        }

        // Get the value from the override group to check if it is null or not
        var overrideGroupValue = getPropertyFunc.Invoke(overrideGroup);
        if (overrideGroupValue == null) {
            // It is null, so we get the value from the normal group
            return getPropertyFunc.Invoke(configGroup);
        }

        // It is not null, so we return it
        return overrideGroupValue;
    }

    /// <summary>
    /// Get the NailSlash component in a crest based on the given config group and the name of the game object that has
    /// the NailSlash component.
    /// </summary>
    /// <param name="configGroup">The config group for the crest.</param>
    /// <param name="gameObjectName">The name of the game object that has the component.</param>
    /// <returns>A nullable NailSlash component.</returns>
    private NailSlash? GetNailSlashInCrestObjectFromName(HeroController.ConfigGroup configGroup, string gameObjectName) {
        var reaperNormalSlash = configGroup.NormalSlash;
        if (reaperNormalSlash == null) {
            Logger.Error("NormalSlash in crest config group is null");
            return null;
        }

        var reaperNormalSlashObj = reaperNormalSlash.gameObject;
        if (reaperNormalSlashObj == null) {
            Logger.Error("NormalSlash game object in crest config group is null");
            return null;
        }

        var reaperCrestObj = reaperNormalSlashObj.transform.parent.gameObject;
        if (reaperCrestObj == null) {
            Logger.Error("Crest game object as parent of slash is null");
            return null;
        }

        var downSlashNewObj = reaperCrestObj.FindGameObjectInChildren(gameObjectName);
        if (downSlashNewObj == null) {
            Logger.Error("Game object as child of crest object is null");
            return null;
        }

        return downSlashNewObj.GetComponent<NailSlash>();
    }

    /// <summary>
    /// Enumeration of nail slash types.
    /// </summary>
    protected enum SlashType {
        Normal,
        NormalAlt,
        Down,
        DownAlt,
        Up,
        Wall
    }
}

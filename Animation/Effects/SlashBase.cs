using System;
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
        
        return [(byte) CrestTypeExt.FromInternal(PlayerData.instance.CurrentCrestID)];
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
        var toolCrest = ToolItemManager.GetCrestByName(crestType.ToInternal());
        if (toolCrest == null) {
            Logger.Error($"Could not find unknown ToolCrest with type: {crestType}, {crestType.ToInternal()}");
            return;
        }
        
        var isInBeastRageMode = false;
        if (crestType == CrestType.Beast) {
            isInBeastRageMode = effectInfo[1] == 1;
        }

        HeroController.ConfigGroup? configGroup = null;
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
                return;
            }
        }

        HeroController.ConfigGroup? overrideGroup = null;
        foreach (var specialConfig in HeroController.instance.specialConfigs) {
            if (specialConfig.Config == toolCrest.HeroConfig && 
                (specialConfig.Config != Gameplay.WarriorCrest.HeroConfig || isInBeastRageMode)) {
                overrideGroup = specialConfig;
                break;
            }
        }

        // For some reason the animation for the normal slash is used with the alt slash NailSlash component and
        // vice versa. So for the first two slash types, the NailSlash component is switched.
        NailSlash? nailSlash = null;
        switch (type) {
            case SlashType.Normal:
                nailSlash = GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.AlternateSlash);
                break;
            case SlashType.Alt:
                nailSlash = GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.NormalSlash);
                break;
            case SlashType.Up:
                nailSlash = GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.UpSlash);
                break;
            case SlashType.Wall:
                nailSlash = GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.WallSlash);
                break;
            case SlashType.Down:
            default:
                Logger.Error($"Cannot play animation for unknown nail slash: {type}");
                break;
        }

        if (nailSlash == null) {
            Logger.Error("Cannot play animation with null NailSlash");
            return;
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
        var slashObj = Object.Instantiate(nailSlash.gameObject, slashParent.transform);

        var slash = slashObj.GetComponent<NailSlash>();
        var audio = slashObj.GetComponent<AudioSource>();
        var poly = slashObj.GetComponent<PolygonCollider2D>();
        var mesh = slashObj.GetComponent<MeshRenderer>();
        var anim = slashObj.GetComponent<tk2dSpriteAnimator>();
        var animName = slash.animName;

        Object.DestroyImmediate(slash);

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
            
            Object.Destroy(slashParent);
        };

        var clipByName = anim.GetClipByName(animName);
        // TODO: FPS increase by Quickening from NailSlash
        anim.Play(clipByName, Mathf.Epsilon, clipByName.fps);
        
        // TODO: nail imbued from NailAttackBase
    }

    private static T? GetPropertyFromConfigGroup<T>(
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
    /// Enumeration of nail slash types.
    /// </summary>
    protected enum SlashType {
        Normal,
        Alt,
        Down,
        Up,
        Wall
    }
}

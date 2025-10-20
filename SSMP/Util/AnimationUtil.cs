using System;
using System.Collections;
using GlobalSettings;
using SSMP.Internals;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Util;

public static class AnimationUtil {
    /// <summary>
    /// Execute the given action after the given delay in seconds. Will start a coroutine that waits for the delay,
    /// then invokes the action.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="delay">The delay in seconds as a float.</param>
    public static void ExecuteActionAfterDelay(Action action, float delay) {
        MonoBehaviourUtil.Instance.StartCoroutine(Wait());
        return;

        IEnumerator Wait() {
            yield return new WaitForSeconds(delay);
            
            action.Invoke();
        }
    }
    
    /// <summary>
    /// Get the hero controller config for the given parameters.
    /// </summary>
    /// <param name="crestType">The type of the crest used.</param>
    /// <param name="isInBeastRageMode">Whether the player is in rage mode with the Beast crest.</param>
    /// <param name="configGroup">If this method returns true, the config group for the given parameters. Otherwise,
    /// undefined or null.</param>
    /// <param name="overrideGroup">If this method returns true, the override config group if it exists. Otherwise,
    /// null.</param>
    /// <returns>True if the config could be found, otherwise false.</returns>
    public static bool GetConfigsFromCrestType(
        CrestType crestType, 
        out HeroController.ConfigGroup? configGroup,
        out HeroController.ConfigGroup? overrideGroup,
        bool isInBeastRageMode = false
    ) {
        configGroup = null;
        overrideGroup = null;
        
        var toolCrest = ToolItemManager.GetCrestByName(crestType.ToInternal());
        if (toolCrest == null) {
            Logger.Error($"Could not find unknown ToolCrest with type: {crestType}, {crestType.ToInternal()}");
            return false;
        }
        
        foreach (var config in HeroController.instance.configs) {
            if (config.Config == toolCrest.HeroConfig) {
                configGroup = config;
                break;
            }
        }

        if (configGroup == null) {
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
    /// Try to find an animation clip with the given name in the given config group's override animation library.
    /// </summary>
    /// <param name="configGroup">The config group to search in.</param>
    /// <param name="clipName">The name of the animation clip to find.</param>
    /// <param name="clip">The animation clip if this method returns true, otherwise null.</param>
    /// <returns>True if the animation clip was found, otherwise false.</returns>
    public static bool TryFindClipInOverrideGroup(
        HeroController.ConfigGroup configGroup,
        string clipName,
        out tk2dSpriteAnimationClip? clip
    ) {
        var overrideLib = configGroup.Config.heroAnimOverrideLib;
        if (overrideLib != null) {
            var overrideLibClip = overrideLib.GetClipByName(clipName);
            if (overrideLibClip != null) {
                clip = overrideLibClip;
                return true;
            }
        }

        clip = null;
        return false;
    }
}

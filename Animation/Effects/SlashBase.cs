using System.Collections.Generic;
using SSMP.Util;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects;

/// <summary>
/// Abstract base class for the animation effect of nail slashes.
/// </summary>
internal abstract class SlashBase : ParryableEffect {
    /// <summary>
    /// Base X and Y scales for the various slash types.
    /// </summary>
    private static readonly Dictionary<SlashType, Vector2> _baseScales = new() {
        { SlashType.Normal, new Vector2(1.6011f, 1.6452f) },
        { SlashType.Alt, new Vector2(1.257f, 1.4224f) },
        { SlashType.Down, new Vector2(1.125f, 1.28f) },
        { SlashType.Up, new Vector2(1.15f, 1.4f) },
        { SlashType.Wall, new Vector2(1.62f, 1.6452f) }
    };
    
    /// <inheritdoc/>
    public abstract override void Play(GameObject playerObject, bool[] effectInfo);

    /// <inheritdoc/>
    public override bool[] GetEffectInfo() {
        // var playerData = PlayerData.instance;
        //
        // return new[] {
        //     playerData.GetInt(nameof(PlayerData.health)) == 1,
        //     playerData.GetInt(nameof(PlayerData.health)) == playerData.GetInt(nameof(PlayerData.maxHealth)),
        //     playerData.GetBool(nameof(PlayerData.equippedCharm_6)), // Fury of the fallen
        //     playerData.GetBool(nameof(PlayerData.equippedCharm_13)), // Mark of pride
        //     playerData.GetBool(nameof(PlayerData.equippedCharm_18)), // Long nail
        //     playerData.GetBool(nameof(PlayerData.equippedCharm_35)) // Grubberfly's Elegy
        // };
        return [];
    }

    /// <summary>
    /// Plays the slash animation for the given player.
    /// </summary>
    /// <param name="playerObject">The GameObject representing the player.</param>
    /// <param name="effectInfo">A boolean array containing effect info.</param>
    /// <param name="nailSlash">The nail slash instance.</param>
    /// <param name="type">The type of nail slash.</param>
    protected void Play(GameObject playerObject, bool[] effectInfo, NailSlash nailSlash, SlashType type) {
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
        anim.AnimationEventTriggered = (animator, clip, frame) => {
            ++animTriggerCounter;
            if (animTriggerCounter == 1) {
                poly.enabled = true;
            }

            if (animTriggerCounter == 2) {
                poly.enabled = false;
            }
        };
        anim.AnimationCompleted = (animator, clip) => {
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

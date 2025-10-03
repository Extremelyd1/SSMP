using GlobalSettings;
using SSMP.Util;
using SSMP.Internals;
using SSMP.Networking.Packet;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects;

/// <summary>
/// Abstract base class for the animation effect of down spikes.
/// </summary>
internal abstract class DownSpikeBase : SlashBase {
    /// <inheritdoc/>
    public abstract override void Play(GameObject playerObject, byte[]? effectInfo);

    /// <summary>
    /// Plays the slash animation for the given player.
    /// </summary>
    /// <param name="playerObject">The GameObject representing the player.</param>
    /// <param name="effectInfo">A byte array containing effect info.</param>
    /// <param name="type">The type of nail slash.</param>
    protected void Play(GameObject playerObject, byte[]? effectInfo, DownSpikeType type) {
        if (effectInfo == null || effectInfo.Length < 1) {
            Logger.Error("Could not get null or empty effect info for SlashBase");
            return;
        }

        var packet = new Packet(effectInfo);
        var crestType = (CrestType) packet.ReadByte();
        var slashEffects = packet.ReadBitFlag<SlashEffect>();

        var toolCrest = ToolItemManager.GetCrestByName(crestType.ToInternal());
        if (toolCrest == null) {
            Logger.Error($"Could not find unknown ToolCrest with type: {crestType}, {crestType.ToInternal()}");
            return;
        }

        if (crestType is CrestType.Witch or CrestType.Architect) {
            Play(playerObject, SlashType.Down, crestType, slashEffects);
            return;
        }

        if (!GetConfigs(crestType, toolCrest, false, out var configGroup, out var overrideGroup)
            || configGroup == null) {
            return;
        }

        NailAttackBase? nailAttackBase = null;
        switch (type) {
            case DownSpikeType.Normal:
                nailAttackBase = GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.Downspike);
                break;
            default:
                Logger.Error($"Cannot play animation for unknown down spike: {type}");
                break;
        }

        if (nailAttackBase == null) {
            Logger.Error("Cannot play animation with null NailAttackBase");
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
        var slashObj = Object.Instantiate(nailAttackBase.gameObject, slashParent.transform);

        var slash = slashObj.GetComponent<Downspike>();
        var heroDownAttack = slashObj.GetComponent<HeroDownAttack>();
        var audio = slashObj.GetComponent<AudioSource>();
        var poly = slashObj.GetComponent<PolygonCollider2D>();
        var mesh = slashObj.GetComponent<MeshRenderer>();
        var anim = slashObj.GetComponent<tk2dSpriteAnimator>();
        var animName = slash.animName;
        var scale = slash.scale;

        Object.DestroyImmediate(slash);
        Object.DestroyImmediate(heroDownAttack);

        slashParent.SetActive(true);
        
        audio.Play();
        mesh.enabled = true;
        poly.enabled = true;

        // When the animation completes we are disabling the components and destroying the slash parent
        // Not sure if this is the moment this should happen
        anim.AnimationCompleted = (_, _) => {
            poly.enabled = false;
            mesh.enabled = false;
            anim.AnimationEventTriggered = null;
            
            Object.Destroy(slashParent);
        };

        anim.PlayFromFrame(animName, 0);
        
        var longclaw = slashEffects.Contains(SlashEffect.Longclaw);
        if (longclaw) {
            var multiplier = Gameplay.LongNeedleMultiplier;
            slashObj.transform.localScale = new Vector3(scale.x * multiplier.x, scale.y * multiplier.y, scale.z);
        } else {
            slashObj.transform.localScale = scale;
        }

        // TODO: nail imbued from NailAttackBase
    }

    /// <summary>
    /// Enumeration of nail slash types.
    /// </summary>
    protected enum DownSpikeType {
        Normal
    }
}

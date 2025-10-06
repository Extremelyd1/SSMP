using SSMP.Internals;
using SSMP.Networking.Packet;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects;

/// <summary>
/// Abstract base class for the animation effect of down spikes.
/// </summary>
internal class DownSpike : SlashBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, byte[]? effectInfo) {
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
        } else {
            Play(playerObject, SlashType.DownSpike, crestType, slashEffects);
        }
    }

    /// <inheritdoc/>
    protected override void HandleSlashSpriteAnimation(
        tk2dSpriteAnimator anim, 
        PolygonCollider2D poly, 
        MeshRenderer mesh, 
        CrestType crestType,
        GameObject slashParent, 
        string animName
    ) {
        if (crestType is CrestType.Witch or CrestType.Architect) {
            base.HandleSlashSpriteAnimation(anim, poly, mesh, crestType, slashParent, animName);
            return;
        }
        
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
    }
}

using System.Collections;
using SSMP.Internals;
using SSMP.Networking.Packet;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for a generic dash slash (when Hornet swings her nail whilst sprinting when having Hunter,
/// Witch, or Architect crest equipped.
/// </summary>
internal class DashSlash : SlashBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, byte[]? effectInfo) {
        if (effectInfo == null || effectInfo.Length < 1) {
            Logger.Error("Could not get null or empty effect info for DashAttack");
            return;
        }
        
        var packet = new Packet(effectInfo);
        var crestType = (CrestType) packet.ReadByte();
        var slashEffects = packet.ReadBitFlag<SlashEffect>();

        var sprintFsm = HeroController.instance.sprintFSM;

        if (crestType == CrestType.Hunter) {
            var configGroup = HeroController.instance.configs[0];
            var dashStabNailAttackPrefab = configGroup.DashStab.GetComponent<DashStabNailAttack>();
            
            var playerAttacks = playerObject.FindGameObjectInChildren("Attacks");
            if (playerAttacks == null) {
                Logger.Warn("Player object does not have player attacks child, cannot play dash slash");
                return;
            }

            var slashParent = new GameObject("Dash Slash Parent");
            slashParent.transform.SetParent(playerAttacks.transform);
            slashParent.transform.localPosition = Vector3.zero;
            slashParent.transform.localScale = Vector3.one;

            var slashObj = Object.Instantiate(dashStabNailAttackPrefab.gameObject, slashParent.transform);

            var dashStab = slashObj.GetComponent<DashStabNailAttack>();
            var poly = slashObj.GetComponent<PolygonCollider2D>();
            var mesh = slashObj.GetComponent<MeshRenderer>();
            var anim = slashObj.GetComponent<tk2dSpriteAnimator>();
            var scale = dashStab.scale;
            
            // Play frame 0 of animator
            anim.PlayFromFrame(0);

            // Enable polygon collider
            poly.enabled = true;
            // Enable mesh renderer
            mesh.enabled = true;

            // OnSlashStarting
            var longclaw = slashEffects.Contains(SlashEffect.Longclaw);
            ApplyLongclawMultiplier(longclaw, SlashType.Dash, slashObj, scale);

            // TODO: Nail imbuement (see OnPlaySlash in NailAttackBase.cs)
            
            // Activate game object in ActivateGameObject action in "Witch?" state

            MonoBehaviourUtil.Instance.StartCoroutine(DestroyAfterTime(slashParent, 0.2f));
        } else if (crestType == CrestType.Architect) {
            // Play the animation from SlashBase, given that the Sprint FSM calls StartSlash for the Architect crest
            Play(playerObject, SlashType.Dash, crestType, slashEffects);
            
            // TODO: Add charged dash stab/slash, see alternative states in Sprint FSM for Architect/Toolmaster
            var playAudioEvent = sprintFsm.GetFirstAction<PlayAudioEvent>("Drill Attack Unch");

            AudioUtil.PlayAudioEventAtPlayerObject(playAudioEvent, playerObject);
        }
    }

    /// <inheritdoc/>
    protected override NailAttackBase? GetNailAttackBase(
        SlashType type, 
        CrestType crestType, 
        bool isInBeastRageMode, 
        HeroController.ConfigGroup configGroup,
        HeroController.ConfigGroup? overrideGroup
    ) {
        if (crestType == CrestType.Architect) {
            return GetPropertyFromConfigGroup(
                configGroup, 
                overrideGroup, 
                group => GetNailAttackBaseComponentFromObject(group.DashStab)
            );
            // TODO: Add charged dash stab/slash, see alternative states in Sprint FSM for Architect/Toolmaster
        }
        return base.GetNailAttackBase(type, crestType, isInBeastRageMode, configGroup, overrideGroup);
    }

    /// <summary>
    /// Destroy given object after given time (in seconds).
    /// </summary>
    /// <param name="obj">The game object to destroy.</param>
    /// <param name="time">The time in seconds as a float.</param>
    private IEnumerator DestroyAfterTime(GameObject obj, float time) {
        yield return new WaitForSeconds(time);

        Object.Destroy(obj);
    }
}

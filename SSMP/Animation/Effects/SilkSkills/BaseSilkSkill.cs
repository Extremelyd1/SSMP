using System.Diagnostics.CodeAnalysis;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

internal abstract class BaseSilkSkill : DamageAnimationEffect {

    private const string SilkSkillsObjectName = "Special Attacks";
    private static GameObject? _localSilkAttacks;

    public static byte[] GetEffectFlags() {
        var voltFilament = ToolItemManager.GetToolByName("Zap Imbuement");

        return new byte[] {
            (byte)(voltFilament.IsEquipped ? 1 : 0)
        };
    }

    public override byte[]? GetEffectInfo() {
        return GetEffectFlags();
    }

    protected bool IsVolt(byte[]? effectInfo) {
        return effectInfo is [1];
    }

    public abstract override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo);

    protected static PlayMakerFSM GetSkillFSM() {
        var fsm = HeroController.instance.silkSpecialFSM;
        if (fsm == null) {
            throw new System.Exception("Unable to obtain Silk Skill FSM");
        }

        if (!fsm.Fsm.Initialized) {
            fsm.Init();
        }

        return fsm;
    }

    protected static bool TryGetLocalSilkAttacks([MaybeNullWhen(false)] out GameObject localSilkAttacks) {
        // Find local silk skills
        if (_localSilkAttacks == null) {
            _localSilkAttacks = HeroController.instance.gameObject.FindGameObjectInChildren(SilkSkillsObjectName);
            if (_localSilkAttacks == null) {
                Logger.Warn("Unable to find local Silk Silks object");
                localSilkAttacks = null;
                return false;
            }
        }

        // Find existing attacks
        localSilkAttacks = _localSilkAttacks;
        return true;
    }

    protected static GameObject GetPlayerSilkAttacks(GameObject playerObject) {
        var silkAttacks = playerObject.FindGameObjectInChildren(SilkSkillsObjectName);
        if (silkAttacks == null) {
            silkAttacks = new GameObject(SilkSkillsObjectName);
            silkAttacks.transform.SetParentReset(playerObject.transform);
        }

        return silkAttacks;
    }

    protected static void PlayHornetAttackSound(GameObject playerObject) {
        var fsm = GetSkillFSM();
        var anticAudio = fsm.GetAction<PlayRandomAudioClipTable>("A Sphere Antic", 2);
        if (anticAudio != null) {
            AudioUtil.PlayAudio(anticAudio, playerObject);
        }
    }

    protected static bool FindOrCreateAttack(GameObject playerObject, string name, out GameObject? attack) {
        // Find existing object
        var attacks = GetPlayerSilkAttacks(playerObject);
        attack = attacks.FindGameObjectInChildren(name);
        if (attack) {
            return false;
        }

        // Copy from local attacks
        if (!TryGetLocalSilkAttacks(out var localSilkAttacks)) {
            return false;
        }

        var localClash = localSilkAttacks.FindGameObjectInChildren(name);
        if (!localClash) {
            return false;
        }

        attack = Object.Instantiate(localClash, attacks.transform);
        attack.name = name;
        return true;
    }
}

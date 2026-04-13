using System.Diagnostics.CodeAnalysis;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

internal abstract class BaseSilkSkill : DamageAnimationEffect {

    private const string SilkSkillsObjectName = "Special Attacks";
    private static GameObject? _localSilkAttacks;

    public override byte[]? GetEffectInfo() {
        return null;
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

    protected static GameObject TryGetPlayerSilkAttacks(GameObject playerObject) {
        var silkAttacks = playerObject.FindGameObjectInChildren(SilkSkillsObjectName);
        if (silkAttacks == null) {
            silkAttacks = new GameObject(SilkSkillsObjectName);
            silkAttacks.transform.SetParentReset(playerObject.transform);
        }

        return silkAttacks;
    }

}

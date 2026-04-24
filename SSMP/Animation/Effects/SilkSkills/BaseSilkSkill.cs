using System.Diagnostics.CodeAnalysis;
using HutongGames.PlayMaker.Actions;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

/// <summary>
/// Base effect class for Silk Skills.
/// </summary>
internal abstract class BaseSilkSkill : DamageAnimationEffect {
    /// <summary>
    /// The name of the silk skills parent.
    /// </summary>
    private const string SilkSkillsObjectName = "Special Attacks";

    /// <summary>
    /// Cached object with silk skills.
    /// </summary>
    private static GameObject? _localSilkSkills;

    /// <summary>
    /// See <see cref="GetEffectInfo"/>. Determines if the player is using the Volt Filament.
    /// </summary>
    public static byte[] GetEffectFlags() {
        var voltFilament = ToolItemManager.GetToolByName("Zap Imbuement");

        return [
            (byte)(voltFilament.IsEquipped ? 1 : 0)
        ];
    }

    /// <inheritdoc/>
    public override byte[] GetEffectInfo() {
        return GetEffectFlags();
    }

    /// <summary>
    /// Determines if the player was using the Volt Filament.
    /// </summary>
    /// <param name="effectInfo">The effect info sent with the animation.</param>
    /// <returns>True if the player used Volt Filament, otherwise false.</returns>
    protected static bool IsVolt(byte[]? effectInfo) {
        return effectInfo is [1];
    }

    /// <summary>
    /// Gets the Silk Skill FSM.
    /// </summary>
    /// <returns>The found FSM.</returns>
    protected static PlayMakerFSM GetSkillFsm() {
        var fsm = HeroController.instance.silkSpecialFSM;
        if (fsm == null) {
            throw new System.Exception("Unable to obtain Silk Skill FSM");
        }

        if (!fsm.Fsm.Initialized) {
            fsm.Init();
        }

        return fsm;
    }

    /// <summary>
    /// Attempts to find the local silk skills object.
    /// </summary>
    /// <param name="localSilkSkills">The silk skills object, if found.</param>
    /// <returns>True if the object was found, otherwise false.</returns>
    protected static bool TryGetLocalSilkSkills([MaybeNullWhen(false)] out GameObject localSilkSkills) {
        // Find local silk skills
        if (_localSilkSkills == null) {
            _localSilkSkills = HeroController.instance.gameObject.FindGameObjectInChildren(SilkSkillsObjectName);
            if (_localSilkSkills == null) {
                Logger.Warn("Unable to find local Silk Silks object");
                localSilkSkills = null;
                return false;
            }
        }

        // Find existing attacks
        localSilkSkills = _localSilkSkills;
        return true;
    }

    /// <summary>
    /// Gets the silk skills object on a player.
    /// </summary>
    /// <param name="playerObject">The player to find silk skills on.</param>
    /// <returns>The found silk skills object.</returns>
    protected static GameObject GetPlayerSilkSkills(GameObject playerObject) {
        var silkAttacks = playerObject.FindGameObjectInChildren(SilkSkillsObjectName);
        if (silkAttacks == null) {
            silkAttacks = new GameObject(SilkSkillsObjectName);
            silkAttacks.transform.SetParentReset(playerObject.transform);
        }

        return silkAttacks;
    }

    /// <summary>
    /// Plays a loud attack sound.
    /// </summary>
    /// <param name="playerObject">The player to play the sound on.</param>
    protected static void PlayHornetAttackSound(GameObject playerObject) {
        var fsm = GetSkillFsm();
        var anticAudio = fsm.GetAction<PlayRandomAudioClipTable>("A Sphere Antic", 2);
        if (anticAudio != null) {
            AudioUtil.PlayAudio(anticAudio, playerObject);
        }
    }

    /// <summary>
    /// Attempts to find or create a silk skill object.
    /// </summary>
    /// <param name="playerObject">The player using the skill.</param>
    /// <param name="name">The name of the skill object.</param>
    /// <param name="skill">The found or created skill object.</param>
    /// <returns>True if the skill was created, false if it already existed or wasn't found.</returns>
    protected static bool FindOrCreateSkill(GameObject playerObject, string name, out GameObject? skill) {
        // Find existing object
        var skills = GetPlayerSilkSkills(playerObject);
        skill = skills.FindGameObjectInChildren(name);
        if (skill) {
            return false;
        }

        // Copy from local attacks
        if (!TryGetLocalSilkSkills(out var localSilkAttacks)) {
            return false;
        }

        var localSkill = localSilkAttacks.FindGameObjectInChildren(name);
        if (!localSkill) {
            return false;
        }

        skill = Object.Instantiate(localSkill, skills.transform);
        skill.name = name;
        return true;
    }

    /// <summary>
    /// <see cref="DamageAnimationEffect.SetDamageHeroState"/> with a calculated damage amount for silk skills.
    /// </summary>
    /// <param name="damager">The target game object to attach or remove the component from.</param>
    /// <param name="baseDamage">The base silk skill damage.</param>
    /// <param name="isVolt">If the Volt Filament is equipped.</param>
    /// <param name="isShaman">If the player is using the Shaman Crest.</param>
    protected DamageHero? SetDamageHeroStateCalculated(GameObject damager, int baseDamage, bool isVolt, bool isShaman) {
        float damage = baseDamage;
        if (isVolt) {
            damage += (float) ServerSettings.VoltFilamentDamage / 2;
        }

        if (isShaman) {
            damage += (float) ServerSettings.ShamanDamage / 2;
        }

        return SetDamageHeroState(damager, (int) damage);
    }
}

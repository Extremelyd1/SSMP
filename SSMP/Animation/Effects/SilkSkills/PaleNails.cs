using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using SSMP.Fsm;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

/// <summary>
/// Effect class for the Pale Nails Silk Skill.
/// </summary>
internal class PaleNails : BaseSilkSkill {
    /// <summary>
    /// The name of the blade summoning effect object.
    /// </summary>
    private const string AnticName = "Hornet_finger_blade_cast_silk";

    /// <summary>
    /// The number of nails to summon.
    /// </summary>
    private const int NailCount = 3;

    /// <summary>
    /// Offset to keep negative values when converting a short to an ushort.
    /// </summary>
    private const int PositionOffset = short.MaxValue;

    /// <summary>
    /// Scale used to keep a higher level of precision when converting a float to a short.
    /// </summary>
    private const int PositionScale = 5;

    /// <summary>
    /// Determines if this animation is for the summoning antic.
    /// </summary>
    public bool IsAntic = false;

    /// <summary>
    /// Cached nail objects for players mapped to their Unity instance ID. Used when firing.
    /// </summary>
    private static readonly Dictionary<int, GameObject[]> PlayerNails = [];

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var isVolt = IsVolt(effectInfo);
        var isShaman = crestType == CrestType.Shaman;

        // Play summon antic if appropriate
        if (IsAntic) {
            MonoBehaviourUtil.Instance.StartCoroutine(PlayAntic(playerObject.gameObject, isVolt, isShaman));
            return;
        }

        // At this point, the firing animation will be played

        // Get existing nails
        var id = playerObject.GetInstanceID();
        if (!PlayerNails.TryGetValue(id, out var nails) || nails.Length == 0) {
            return;
        }

        // Ensure nails aren't de-spawned
        if (nails.Any(obj => obj == null)) {
            return;
        }

        // Decode target position. If we can't decode, play the unguided variant
        var targetInfo = DecodeTargetInfo(effectInfo);
        if (!targetInfo.HasValue) {
            PlayNailFireUnguided(nails, isVolt, id);
            return;
        }

        // Fire at target position
        var target = FindTarget(targetInfo.Value);
        MonoBehaviourUtil.Instance.StartCoroutine(PlayNailFireTargeted(target, nails, isVolt, id));
    }

    /// <summary>
    /// Fires a set of nails at a given target.
    /// </summary>
    /// <param name="target">The target object to fire at.</param>
    /// <param name="nails">The set of nails to fire.</param>
    /// <param name="isVolt">If the volt filament effect should be used.</param>
    /// <param name="playerId">The 'id' of the player firing the nails.</param>
    private static IEnumerator PlayNailFireTargeted(GameObject target, GameObject[] nails, bool isVolt, int playerId) {
        // Copy nails and clear array before they can be fired by anything else
        GameObject[] playerNails = [.. nails];
        PlayerNails[playerId] = [];

        // Play audio
        if (isVolt) PlayVoltAudio(playerNails[0]);
        
        // Fire each nail
        foreach (var nail in playerNails) {
            // A nail has already de-spawned
            if (nail == null) {
                yield break;
            }

            // Set the target
            var fsm = nail.LocateMyFSM("Control");
            fsm.FsmVariables.GetFsmGameObject("Target").Value = target;

            // Send it at the target and wait a bit before sending the next
            fsm.Fsm.Event("FOLLOW BUDDY");

            yield return new WaitForSeconds(0.1f);
        }
    }

    /// <summary>
    /// Fires off a set of nails at their current positions.
    /// </summary>
    /// <param name="nails">The set of nails to fire.</param>
    /// <param name="isVolt">If the volt filament effect should be used.</param>
    /// <param name="playerId">The 'id' of the player that summoned the nails.</param>
    private static void PlayNailFireUnguided(GameObject[] nails, bool isVolt, int playerId) {
        // Play audio
        if (isVolt) PlayVoltAudio(nails[0]);

        // Fire each nail
        foreach (var nail in nails) {
            // Nail has already de-spawned
            if (nail == null) {
                return;
            }

            // Remove any target
            var fsm = nail.LocateMyFSM("Control");
            fsm.FsmVariables.GetFsmGameObject("Target").Value = null;

            // Send it off into the world immediately
            fsm.Fsm.Event("FOLLOW BUDDY");
        }

        // Clear nails
        PlayerNails[playerId] = [];
    }

    /// <summary>
    /// Plays the nail summoning antic effect.
    /// </summary>
    /// <param name="playerObject">The player that summoned the nails.</param>
    /// <param name="isVolt">If the volt filament effect should be used.</param>
    /// <param name="isShaman">If the shaman crest effects should be used.</param>
    /// <returns></returns>
    private IEnumerator PlayAntic(GameObject playerObject, bool isVolt, bool isShaman) {
        var fsm = GetSkillFsm();

        // Fire existing nails
        var id = playerObject.GetInstanceID();
        if (PlayerNails.TryGetValue(id, out var existingNails) && existingNails.Length > 0) {
            PlayNailFireUnguided(existingNails, isVolt, id);
        }

        // Play main antic
        PlayHornetAttackSound(playerObject);
        if (TryGetAntic(playerObject, out var antic)) {
            antic.SetActive(false);
            antic.SetActive(true);

            var volt = antic
                .FindGameObjectInChildren("offset")?
                .FindGameObjectInChildren("zap");

            if (volt) {
                volt.SetActive(false);
                volt.SetActive(isVolt);
            }
        }

        // Play volt audio
        if (isVolt) {
            var voltAudio = fsm.GetFirstAction<PlayAudioEvent>("Boss Needle Zap FX");
            AudioUtil.PlayAudio(voltAudio, playerObject);
        }

        // Wait for animation to finish
        yield return new WaitForSeconds(0.2f);

        // Play summon audio
        var needleAudio = fsm.GetFirstAction<PlayAudioEvent>("BossNeedle Cast");
        AudioUtil.PlayAudio(needleAudio, playerObject);

        var localNail = fsm.GetFirstAction<SpawnObjectFromGlobalPool>("BossNeedle Cast");

        // Summon nails
        var nails = new GameObject[NailCount];

        for (var i = 0; i < NailCount; i++) {
            // Spawn it in
            var nail = EffectUtils.SpawnGlobalPoolObject(localNail, playerObject.transform, 10)!;
            nail.transform.localScale = Vector3.one;

            // Set the damage state
            var damager = nail.FindGameObjectInChildren("Enemy Damager");
            if (damager) {
                SetDamageHeroStateCalculated(damager, ServerSettings.PaleNailsDamage, isVolt, isShaman);
            }

            // Remove interfering components
            nail.DestroyComponentsInChildren<HeroShamanRuneEffect>();
            nail.DestroyComponent<EventRegister>();
            nail.DestroyComponent<EventRegister>();

            // Remove shaman effects (completely if not shaman crest)
            var shaman = nail
                .FindGameObjectInChildren("Sprite")?
                .FindGameObjectInChildren("Rune Parent");

            if (shaman) {
                if (isShaman) {
                    shaman.SetActiveChildren(true);

                    var shamanSpawn = shaman.FindGameObjectInChildren("Shaman Rune Spawn");
                    shamanSpawn?.DestroyGameObjectInChildren("Shaman Rune Camera Bloom");

                    var shamanFire = shaman.FindGameObjectInChildren("Shaman Rune Fire");
                    shamanFire?.DestroyGameObjectInChildren("Shaman Rune Camera Bloom");
                } else {
                    Object.Destroy(shaman);
                }
            }

            nails[i] = nail;
        }

        // Set up FSMs for each nail
        for (var i = 0; i < NailCount; i++) {
            SetupNailFsm(playerObject, nails, i, isVolt);
        }

        // Store nails for firing later
        PlayerNails[id] = nails;

        // Wait for nail hover time to expire
        yield return new WaitForSeconds(1.8f);
        
        // Fire them
        PlayNailFireUnguided(nails, isVolt, id);
    }

    /// <summary>
    /// Plays volt audio when firing.
    /// </summary>
    /// <param name="nail">The nail to play the sound on.</param>
    private static void PlayVoltAudio(GameObject nail) {
        if (nail == null) return;
        
        // Play audio
        var fsm = nail.LocateMyFSM("Control");
        var audio = fsm.GetFirstAction<PlayAudioEventRandom>("Zap FX");
        AudioUtil.PlayAudio(audio, nail);
    }

    /// <summary>
    /// Sets FSM variables to help them fly smoothly.
    /// </summary>
    /// <param name="playerObject">The player that summoned the nails.</param>
    /// <param name="nails">All nails in the set, for reference.</param>
    /// <param name="index">The index of the nail in the set.</param>
    /// <param name="isVolt">Whether the volt filament effect should be used.</param>
    private static void SetupNailFsm(GameObject playerObject, GameObject[] nails, int index, bool isVolt) {
        // Fix the FSM
        var nail = nails[index];
        var fsm = nail.LocateMyFSM("Control");
        if (fsm == null) return;

        FixFsmForUse(fsm, playerObject, isVolt);

        // Set nail buddy and event
        string position;
        GameObject buddy1;
        GameObject buddy2;

        if (index == 0) {
            position = "TOP1";
            buddy1 = nails[1];
            buddy2 = nails[2];
        } else if (index == 1) {
            position = "MID1";
            buddy1 = nails[0];
            buddy2 = nails[2];
        } else {
            position = "BOT1";
            buddy1 = nails[0];
            buddy2 = nails[1];
        }

        fsm.Fsm.Variables.GetFsmGameObject("Buddy 1").Value = buddy1;
        fsm.Fsm.Variables.GetFsmGameObject("Buddy 2").Value = buddy2;

        fsm.Fsm.Event(position);
    }

    /// <summary>
    /// Changes the control FSM for use with a non-local player.
    /// </summary>
    /// <param name="fsm">The FSM of the nail.</param>
    /// <param name="playerObject">The player that summoned the nail.</param>
    /// <param name="isVolt">Whether the volt filament effect should be used.</param>
    private static void FixFsmForUse(PlayMakerFSM fsm, GameObject playerObject, bool isVolt) {
        fsm.enabled = false;

        const string followLeftName = "Follow HeroFacingLeft";
        const string followRightName = "Follow HeroFacingRight";

        // Set FSM variables
        var hero = new FsmGameObject { Value = playerObject };

        var wallTrueVar = new FsmFloat { Value = 1 };
        var wallTrackCount = new FsmInt { Value = 0 };
        var wallTrackTest = new FsmEnum { Value = Extensions.IntTest.LessThan };

        // Set follow targets
        var flyTo = fsm.GetFirstAction<DirectlyFlyTo>(followLeftName);
        flyTo.target = hero;

        flyTo = fsm.GetFirstAction<DirectlyFlyTo>(followRightName);
        flyTo.target = hero;

        // Set scale target
        var getScale = fsm.GetAction<GetScale>(followLeftName, 4);
        getScale?.gameObject.gameObject = hero;

        getScale = fsm.GetAction<GetScale>(followLeftName, 5);
        getScale?.gameObject.gameObject = hero;

        getScale = fsm.GetAction<GetScale>(followRightName, 4);
        getScale?.gameObject.gameObject = hero;

        getScale = fsm.GetAction<GetScale>(followRightName, 5);
        getScale?.gameObject.gameObject = hero;

        // Remove wall checks
        var wallCheck = fsm.GetAction<ConvertBoolToFloat>(followLeftName, 7);
        wallCheck?.trueValue = wallTrueVar;

        wallCheck = fsm.GetAction<ConvertBoolToFloat>(followRightName, 7);
        wallCheck?.trueValue = wallTrueVar;

        // Remove hooks
        var hook = fsm.GetAction<FsmStateActionInjector>(followLeftName, 12);
        hook?.Uninject();

        hook = fsm.GetAction<FsmStateActionInjector>(followRightName, 12);
        hook?.Uninject();

        hook = fsm.GetAction<FsmStateActionInjector>("Fire Antic", 0);
        hook?.Uninject();

        // Remove track trigger
        var trackTrigger = fsm.GetAction<CheckTrackTriggerCountV2>(followLeftName, 12);

        trackTrigger?.Count = wallTrackCount;
        trackTrigger?.Test = wallTrackTest;

        trackTrigger = fsm.GetAction<CheckTrackTriggerCountV2>(followRightName, 12);
        trackTrigger?.Count = wallTrackCount;
        trackTrigger?.Test = wallTrackTest;

        // Remove transitions
        var left = fsm.GetState(followLeftName);
        left.Transitions = [
            left.Transitions[0],
            left.Transitions[5]
        ];

        var right = fsm.GetState(followRightName);
        right.Transitions = [
            right.Transitions[0],
            right.Transitions[5],
        ];

        // Set volt state
        SetVolt(fsm, isVolt, "Init", 8);
        SetVolt(fsm, isVolt, "Init", 11);
        SetVolt(fsm, isVolt, "Fire Antic", 7);
        SetVolt(fsm, isVolt, "Launch", 6);
        SetVolt(fsm, isVolt, "Launch NoTarget", 11);

        var burst = fsm.GetFirstAction<BoolTest>("Burst");
        if (isVolt) {
            burst.isFalse = burst.isTrue;
        } else {
            burst.isTrue = burst.isFalse;
        }

        fsm.enabled = true;
    }

    /// <summary>
    /// Sets the animation strings for volt/non-volt to the same value, depending on the volt status.
    /// </summary>
    /// <param name="fsm">The FSM of the nail.</param>
    /// <param name="isVolt">Whether the volt filament effect should be used.</param>
    /// <param name="state">The name of the state to change.</param>
    /// <param name="index">The index of the action.</param>
    private static void SetVolt(PlayMakerFSM fsm, bool isVolt, string state, int index) {
        // Get the consumer of the volt state
        var boolToString = fsm.GetAction<ConvertBoolToString>(state, index);
        if (boolToString == null) {
            return;
        }

        // Ensure that the value is always the same
        if (isVolt) {
            boolToString.falseString = boolToString.trueString;
        } else {
            boolToString.trueString = boolToString.falseString;
        }
    }

    /// <summary>
    /// Gets the antic effect for summoning nails.
    /// </summary>
    /// <param name="playerObject">The player summoning nails.</param>
    /// <param name="antic">The antic, if found.</param>
    /// <returns>True if the antic was found, otherwise false.</returns>
    private static bool TryGetAntic(GameObject playerObject, [MaybeNullWhen(false)] out GameObject antic) {
        // Find existing first
        var effects = playerObject.FindGameObjectInChildren("Effects");
        if (effects == null) {
            antic = null;
            return false;
        }

        antic = effects.FindGameObjectInChildren(AnticName);
        if (antic != null) {
            return true;
        }

        // Create from local effects
        var localAntic = HeroController.instance.gameObject
            .FindGameObjectInChildren("Effects")?
            .FindGameObjectInChildren(AnticName);

        if (localAntic == null) {
            return false;
        }

        // Set name and remove components
        antic = Object.Instantiate(localAntic, effects.transform);
        antic.name = AnticName;

        antic.DestroyComponent<ToolEquipChecker>();

        return true;
    }

    /// <summary>
    /// Encodes a target object's position into a byte array.
    /// Also includes the volt filament status and if the target is a player or not.
    /// </summary>
    /// <param name="target">The target of a nail.</param>
    /// <returns>The bytes to send over the network.</returns>
    public static byte[] EncodeTargetInfo(GameObject target) {
        var position = target.transform.position;
        var isPlayer = target.GetComponent<CoroutineCancelComponent>() != null;

        // Convert floats to ushorts, scaling to preserve some precision, and offsetting to keep negative values
        var x = (ushort) ((position.x * PositionScale) + PositionOffset);
        var y = (ushort) ((position.y * PositionScale) + PositionOffset);

        // Split shorts into bytes
        return [
            GetEffectFlags()[0], // include volt status
            (byte) (x & 0xFF),
            (byte) (x >> 8),
            (byte) (y & 0xFF),
            (byte) (y >> 8),
            (byte) (isPlayer ? 1 : 0)
        ];

    }

    /// <summary>
    /// Converts a byte array to information about a nail target.
    /// </summary>
    /// <param name="info">The effect info received over the network.</param>
    /// <returns>The information about the target, or null if no target.</returns>
    private static NailTarget? DecodeTargetInfo(byte[]? info) {
        if (info == null || info.Length < 6) return null;

        // Convert two bytes to ushorts, then to floats.
        // Offset to restore the range to a short.
        var x = (float) BitConverter.ToUInt16([info[1], info[2]], 0) - PositionOffset;
        var y = (float) BitConverter.ToUInt16([info[3], info[4]], 0) - PositionOffset;

        // Undo precision scaling
        var position = new Vector2(x / PositionScale, y / PositionScale);
        var isPlayer = info[5] == 1;

        return new NailTarget {
            IsPlayer = isPlayer,
            Position = position
        };
    }

    /// <summary>
    /// Finds the target of a nail. If a suitable enemy or player wasn't found, the nails will target a new object
    /// in the given position.
    /// </summary>
    /// <param name="target">The target to find.</param>
    /// <returns>The target object.</returns>
    private static GameObject FindTarget(NailTarget target) {
        // Find all players and enemies within 2.5 units of the target
        var mask = LayerMask.GetMask("Player", "Default", "Enemies");

        // ReSharper disable once Unity.PreferNonAllocApi
        // Non-alloc version not preferred, because this method is not called frequently enough
        var inside = Physics2D.OverlapBoxAll(target.Position, new Vector2(2.5f, 2.5f), 0, mask);

        // Prioritize player
        if (target.IsPlayer) {
            // CoroutineCancelComponent is on all player objects and nothing else
            // It may be a good idea to create a custom component specifically for identifying players
            var player = inside.FirstOrDefault(obj => 
                (bool) obj.GetComponent<HeroController>() || (bool) obj.GetComponent<CoroutineCancelComponent>()
            );
            if (player) {
                return player.gameObject;
            }
        }

        // Find an enemy to target if possible
        var firstEnemy = inside.FirstOrDefault(obj => obj.gameObject.layer == (int) GlobalEnums.PhysLayers.ENEMIES);
        if (firstEnemy) {
            return firstEnemy.gameObject;
        }

        // Otherwise just create a placeholder that expires
        var targetObj = new GameObject {
            transform = {
                position = target.Position
            }
        };
        targetObj.DestroyAfterTime(5);

        return targetObj;
    }

    /// <summary>
    /// Information about where/what a nail is targeting.
    /// </summary>
    private struct NailTarget {
        public Vector2 Position;
        public bool IsPlayer;
    }
}

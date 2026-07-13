using System.Collections.Generic;
using System.Linq;
using GlobalSettings;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Tools;

/// <summary>
/// Class for the tool effect of the Tacks.
/// </summary>
internal class Tacks : BaseAttackTool {
    /// <summary>
    /// Cached prefab for the attacking Tacks.
    /// </summary>
    private static GameObject? _modifiedPrefab;

    /// <summary>
    /// Dictionary mapping player object IDs to a list of Tacks they own.
    /// </summary>
    private static readonly Dictionary<int, List<GameObject>> TackGroups = [];

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Play audio
        var toolFsm = HeroController.instance.toolsFSM;
        var audio = toolFsm.GetFirstAction<PlayAudioEventRandom>("Tacks Dir");
        AudioUtil.PlayAudio(audio, playerObject);

        var poisoned = EffectIsPoisoned(effectInfo);

        // Determine scatter angle
        var isFacingRight = playerObject.transform.localScale.x < 0;
        if (EffectIsOnWall(effectInfo)) {
            isFacingRight = !isFacingRight;
        }

        var minAngle = 150f;
        var maxAngle = 190f;

        if (isFacingRight) {
            minAngle = -10f;
            maxAngle = 30f;
        }

        var velocity = Vector2.zero;
        var position = playerObject.transform.position;

        // Set up tack group
        var key = playerObject.GetInstanceID();
        var group = new GameObject("Tack Group");

        if (!TackGroups.TryGetValue(key, out var groups)) {
            groups = [];
            TackGroups.Add(key, groups);
        }

        if (groups.Count >= 4) {
            groups = DestroyOldTacks(groups);
            TackGroups[key] = groups;
        }

        groups.Add(group);

        // Spawn in tacks
        var prefab = GetTackPrefab(playerObject);

        for (var i = 0; i < 4; i++) {
            var tack = prefab.Spawn(group.transform);

            // Set damage settings
            SetDamageHeroState(tack, ServerSettings.TacksDamage);

            // Set spawn position
            var variationX = Random.Range(-0.1f, 0.1f);
            var variationY = Random.Range(-0.15f, 0.15f);

            tack.transform.position = new Vector3(position.x + variationX, position.y + variationY);

            // Set velocity
            var speed = Random.Range(15f, 22f);
            var angle = Random.Range(minAngle, maxAngle);

            velocity.x = speed * Mathf.Cos(angle * (Mathf.PI / 180f));
            velocity.y = speed * Mathf.Sin(angle * (Mathf.PI / 180f));

            if (tack.TryGetComponent<Rigidbody2D>(out var body)) {
                body.linearVelocity = velocity;
            }

            // Set poison state
            var color = poisoned ? Gameplay.PoisonPouchTintColour : Color.white;
            var sprite = tack.GetComponent<tk2dSprite>();
            sprite.color = color;

            if (poisoned) {
                var particles = tack.FindGameObjectInChildren("Pt Poison Trail");
                if (particles && particles.TryGetComponent<ParticleSystem>(out var system)) {
                    system.Play();
                }
            }

            // Set FSM transition to destroy tacks when dealing damage to players
            var fsm = tack.LocateMyFSM("Control");
            var heroTouchTransition = fsm.GetState("Ready").Transitions.FirstOrDefault(t => t.EventName == "HERO TOUCH");

            if (heroTouchTransition == null) continue;

            FsmState destinationState;
            if (ShouldDoDamage && ServerSettings.IsPvpEnabled) {
                destinationState = fsm.GetState("Break");
            } else {
                destinationState = fsm.GetState("Hero Touch");
            }

            heroTouchTransition.ToState = destinationState.Name;
            heroTouchTransition.ToFsmState = destinationState;
        }
    }

    /// <summary>
    /// Destroys sets of tacks that exceed the tools limit.
    /// </summary>
    /// <param name="tackGroups">The tack groups that belong to a player.</param>
    /// <returns>The filtered tack groups.</returns>
    private static List<GameObject> DestroyOldTacks(List<GameObject> tackGroups) {
        List<GameObject> newTackGroups = [];

        // Iterate in reverse order to remove the oldest sets
        var validCount = 0;
        foreach (var tackGroup in tackGroups.Reverse<GameObject>()) {
            if (tackGroup == null) continue;
            
            // Empty groups don't count towards limit
            if (tackGroup.transform.childCount == 0) continue;
            

            // Only keep the last three
            validCount++;
            if (validCount < 4) {
                newTackGroups.Add(tackGroup);
                continue;
            }

            DestroyTackGroup(tackGroup);
        }
        
        // Return the new tacks
        return newTackGroups;
    }

    /// <summary>
    /// Destroys a set of tacks.
    /// </summary>
    /// <param name="tackGroup">The set of tacks to destroy.</param>
    private static void DestroyTackGroup(GameObject tackGroup) {
        // Send "LIMITED" event to de-spawn tacks
        for (var i = 0; i < tackGroup.transform.childCount; i++) {
            var fsm = tackGroup.transform.GetChild(i).gameObject.LocateMyFSM("Control");
            fsm.SendEvent("LIMITED");
        }
    }

    /// <summary>
    /// Destroys all of a player's tacks.
    /// </summary>
    /// <param name="playerObject">The player's object.</param>
    public static void DestroyPlayerTacks(GameObject? playerObject) {
        if (playerObject == null) {
            return;
        }

        // Get the player's tacks
        var key = playerObject.GetInstanceID();
        if (!TackGroups.TryGetValue(key, out var tackGroups)) {
            return;
        }

        // Destroy them all
        foreach (var group in tackGroups) {
            DestroyTackGroup(group);
        }
    }

    /// <summary>
    /// Gets or creates a modified tool prefab.
    /// </summary>
    /// <param name="playerObject">The player using the tool.</param>
    /// <returns>The modified prefab, if found.</returns>
    private GameObject? GetTackPrefab(GameObject playerObject) {
        // Create prefab if needed
        if (_modifiedPrefab == null) {
            // Create a copy of the original prefab
            var fsm = HeroController.instance.toolsFSM;

            var tacks = fsm.GetFirstAction<FlingObjectsFromGlobalPool>("Tacks L").gameObject.Value;
            _modifiedPrefab = EffectUtils.SpawnGlobalPoolObject(tacks, playerObject.transform, 0);

            if (!_modifiedPrefab) return null;

            _modifiedPrefab.SetActive(false);
            _modifiedPrefab.name = "TACK";

            // Remove interfering components
            _modifiedPrefab.DestroyComponent<ToolItemLimiter>();
            _modifiedPrefab.DestroyComponent<EventRegister>();

            var recycler = _modifiedPrefab.AddComponent<AutoRecycleSelf>();
            recycler.afterEvent = GlobalEnums.AfterEvent.LEVEL_UNLOAD;

            // Modify FSM to remove poison check
            var controller = _modifiedPrefab.LocateMyFSM("Control");

            var init = controller.GetState("Init");
            var pauseFrame = controller.GetState("Pause Frame");

            init.transitions[0] = new() {
                FsmEvent = FsmEvent.Finished,
                ToFsmState = pauseFrame,
                ToState = "Pause Frame"
            };
        }

        return _modifiedPrefab;
    }
}

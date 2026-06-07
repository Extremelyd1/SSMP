using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker.Actions;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using SSMP.Hooks;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

// ReSharper disable UnusedMember.Local

namespace SSMP.Game.Client;

/// <summary>
/// Class that manager patches such as IL and On hooks that are standalone patches for the multiplayer to function
/// correctly.
/// </summary>
internal class GamePatcher {
    private const BindingFlags InstanceNonPublicFlags = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags InstancePublicFlags = BindingFlags.Public | BindingFlags.Instance;
    private const int AlertRangeLineOfSightSelf = 1;
    private const int AlertRangeLineOfSightParent = 2;
    private const int TerrainLayerMask = 256;

    private static readonly FieldInfo? AlertRangeLineOfSightField =
        typeof(AlertRange).GetField("lineOfSight", InstanceNonPublicFlags);

    private static readonly FieldInfo? AlertRangeInitialParentField =
        typeof(AlertRange).GetField("initialParent", InstanceNonPublicFlags);

    private static readonly FieldInfo? LineOfSightDetectorAlertRangesField =
        typeof(LineOfSightDetector).GetField("alertRanges", InstanceNonPublicFlags);

    private static readonly FieldInfo? LineOfSightDetectorCanSeeHeroField =
        typeof(LineOfSightDetector).GetField("canSeeHero", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerMainCameraField =
        typeof(Walker).GetField("mainCamera", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerDidFulfilCameraDistanceConditionField =
        typeof(Walker).GetField("didFulfilCameraDistanceCondition", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerDidFulfilHeroXConditionField =
        typeof(Walker).GetField("didFulfilHeroXCondition", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerWaitHeroXField =
        typeof(Walker).GetField("waitHeroX", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerWaitForHeroXField =
        typeof(Walker).GetField("waitForHeroX", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerStartInactiveField =
        typeof(Walker).GetField("startInactive", InstanceNonPublicFlags | BindingFlags.Public);

    private static readonly FieldInfo? WalkerAmbushField =
        typeof(Walker).GetField("ambush", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerTurnCooldownRemainingField =
        typeof(Walker).GetField("turnCooldownRemaining", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerAggroEdgeTurnCooldownRemainingField =
        typeof(Walker).GetField("aggroEdgeTurnCooldownRemaining", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerCurrentFacingField =
        typeof(Walker).GetField("currentFacing", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerBodyColliderField =
        typeof(Walker).GetField("bodyCollider", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerPreventTurningToFaceHeroField =
        typeof(Walker).GetField("preventTurningToFaceHero", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerLineOfSightDetectorField =
        typeof(Walker).GetField("lineOfSightDetector", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerAlertRangeField =
        typeof(Walker).GetField("alertRange", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerIgnoreHolesField =
        typeof(Walker).GetField("ignoreHoles", InstanceNonPublicFlags | BindingFlags.Public);

    private static readonly FieldInfo? WalkerEdgeXAdjusterField =
        typeof(Walker).GetField("edgeXAdjuster", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerWalkTimeRemainingField =
        typeof(Walker).GetField("walkTimeRemaining", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerPausesField =
        typeof(Walker).GetField("pauses", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerBodyField =
        typeof(Walker).GetField("body", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerWalkSpeedRField =
        typeof(Walker).GetField("walkSpeedR", InstanceNonPublicFlags | BindingFlags.Public);

    private static readonly FieldInfo? WalkerWalkSpeedLField =
        typeof(Walker).GetField("walkSpeedL", InstanceNonPublicFlags | BindingFlags.Public);

    private static readonly MethodInfo? WalkerBeginTurningMethod =
        typeof(Walker).GetMethod("BeginTurning", InstanceNonPublicFlags);

    private static readonly MethodInfo? WalkerBeginStoppedMethod =
        typeof(Walker).GetMethod("BeginStopped", InstanceNonPublicFlags);

    private static readonly FieldInfo? WalkerV2HeroField =
        typeof(WalkerV2).GetField("hero", InstanceNonPublicFlags);

    private static readonly FieldInfo? ScuttlerControlHeroField =
        typeof(ScuttlerControl).GetField("hero", InstanceNonPublicFlags);

    private Hook? _chaseObjectDoBuzzHook;
    private Hook? _getHeroOnEnterHook;
    private Hook? _alertRangeIsHeroInRangeHook;
    private Hook? _lineOfSightDetectorUpdateHook;
    private Hook? _walkerUpdateWalkingHook;
    private Hook? _walkerUpdateWaitingForConditionsHook;

    /// <summary>
    /// Initializes the patch manager.
    /// </summary>
    public GamePatcher() {
    }

    /// <summary>
    /// Register the hooks.
    /// </summary>
    public void RegisterHooks() {
        EventHooks.InteractableBaseAddInsideIL += ILInteractableBaseAddInside;
        EventHooks.InteractableBaseLocalAddInsideIL += ILInteractableBaseAddInside;

        EventHooks.TransitionPointOnTriggerEnter2DIL += ILTransitionPointOnTrigger2D;
        EventHooks.TransitionPointOnTriggerStay2DIL += ILTransitionPointOnTrigger2D;

        EventHooks.CameraLockAreaAwake += OnCameraLockAreaAwake;

        _chaseObjectDoBuzzHook = new Hook(
            typeof(ChaseObject).GetMethod("DoBuzz", InstanceNonPublicFlags),
            OnChaseObjectDoBuzz
        );

        _getHeroOnEnterHook = new Hook(
            typeof(GetHero).GetMethod("OnEnter", InstancePublicFlags),
            OnGetHeroOnEnter
        );

        _alertRangeIsHeroInRangeHook = new Hook(
            typeof(AlertRange).GetMethod("IsHeroInRange", InstancePublicFlags),
            OnAlertRangeIsHeroInRange
        );

        _lineOfSightDetectorUpdateHook = new Hook(
            typeof(LineOfSightDetector).GetMethod("Update", InstanceNonPublicFlags),
            OnLineOfSightDetectorUpdate
        );

        _walkerUpdateWalkingHook = new Hook(
            typeof(Walker).GetMethod("UpdateWalking", InstanceNonPublicFlags),
            OnWalkerUpdateWalking
        );

        _walkerUpdateWaitingForConditionsHook = new Hook(
            typeof(Walker).GetMethod("UpdateWaitingForConditions", InstanceNonPublicFlags),
            OnWalkerUpdateWaitingForConditions
        );

        MonoBehaviourUtil.Instance.OnUpdateEvent += OnUpdateRetargetEnemyTransforms;
    }

    /// <summary>
    /// De-register the hooks.
    /// </summary>
    public void DeregisterHooks() {
        EventHooks.InteractableBaseAddInsideIL -= ILInteractableBaseAddInside;
        EventHooks.InteractableBaseLocalAddInsideIL -= ILInteractableBaseAddInside;

        EventHooks.TransitionPointOnTriggerEnter2DIL -= ILTransitionPointOnTrigger2D;
        EventHooks.TransitionPointOnTriggerStay2DIL -= ILTransitionPointOnTrigger2D;

        _chaseObjectDoBuzzHook?.Dispose();
        _chaseObjectDoBuzzHook = null;

        _getHeroOnEnterHook?.Dispose();
        _getHeroOnEnterHook = null;

        _alertRangeIsHeroInRangeHook?.Dispose();
        _alertRangeIsHeroInRangeHook = null;

        _lineOfSightDetectorUpdateHook?.Dispose();
        _lineOfSightDetectorUpdateHook = null;

        _walkerUpdateWalkingHook?.Dispose();
        _walkerUpdateWalkingHook = null;

        _walkerUpdateWaitingForConditionsHook?.Dispose();
        _walkerUpdateWaitingForConditionsHook = null;

        if (MonoBehaviourUtil.Instance != null) {
            MonoBehaviourUtil.Instance.OnUpdateEvent -= OnUpdateRetargetEnemyTransforms;
        }
    }

    /// <summary>
    /// Guards <see cref="ChaseObject"/> against null target values before the original method runs.
    /// </summary>
    /// <param name="orig">The original method.</param>
    /// <param name="self">The action instance being executed.</param>
    private static void OnChaseObjectDoBuzz(Action<ChaseObject> orig, ChaseObject self) {
        if (self.target?.Value == null) {
            return;
        }

        orig(self);
    }

    /// <summary>
    /// Overrides the PlayMaker <see cref="GetHero"/> action to resolve the nearest tracked player.
    /// </summary>
    /// <param name="orig">The original method.</param>
    /// <param name="self">The action instance being executed.</param>
    private static void OnGetHeroOnEnter(Action<GetHero> orig, GetHero self) {
        if (self.storeResult == null) {
            orig(self);
            return;
        }

        var requester = self.Fsm?.GameObject;
        self.storeResult.Value = PlayerTargetRegistry.GetNearestPlayer(requester);
        self.Finish();
    }

    /// <summary>
    /// Replaces the hero-only alert-range check with nearest tracked-player evaluation.
    /// </summary>
    /// <param name="orig">The original method.</param>
    /// <param name="self">The alert range being queried.</param>
    /// <returns>True when a tracked player is in range and visible; otherwise false.</returns>
    private static bool OnAlertRangeIsHeroInRange(Func<AlertRange, bool> orig, AlertRange self) {
        var target = PlayerTargetRegistry.GetNearestPlayer(self.transform.position, self.InsideGameObjects);
        if (target == null) {
            return false;
        }

        if (!self.ChecksLineOfSight) {
            return true;
        }

        var origin = GetAlertRangeLineOfSightOrigin(self);
        if (origin == null) {
            return false;
        }

        return !Helper.LineCast2DHit(origin.position, target.transform.position, TerrainLayerMask, out _);
    }

    /// <summary>
    /// Recomputes line-of-sight against the nearest tracked player instead of the local hero singleton.
    /// </summary>
    /// <param name="orig">The original method.</param>
    /// <param name="self">The detector being updated.</param>
    private static void OnLineOfSightDetectorUpdate(Action<LineOfSightDetector> orig, LineOfSightDetector self) {
        var target = GetNearestVisiblePlayer(self);
        var canSeeHero = false;

        if (target != null) {
            var origin = (Vector2) self.transform.position;
            var targetPosition = (Vector2) target.transform.position;
            var direction = targetPosition - origin;

            canSeeHero = !(bool) Helper.Raycast2D(origin, direction.normalized, direction.magnitude, TerrainLayerMask);
            Debug.DrawLine(origin, targetPosition, canSeeHero ? Color.green : Color.yellow);
        }

        LineOfSightDetectorCanSeeHeroField?.SetValue(self, canSeeHero);
    }

    /// <summary>
    /// Resolves the transform used as the line-of-sight origin for an alert range.
    /// </summary>
    /// <param name="alertRange">The alert range to inspect.</param>
    /// <returns>The transform to use for line-of-sight checks, or null if none applies.</returns>
    private static Transform? GetAlertRangeLineOfSightOrigin(AlertRange alertRange) {
        var lineOfSight = (int?) AlertRangeLineOfSightField?.GetValue(alertRange) ?? 0;
        return lineOfSight switch {
            AlertRangeLineOfSightSelf => alertRange.transform,
            AlertRangeLineOfSightParent => alertRange.transform.parent
                ? alertRange.transform.parent
                : AlertRangeInitialParentField?.GetValue(alertRange) as Transform,
            _ => null
        };
    }

    /// <summary>
    /// Finds the nearest visible tracked player for a line-of-sight detector.
    /// </summary>
    /// <param name="detector">The detector requesting a target.</param>
    /// <returns>The nearest visible tracked player if found; otherwise null.</returns>
    private static GameObject? GetNearestVisiblePlayer(LineOfSightDetector detector) {
        var alertRanges = LineOfSightDetectorAlertRangesField?.GetValue(detector) as AlertRange[];
        if (alertRanges == null || alertRanges.Length == 0) {
            return PlayerTargetRegistry.GetNearestPlayer(detector.gameObject);
        }

        var candidates = new List<GameObject>();
        foreach (var alertRange in alertRanges) {
            if (alertRange == null || !OnAlertRangeIsHeroInRange(static _ => false, alertRange)) {
                continue;
            }

            candidates.AddRange(alertRange.InsideGameObjects);
        }

        return PlayerTargetRegistry.GetNearestPlayer(detector.transform.position, candidates);
    }

    /// <summary>
    /// Replaces <see cref="Walker"/>'s hero-position startup gate with nearest tracked-player evaluation.
    /// </summary>
    /// <param name="orig">The original method.</param>
    /// <param name="self">The walker being updated.</param>
    private static void OnWalkerUpdateWaitingForConditions(Action<Walker> orig, Walker self) {
        var target = GetNearestPlayerTransform(self.transform.position);
        if (target == null) {
            orig(self);
            return;
        }

        var mainCamera = WalkerMainCameraField?.GetValue(self) as Camera;
        var didFulfilCameraDistanceCondition =
            (bool?) WalkerDidFulfilCameraDistanceConditionField?.GetValue(self) ?? false;
        var didFulfilHeroXCondition =
            (bool?) WalkerDidFulfilHeroXConditionField?.GetValue(self) ?? false;

        if (!didFulfilCameraDistanceCondition &&
            mainCamera != null &&
            (mainCamera.transform.position - self.transform.position).sqrMagnitude < 3600f) {
            didFulfilCameraDistanceCondition = true;
            WalkerDidFulfilCameraDistanceConditionField?.SetValue(self, true);
        }

        var waitHeroX = (float?) WalkerWaitHeroXField?.GetValue(self) ?? 0f;
        if (didFulfilCameraDistanceCondition &&
            !didFulfilHeroXCondition &&
            Mathf.Abs(target.position.x - waitHeroX) < 1f) {
            didFulfilHeroXCondition = true;
            WalkerDidFulfilHeroXConditionField?.SetValue(self, true);
        }

        var waitForHeroX = (bool?) WalkerWaitForHeroXField?.GetValue(self) ?? false;
        var startInactive = (bool?) WalkerStartInactiveField?.GetValue(self) ?? false;
        var ambush = (bool?) WalkerAmbushField?.GetValue(self) ?? false;

        if (didFulfilCameraDistanceCondition &&
            (!waitForHeroX || didFulfilHeroXCondition) &&
            !startInactive &&
            !ambush) {
            WalkerBeginStoppedMethod?.Invoke(self, [Walker.StopReasons.Bored]);
            self.StartMoving();
        }
    }

    /// <summary>
    /// Replaces <see cref="Walker"/>'s hero-facing chase logic with nearest tracked-player evaluation.
    /// </summary>
    /// <param name="orig">The original method.</param>
    /// <param name="self">The walker being updated.</param>
    private static void OnWalkerUpdateWalking(Action<Walker> orig, Walker self) {
        var target = GetNearestPlayerTransform(self.transform.position);
        if (target == null) {
            orig(self);
            return;
        }

        var turnCooldownRemaining = (float?) WalkerTurnCooldownRemainingField?.GetValue(self) ?? 0f;
        var aggroEdgeTurnCooldownRemaining =
            (float?) WalkerAggroEdgeTurnCooldownRemainingField?.GetValue(self) ?? 0f;
        var currentFacing = (int?) WalkerCurrentFacingField?.GetValue(self) ?? 0;
        var bodyCollider = WalkerBodyColliderField?.GetValue(self) as Collider2D;
        var lineOfSightDetector = WalkerLineOfSightDetectorField?.GetValue(self) as LineOfSightDetector;
        var alertRange = WalkerAlertRangeField?.GetValue(self) as AlertRange;
        var body = WalkerBodyField?.GetValue(self) as Rigidbody2D;

        if (bodyCollider == null || body == null || currentFacing == 0) {
            orig(self);
            return;
        }

        var canCheckTurn = turnCooldownRemaining <= 0f;
        var canAggroTurn = aggroEdgeTurnCooldownRemaining <= 0f;

        if (canCheckTurn) {
            if (new Sweep(bodyCollider, 1 - currentFacing, 3).Check(
                    bodyCollider.bounds.extents.x + 0.5f,
                    33024,
                    useTriggers: false
                )) {
                WalkerBeginTurningMethod?.Invoke(self, [-currentFacing]);
                return;
            }

            var preventTurningToFaceHero =
                (bool?) WalkerPreventTurningToFaceHeroField?.GetValue(self) ?? false;
            var seesTarget = lineOfSightDetector == null || lineOfSightDetector.CanSeeHero;
            var targetInRange = alertRange != null && alertRange.IsHeroInRange();
            var targetIsInFront = target.position.x > self.transform.position.x != currentFacing > 0;

            if (!preventTurningToFaceHero && canAggroTurn && targetIsInFront && seesTarget && targetInRange) {
                WalkerBeginTurningMethod?.Invoke(self, [-currentFacing]);
                return;
            }

            var ignoreHoles = (bool?) WalkerIgnoreHolesField?.GetValue(self) ?? false;
            var edgeXAdjuster = (float?) WalkerEdgeXAdjusterField?.GetValue(self) ?? 0f;
            if (!ignoreHoles &&
                !new Sweep(bodyCollider, 3, 3).Check(
                    0.25f,
                    33024,
                    out _,
                    useTriggers: false,
                    new Vector2((bodyCollider.bounds.extents.x + 0.5f + edgeXAdjuster) * currentFacing, 0f)
                )) {
                WalkerBeginTurningMethod?.Invoke(self, [-currentFacing]);
                return;
            }
        }

        if (!canAggroTurn) {
            WalkerWalkTimeRemainingField?.SetValue(self, 0f);
        } else {
            var pauses = (bool?) WalkerPausesField?.GetValue(self) ?? false;
            var lostTarget = (lineOfSightDetector != null && !lineOfSightDetector.CanSeeHero) ||
                             alertRange == null ||
                             !alertRange.IsHeroInRange();

            if (pauses && lostTarget) {
                var walkTimeRemaining = (float?) WalkerWalkTimeRemainingField?.GetValue(self) ?? 0f;
                walkTimeRemaining -= Time.deltaTime;

                if (walkTimeRemaining <= 0f) {
                    WalkerBeginStoppedMethod?.Invoke(self, [Walker.StopReasons.Bored]);
                    return;
                }

                WalkerWalkTimeRemainingField?.SetValue(self, walkTimeRemaining);
            }
        }

        var walkSpeedR = (float?) WalkerWalkSpeedRField?.GetValue(self) ?? 0f;
        var walkSpeedL = (float?) WalkerWalkSpeedLField?.GetValue(self) ?? 0f;
        body.linearVelocity = new Vector2(currentFacing > 0 ? walkSpeedR : walkSpeedL, body.linearVelocity.y);
    }

    /// <summary>
    /// Gets the nearest tracked player transform for the given world position.
    /// </summary>
    /// <param name="requesterPosition">The position to measure from.</param>
    /// <returns>The nearest tracked player transform if one exists; otherwise null.</returns>
    private static Transform? GetNearestPlayerTransform(Vector3 requesterPosition) {
        var target = PlayerTargetRegistry.GetNearestPlayer(requesterPosition);
        return target == null ? null : target.transform;
    }

    /// <summary>
    /// Refreshes cached enemy target transforms that are stored directly on vanilla behaviour components.
    /// </summary>
    private void OnUpdateRetargetEnemyTransforms() {
        UpdateCachedTargetField<WalkerV2>(WalkerV2HeroField);
        UpdateCachedTargetField<ScuttlerControl>(ScuttlerControlHeroField);
    }

    /// <summary>
    /// Updates a cached transform field on all active behaviour instances of the given type.
    /// </summary>
    /// <typeparam name="TComponent">The behaviour type containing the cached transform field.</typeparam>
    /// <param name="field">The field that stores the cached target transform.</param>
    private static void UpdateCachedTargetField<TComponent>(FieldInfo? field)
        where TComponent : Behaviour {
        if (field == null) {
            return;
        }

        foreach (var component in UnityEngine.Object.FindObjectsByType<TComponent>(
                     FindObjectsInactive.Exclude,
                     FindObjectsSortMode.None
                 )) {
            if (component == null || !component.isActiveAndEnabled) {
                continue;
            }

            var target = GetNearestPlayerTransform(component.transform.position);
            if (target != null) {
                field.SetValue(component, target);
            }
        }
    }

    /// <summary>
    /// IL hook to change the behaviour of the <see cref="InteractableBase"/> to add a check for whether it is dealing
    /// with the local player.
    /// </summary>
    private void ILInteractableBaseAddInside(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            var beforeFirstReturnLabel = c.DefineLabel();

            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdarg(1),
                i => i.MatchCallvirt(typeof(Component), "get_gameObject"),
                i => i.MatchCallvirt(typeof(GameObject), "get_layer"),
                i => i.MatchLdcI4(9),
                i => i.MatchBeq(out _)
            );

            c.Emit(OpCodes.Ldarg_1);
            // Emit a delegate that pops the collider argument off the stack and pushes a boolean onto the stack
            // that indicates whether the collider's game object has the tag "Player"
            c.EmitDelegate<Func<Collider2D, bool>>(col => col.gameObject.tag == "Player");

            // Branch if the tag is not "Player" to the pre-defined label, which is before the return
            // In other words, we return if it is not the local player
            c.Emit(OpCodes.Brfalse, beforeFirstReturnLabel);

            // Goto before the next return to mark our label there, so we can branch to it
            c.GotoNext(
                MoveType.Before,
                i => i.MatchRet()
            );

            c.MarkLabel(beforeFirstReturnLabel);
        } catch (Exception e) {
            Logger.Error($"Could not change InteractableBase#AddInside IL:\n{e}");
        }
    }

    /// <summary>
    /// IL hook to change the behaviour of the <see cref="TransitionPoint"/> to add a check for whether it is dealing
    /// with the local player.
    /// </summary>
    private void ILTransitionPointOnTrigger2D(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            ILLabel? returnLabel = null;

            c.GotoNext(
                MoveType.After,
                i => i.MatchLdarg(1),
                i => i.MatchCallvirt(typeof(Component), "get_gameObject"),
                i => i.MatchCallvirt(typeof(GameObject), "get_layer"),
                i => i.MatchLdcI4(9),
                i => i.MatchBneUn(out returnLabel)
            );

            if (returnLabel == null) {
                Logger.Error($"Could not change TransitionPoint#OnTrigger{{Enter,Stay}}2D IL:\nCould not find label");
                return;
            }

            c.Emit(OpCodes.Ldarg_1);
            // Emit a delegate that pops the collider argument off the stack and pushes a boolean onto the stack
            // that indicates whether the collider's game object has the tag "Player"
            c.EmitDelegate<Func<Collider2D, bool>>(movingObj => movingObj.gameObject.tag == "Player");

            // Branch if the tag is not "Player" to the pre-defined label, which is before the return
            // In other words, we return if it is not the local player
            c.Emit(OpCodes.Brfalse, returnLabel);
        } catch (Exception e) {
            Logger.Error($"Could not change TransitionPoint#OnTrigger{{Enter,Stay}}2D IL:\n{e}");
        }
    }

    /// <summary>
    /// Hook to add a tag include to the <see cref="TrackTriggerObjects"/> of <see cref="CameraLockArea"/> to ensure
    /// that it only triggers on the local player.
    /// </summary>
    private void OnCameraLockAreaAwake(CameraLockArea cameraLockArea) {
        cameraLockArea.tagIncludeList ??= [];
        cameraLockArea.tagIncludeList.Add("Player");
    }
}

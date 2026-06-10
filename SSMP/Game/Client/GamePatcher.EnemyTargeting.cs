using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using MonoMod.RuntimeDetour;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Game.Client;

/// <summary>
/// Contains multiplayer-aware aggression, retargeting, and walking patches for enemy AI.
/// </summary>
internal partial class GamePatcher {
    /// <summary>
    /// Minimum interval, in seconds, between periodic refreshes of cached vanilla enemy target fields.
    /// </summary>
    private const float EnemyRetargetIntervalSeconds = 0.2f;

    /// <summary>
    /// Squared-distance bias used before switching an enemy from its current approved target to a new candidate target.
    /// Higher values make target switching less twitchy.
    /// </summary>
    private const float TargetSwitchDistanceBias = 4f;

    /// <summary>
    /// Approved multiplayer target per enemy owner instance ID.
    /// </summary>
    private static readonly Dictionary<int, GameObject> EnemyApprovedTargets = new();

    /// <summary>
    /// Active PlayMaker FSM action types that consume an enemy target and should be forced to use the
    /// enemy-approved multiplayer target.
    /// </summary>
    private static readonly HashSet<Type> TargetedFsmActionTypes = [
        typeof(ChaseObject),
        typeof(ChaseObjectV2),
        typeof(ChaseObjectGround),
        typeof(FaceObject),
        typeof(FaceObjectV2),
        typeof(FaceDirection),
        typeof(DistanceFly),
        typeof(DistanceFlyV2),
        typeof(DistanceFlySmooth),
        typeof(FireAtTarget),
        typeof(GetDistance),
        typeof(CheckTargetDirection),
        typeof(GetAngleToTarget2D),
        typeof(DistanceBetweenPoints2D),
        typeof(CheckAlertRange),
        typeof(CheckCanSeeHero)
    ];

    /// <summary>
    /// Cached reflected <see cref="FsmGameObject"/> fields for target-consuming FSM action types.
    /// </summary>
    private static readonly Dictionary<Type, FieldInfo[]> TargetedActionGameObjectFieldCache = new();

    /// <summary>
    /// Reflected private field storing the camera used by <see cref="Walker"/> startup and distance checks.
    /// </summary>
    private static readonly FieldInfo? WalkerMainCameraField =
        typeof(Walker).GetField("mainCamera", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing whether a <see cref="Walker"/> has passed its camera-distance activation check.
    /// </summary>
    private static readonly FieldInfo? WalkerDidFulfilCameraDistanceConditionField =
        typeof(Walker).GetField("didFulfilCameraDistanceCondition", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing whether a <see cref="Walker"/> has passed its hero-X activation check.
    /// </summary>
    private static readonly FieldInfo? WalkerDidFulfilHeroXConditionField =
        typeof(Walker).GetField("didFulfilHeroXCondition", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the X position used by <see cref="Walker"/> hero-X activation logic.
    /// </summary>
    private static readonly FieldInfo? WalkerWaitHeroXField =
        typeof(Walker).GetField("waitHeroX", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing whether a <see cref="Walker"/> should wait for the hero-X activation condition.
    /// </summary>
    private static readonly FieldInfo? WalkerWaitForHeroXField =
        typeof(Walker).GetField("waitForHeroX", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected field storing whether a <see cref="Walker"/> starts inactive until activation conditions are satisfied.
    /// </summary>
    private static readonly FieldInfo? WalkerStartInactiveField =
        typeof(Walker).GetField("startInactive", InstanceNonPublicFlags | BindingFlags.Public);

    /// <summary>
    /// Reflected private field storing whether a <see cref="Walker"/> is configured as an ambush enemy.
    /// </summary>
    private static readonly FieldInfo? WalkerAmbushField =
        typeof(Walker).GetField("ambush", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the regular turn cooldown remaining for a <see cref="Walker"/>.
    /// </summary>
    private static readonly FieldInfo? WalkerTurnCooldownRemainingField =
        typeof(Walker).GetField("turnCooldownRemaining", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the aggro edge-turn cooldown remaining for a <see cref="Walker"/>.
    /// </summary>
    private static readonly FieldInfo? WalkerAggroEdgeTurnCooldownRemainingField =
        typeof(Walker).GetField("aggroEdgeTurnCooldownRemaining", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the current facing direction of a <see cref="Walker"/>.
    /// </summary>
    private static readonly FieldInfo? WalkerCurrentFacingField =
        typeof(Walker).GetField("currentFacing", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the body collider used by <see cref="Walker"/> movement and sweep checks.
    /// </summary>
    private static readonly FieldInfo? WalkerBodyColliderField =
        typeof(Walker).GetField("bodyCollider", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing whether a <see cref="Walker"/> is prevented from turning to face the hero.
    /// </summary>
    private static readonly FieldInfo? WalkerPreventTurningToFaceHeroField =
        typeof(Walker).GetField("preventTurningToFaceHero", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the line-of-sight detector used by a <see cref="Walker"/>.
    /// </summary>
    private static readonly FieldInfo? WalkerLineOfSightDetectorField =
        typeof(Walker).GetField("lineOfSightDetector", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the alert range used by a <see cref="Walker"/>.
    /// </summary>
    private static readonly FieldInfo? WalkerAlertRangeField =
        typeof(Walker).GetField("alertRange", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected field storing whether a <see cref="Walker"/> should ignore ledge / hole checks.
    /// </summary>
    private static readonly FieldInfo? WalkerIgnoreHolesField =
        typeof(Walker).GetField("ignoreHoles", InstanceNonPublicFlags | BindingFlags.Public);

    /// <summary>
    /// Reflected private field storing the X offset applied to <see cref="Walker"/> edge checks.
    /// </summary>
    private static readonly FieldInfo? WalkerEdgeXAdjusterField =
        typeof(Walker).GetField("edgeXAdjuster", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the remaining walk time before a pausing <see cref="Walker"/> stops.
    /// </summary>
    private static readonly FieldInfo? WalkerWalkTimeRemainingField =
        typeof(Walker).GetField("walkTimeRemaining", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing whether a <see cref="Walker"/> may pause when it loses its target.
    /// </summary>
    private static readonly FieldInfo? WalkerPausesField =
        typeof(Walker).GetField("pauses", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the rigidbody used by <see cref="Walker"/> movement.
    /// </summary>
    private static readonly FieldInfo? WalkerBodyField =
        typeof(Walker).GetField("body", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected field storing the right-facing walk speed for a <see cref="Walker"/>.
    /// </summary>
    private static readonly FieldInfo? WalkerWalkSpeedRField =
        typeof(Walker).GetField("walkSpeedR", InstanceNonPublicFlags | BindingFlags.Public);

    /// <summary>
    /// Reflected field storing the left-facing walk speed for a <see cref="Walker"/>.
    /// </summary>
    private static readonly FieldInfo? WalkerWalkSpeedLField =
        typeof(Walker).GetField("walkSpeedL", InstanceNonPublicFlags | BindingFlags.Public);

    /// <summary>
    /// Reflected private method used to make a <see cref="Walker"/> begin turning.
    /// </summary>
    private static readonly MethodInfo? WalkerBeginTurningMethod =
        typeof(Walker).GetMethod("BeginTurning", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private method used to make a <see cref="Walker"/> enter its stopped state.
    /// </summary>
    private static readonly MethodInfo? WalkerBeginStoppedMethod =
        typeof(Walker).GetMethod("BeginStopped", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the cached hero transform used by <see cref="WalkerV2"/>.
    /// </summary>
    private static readonly FieldInfo? WalkerV2HeroField =
        typeof(WalkerV2).GetField("hero", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the cached hero transform or object used by <see cref="ScuttlerControl"/>.
    /// </summary>
    private static readonly FieldInfo? ScuttlerControlHeroField =
        typeof(ScuttlerControl).GetField("hero", InstanceNonPublicFlags);

    /// <summary>
    /// Hook for replacing <see cref="Walker"/> walking updates with multiplayer-aware target handling.
    /// </summary>
    private Hook? _walkerUpdateWalkingHook;

    /// <summary>
    /// Hook for replacing <see cref="Walker"/> activation-condition updates with multiplayer-aware target handling.
    /// </summary>
    private Hook? _walkerUpdateWaitingForConditionsHook;

    /// <summary>
    /// Hooks registered on target-consuming FSM action <c>OnEnter</c> methods.
    /// </summary>
    private readonly List<Hook> _targetedFsmActionEnterHooks = [];

    /// <summary>
    /// Next unscaled time at which cached vanilla enemy target fields should be refreshed.
    /// </summary>
    private float _nextEnemyRetargetTime;

    /// <summary>
    /// Gets the currently approved multiplayer target for an enemy.
    /// </summary>
    /// <param name="requester">The enemy object or one of its child/component objects.</param>
    /// <returns>The approved tracked player target, or <see langword="null"/> when no valid target is approved.</returns>
    private static GameObject? GetApprovedEnemyTarget(GameObject requester) {
        var owner = GetEnemyTargetOwner(requester);
        return owner != null && EnemyApprovedTargets.TryGetValue(owner.GetInstanceID(), out var target) &&
               target != null
            ? target
            : null;
    }

    /// <summary>
    /// Stores the approved multiplayer target for an enemy.
    /// </summary>
    /// <param name="requester">The enemy object or one of its child/component objects.</param>
    /// <param name="target">The tracked player target approved by acquisition.</param>
    private static void ApproveEnemyTarget(GameObject requester, GameObject target) {
        var owner = GetEnemyTargetOwner(requester);
        if (owner == null || target == null) {
            return;
        }

        EnemyApprovedTargets[owner.GetInstanceID()] = target;
    }

    /// <summary>
    /// Registers aggression-specific hooks and periodic target refresh logic.
    /// </summary>
    private void RegisterAggressionHooks() {
        _walkerUpdateWalkingHook = new Hook(
            typeof(Walker).GetMethod("UpdateWalking", InstanceNonPublicFlags)!,
            OnWalkerUpdateWalking
        );

        _walkerUpdateWaitingForConditionsHook = new Hook(
            typeof(Walker).GetMethod("UpdateWaitingForConditions", InstanceNonPublicFlags)!,
            OnWalkerUpdateWaitingForConditions
        );

        RegisterTargetedFsmActionEnterHooks();

        MonoBehaviourUtil.Instance.OnUpdateEvent += OnUpdateRetargetEnemyTransforms;
    }

    /// <summary>
    /// Disposes aggression-specific hooks and periodic target refresh logic.
    /// </summary>
    private void DisposeAggressionHooks() {
        _walkerUpdateWalkingHook?.Dispose();
        _walkerUpdateWalkingHook = null;

        _walkerUpdateWaitingForConditionsHook?.Dispose();
        _walkerUpdateWaitingForConditionsHook = null;

        DisposeTargetedFsmActionEnterHooks();

        if (MonoBehaviourUtil.Instance != null) {
            MonoBehaviourUtil.Instance.OnUpdateEvent -= OnUpdateRetargetEnemyTransforms;
        }
    }

    /// <summary>
    /// Hooks target-consuming FSM action <c>OnEnter</c> methods so their target fields are corrected before
    /// one-shot logic such as firing, distance checks, or state gates can read the vanilla hero singleton.
    /// </summary>
    private void RegisterTargetedFsmActionEnterHooks() {
        DisposeTargetedFsmActionEnterHooks();

        foreach (var actionType in TargetedFsmActionTypes) {
            var onEnterMethod = actionType.GetMethod("OnEnter", InstancePublicFlags | InstanceNonPublicFlags);
            if (onEnterMethod == null || onEnterMethod.DeclaringType != actionType) {
                continue;
            }

            try {
                _targetedFsmActionEnterHooks.Add(new Hook(onEnterMethod, OnTargetedFsmActionEnter));
            } catch (Exception e) {
                Logger.Debug(
                    $"Could not hook {actionType.Name}.OnEnter for target retargeting: " +
                    $"{e.GetType().Name}: {e.Message}"
                );
            }
        }
    }

    /// <summary>
    /// Disposes target-consuming FSM action <c>OnEnter</c> hooks.
    /// </summary>
    private void DisposeTargetedFsmActionEnterHooks() {
        foreach (var hook in _targetedFsmActionEnterHooks) {
            hook.Dispose();
        }

        _targetedFsmActionEnterHooks.Clear();
    }

    /// <summary>
    /// Forces an FSM action to use its enemy-approved target before and after its original <c>OnEnter</c> logic.
    /// </summary>
    /// <param name="orig">The original action enter method.</param>
    /// <param name="self">The FSM action instance.</param>
    private static void OnTargetedFsmActionEnter(Action<FsmStateAction> orig, FsmStateAction self) {
        ForceApprovedTargetOnFsmAction(self);
        orig(self);
        ForceApprovedTargetOnFsmAction(self);
    }

    /// <summary>
    /// Replaces <see cref="Walker"/>'s hero-position startup gate with approved-target evaluation.
    /// </summary>
    /// <param name="orig">The original method.</param>
    /// <param name="self">The walker being updated.</param>
    private static void OnWalkerUpdateWaitingForConditions(Action<Walker> orig, Walker self) {
        var targetObject = GetApprovedEnemyTarget(self.gameObject);
        var target = targetObject == null ? null : targetObject.transform;

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
    /// Replaces <see cref="Walker"/>'s hero-facing chase logic with approved-target evaluation.
    /// </summary>
    /// <param name="orig">The original method.</param>
    /// <param name="self">The walker being updated.</param>
    private static void OnWalkerUpdateWalking(Action<Walker> orig, Walker self) {
        var targetObject = GetApprovedEnemyTarget(self.gameObject);
        var target = targetObject == null ? null : targetObject.transform;

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
    /// Refreshes enemy target references that need to follow the approved multiplayer target.
    /// </summary>
    private void OnUpdateRetargetEnemyTransforms() {
        UpdateTargetedFsmActions();

        if (Time.unscaledTime < _nextEnemyRetargetTime) {
            return;
        }

        _nextEnemyRetargetTime = Time.unscaledTime + EnemyRetargetIntervalSeconds;

        UpdateCachedTargetField<WalkerV2>(WalkerV2HeroField);
        UpdateCachedTargetField<ScuttlerControl>(ScuttlerControlHeroField);
    }

    /// <summary>
    /// Forces a target-consuming FSM action to use the approved multiplayer target of its owning enemy.
    /// </summary>
    /// <param name="action">The FSM action to retarget.</param>
    private static void ForceApprovedTargetOnFsmAction(FsmStateAction action) {
        var requester = action.Fsm?.GameObject;
        if (requester == null) {
            return;
        }

        var approvedTarget = GetApprovedEnemyTarget(requester);
        if (approvedTarget == null) {
            return;
        }

        RetargetFsmActionGameObjectFields(action, approvedTarget);
    }

    /// <summary>
    /// Updates active enemy FSM target-consuming actions so they use their approved multiplayer target.
    /// </summary>
    private static void UpdateTargetedFsmActions() {
        foreach (var fsm in UnityEngine.Object.FindObjectsByType<PlayMakerFSM>(
                     FindObjectsInactive.Exclude,
                     FindObjectsSortMode.None
                 )) {
            RetargetActiveTargetedFsmActions(fsm);
        }
    }

    /// <summary>
    /// Retargets the currently active target-consuming actions of one enemy FSM to the enemy's approved target.
    /// </summary>
    /// <param name="fsm">The FSM to inspect.</param>
    private static void RetargetActiveTargetedFsmActions(PlayMakerFSM fsm) {
        if (fsm == null || !fsm.isActiveAndEnabled) {
            return;
        }

        if (fsm.GetComponentInParent<HealthManager>() == null) {
            return;
        }

        if (PlayerTargetRegistry.GetTrackedPlayerRoot(fsm.gameObject) != null) {
            return;
        }

        var approvedTarget = GetApprovedEnemyTarget(fsm.gameObject);
        if (approvedTarget == null) {
            return;
        }

        var activeState = fsm.Fsm?.ActiveState;
        if (activeState?.Actions == null) {
            return;
        }

        foreach (var action in activeState.Actions) {
            if (action == null || !action.Enabled) {
                continue;
            }

            if (!TargetedFsmActionTypes.Contains(action.GetType())) {
                continue;
            }

            RetargetFsmActionGameObjectFields(action, approvedTarget);
        }
    }

    /// <summary>
    /// Rewrites target-specific <see cref="FsmGameObject"/> fields on a target-consuming FSM action to direct
    /// approved-target references, detached from shared FSM variables such as <c>Hero</c>.
    /// </summary>
    /// <param name="action">The active FSM action to retarget.</param>
    /// <param name="approvedTarget">The approved multiplayer target for the owning enemy.</param>
    private static void RetargetFsmActionGameObjectFields(FsmStateAction action, GameObject approvedTarget) {
        var fields = GetFsmGameObjectFields(action.GetType());

        foreach (var field in fields) {
            var fsmGameObject = field.GetValue(action) as FsmGameObject;
            if (fsmGameObject == null) {
                continue;
            }

            if (!CanRetargetFsmGameObjectField(action, field, fsmGameObject)) {
                continue;
            }

            ReplaceFsmGameObjectFieldWithDirectTarget(action, field, fsmGameObject, approvedTarget);
        }
    }

    /// <summary>
    /// Replaces a target-specific FSM action field with a direct approved-target reference.
    /// This prevents vanilla PlayMaker variables such as <c>Hero</c> from fighting the multiplayer retargeter.
    /// </summary>
    /// <param name="action">The action that owns the target field.</param>
    /// <param name="field">The reflected target field.</param>
    /// <param name="fsmGameObject">The current field value.</param>
    /// <param name="approvedTarget">The approved multiplayer target.</param>
    private static void ReplaceFsmGameObjectFieldWithDirectTarget(
        FsmStateAction action,
        FieldInfo field,
        FsmGameObject fsmGameObject,
        GameObject approvedTarget
    ) {
        var variableName = fsmGameObject.Name;

        if (!string.IsNullOrEmpty(variableName) &&
            variableName.Equals("Self", StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        var currentTarget = fsmGameObject.Value;

        if (currentTarget != null &&
            currentTarget != approvedTarget &&
            IsSameEnemyOwner(action.Fsm?.GameObject, currentTarget)) {
            return;
        }

        var isTargetVariable = !string.IsNullOrEmpty(variableName) && IsTargetVariableName(variableName);
        var isHeroLikeValue =
            currentTarget == null || currentTarget == approvedTarget || IsHeroLikeObject(currentTarget);

        if (!isTargetVariable && !isHeroLikeValue) {
            return;
        }

        field.SetValue(
            action, new FsmGameObject {
                Value = approvedTarget
            }
        );
    }

    /// <summary>
    /// Determines whether a reflected <see cref="FsmGameObject"/> field is a target field that may be rewritten.
    /// </summary>
    /// <param name="action">The action that owns the field.</param>
    /// <param name="field">The reflected FSM action field.</param>
    /// <param name="fsmGameObject">The field value.</param>
    /// <returns>
    /// <see langword="true"/> when the field is known to represent a target object; otherwise
    /// <see langword="false"/>.
    /// </returns>
    private static bool CanRetargetFsmGameObjectField(
        FsmStateAction action,
        FieldInfo field,
        FsmGameObject fsmGameObject
    ) {
        var variableName = fsmGameObject.Name;
        if (!string.IsNullOrEmpty(variableName)) {
            if (variableName.Equals("Self", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            if (IsTargetVariableName(variableName)) {
                return true;
            }
        }

        var actionType = action.GetType();

        if (actionType == typeof(FaceObject) || actionType == typeof(FaceObjectV2)) {
            return field.Name.Equals("objectB", StringComparison.OrdinalIgnoreCase) ||
                   field.Name.Equals("target", StringComparison.OrdinalIgnoreCase);
        }

        return field.Name.Equals("target", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Equals("targetObject", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Equals("targetGameObject", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Equals("targetObj", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Equals("gameObject", StringComparison.OrdinalIgnoreCase) &&
               IsHeroLikeObject(fsmGameObject.Value);
    }

    /// <summary>
    /// Determines whether an FSM variable name represents a player target reference.
    /// </summary>
    /// <param name="variableName">The FSM variable name.</param>
    /// <returns>
    /// <see langword="true"/> when the variable is safe to rewrite to an approved player target; otherwise
    /// <see langword="false"/>.
    /// </returns>
    private static bool IsTargetVariableName(string variableName) {
        return variableName.Equals("Hero", StringComparison.OrdinalIgnoreCase) ||
               variableName.Equals("Target", StringComparison.OrdinalIgnoreCase) ||
               variableName.Equals("Target Object", StringComparison.OrdinalIgnoreCase) ||
               variableName.Equals("TargetObject", StringComparison.OrdinalIgnoreCase) ||
               variableName.Equals("Player", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets all instance fields of an FSM action type that store <see cref="FsmGameObject"/> values.
    /// </summary>
    /// <param name="actionType">The FSM action type to inspect.</param>
    /// <returns>The reflected <see cref="FsmGameObject"/> fields for this action type.</returns>
    private static FieldInfo[] GetFsmGameObjectFields(Type actionType) {
        if (TargetedActionGameObjectFieldCache.TryGetValue(actionType, out var cachedFields)) {
            return cachedFields;
        }

        var fields = actionType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var fsmGameObjectFields = new List<FieldInfo>();

        foreach (var field in fields) {
            if (typeof(FsmGameObject).IsAssignableFrom(field.FieldType)) {
                fsmGameObjectFields.Add(field);
            }
        }

        var result = fsmGameObjectFields.ToArray();
        TargetedActionGameObjectFieldCache[actionType] = result;

        return result;
    }

    /// <summary>
    /// Determines whether two objects resolve to the same enemy target owner.
    /// </summary>
    /// <param name="left">The first object.</param>
    /// <param name="right">The second object.</param>
    /// <returns>
    /// <see langword="true"/> when both objects belong to the same enemy owner; otherwise
    /// <see langword="false"/>.
    /// </returns>
    private static bool IsSameEnemyOwner(GameObject? left, GameObject? right) {
        if (left == null || right == null) {
            return false;
        }

        var leftOwner = GetEnemyTargetOwner(left);
        var rightOwner = GetEnemyTargetOwner(right);

        return leftOwner != null && rightOwner != null && leftOwner == rightOwner;
    }

    /// <summary>
    /// Determines whether an enemy should transfer its approved target to a new candidate.
    /// </summary>
    /// <param name="owner">The enemy owner.</param>
    /// <param name="approvedTarget">The currently approved target.</param>
    /// <param name="candidateTarget">The candidate target found by the acquisition range.</param>
    /// <returns>
    /// <see langword="true"/> when the candidate is clearly closer than the current approved target; otherwise
    /// <see langword="false"/>.
    /// </returns>
    private static bool ShouldSwitchApprovedTarget(
        GameObject owner,
        GameObject approvedTarget,
        GameObject candidateTarget
    ) {
        if (approvedTarget == candidateTarget) {
            return false;
        }

        if (!IsHeroLikeObject(approvedTarget)) {
            return true;
        }

        var ownerPosition = (Vector2) owner.transform.position;
        var approvedDistance = ((Vector2) approvedTarget.transform.position - ownerPosition).sqrMagnitude;
        var candidateDistance = ((Vector2) candidateTarget.transform.position - ownerPosition).sqrMagnitude;

        return candidateDistance + TargetSwitchDistanceBias < approvedDistance;
    }

    /// <summary>
    /// Updates a cached target field on all active behaviour instances of the given type.
    /// </summary>
    /// <typeparam name="TComponent">The behaviour type containing the cached target field.</typeparam>
    /// <param name="field">The field that stores the cached target.</param>
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

            var target = GetApprovedEnemyTarget(component.gameObject);
            if (target == null) {
                continue;
            }

            if (field.FieldType == typeof(Transform)) {
                field.SetValue(component, target.transform);
            } else if (field.FieldType == typeof(GameObject)) {
                field.SetValue(component, target);
            }
        }
    }

    /// <summary>
    /// Resolves the enemy object that owns targeting state for the supplied object.
    /// </summary>
    /// <param name="obj">An object inside an enemy hierarchy.</param>
    /// <returns>The enemy object that should own target state.</returns>
    /// <remarks>
    /// Enemy targeting hooks can run from child objects such as alert ranges, line-of-sight detectors, FSM objects,
    /// or movement controllers. This method normalizes those callers back to the actual enemy instance so target state
    /// is stored per mob instead of accidentally shared through a scene/root container.
    /// </remarks>
    private static GameObject GetEnemyTargetOwner(GameObject obj) {
        var healthManager = obj.GetComponentInParent<HealthManager>();
        if (healthManager != null) {
            return healthManager.gameObject;
        }

        var walker = obj.GetComponentInParent<Walker>();
        if (walker != null) {
            return walker.gameObject;
        }

        var walkerV2 = obj.GetComponentInParent<WalkerV2>();
        if (walkerV2 != null) {
            return walkerV2.gameObject;
        }

        var fsm = obj.GetComponentInParent<PlayMakerFSM>();
        return fsm != null ? fsm.gameObject : obj;
    }
}

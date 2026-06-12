using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using MonoMod.RuntimeDetour;
using SSMP.Game.Client.Entity.Action;
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
    /// Cached resolved enemy target owner per GameObject instance ID to avoid deep transform parent lookups.
    /// </summary>
    private static readonly Dictionary<int, GameObject> TargetOwnerCache = new();

    /// <summary>
    /// Searches active assemblies to find a PlayMaker FSM action type matching the given class name.
    /// </summary>
    /// <param name="name">The name of the action class.</param>
    /// <returns>The resolved action <see cref="Type"/>, or null if not found.</returns>
    private static Type? GetFsmActionTypeByName(string name) {
        var actionAssembly = typeof(GetHero).Assembly;
        var type = actionAssembly.GetType($"HutongGames.PlayMaker.Actions.{name}");
        if (type != null) return type;

        var gameAssembly = typeof(Walker).Assembly;
        type = gameAssembly.GetType($"HutongGames.PlayMaker.Actions.{name}");
        if (type != null) return type;

        type = gameAssembly.GetType(name);
        if (type != null) return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            type = assembly.GetType(name) ?? assembly.GetType($"HutongGames.PlayMaker.Actions.{name}");
            if (type != null) return type;
        }

        return null;
    }

    /// <summary>
    /// Cached reflected <see cref="FsmGameObject"/> fields for target-consuming FSM action types.
    /// </summary>
    private static readonly Dictionary<Type, FieldInfo[]> TargetedActionGameObjectFieldCache = new();

    /// <summary>
    /// Reflected private field storing the cached hero transform or controller used by <see cref="Walker"/>.
    /// </summary>
    private static readonly FieldInfo? WalkerHeroField =
        typeof(Walker).GetField("hero", InstanceNonPublicFlags);

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

    private static readonly Func<Walker, HeroController?>? GetWalkerHero =
        CompileGetter<HeroController?>(WalkerHeroField);

    /// <summary>
    /// Compiles a dynamic lambda delegate for retrieving the value of a private field on a <see cref="Walker"/> instance.
    /// </summary>
    /// <typeparam name="TField">The expected return type of the field.</typeparam>
    /// <param name="field">The reflected <see cref="FieldInfo"/> to read.</param>
    /// <returns>A compiled getter delegate, or null if compile fails.</returns>
    private static Func<Walker, TField>? CompileGetter<TField>(FieldInfo? field) {
        if (field == null) return null;
        try {
            var param = Expression.Parameter(typeof(Walker), "self");
            var fieldExp = Expression.Field(param, field);
            return Expression.Lambda<Func<Walker, TField>>(fieldExp, param).Compile();
        } catch (Exception e) {
            Logger.Error($"Failed to compile getter for field {field.Name}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Hook for replacing <see cref="Walker"/> walking updates with multiplayer-aware target handling.
    /// </summary>
    private ILHook? _walkerUpdateWalkingHook;

    /// <summary>
    /// Hook for replacing <see cref="Walker"/> activation-condition updates with multiplayer-aware target handling.
    /// </summary>
    private ILHook? _walkerUpdateWaitingForConditionsHook;

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
    private static GameObject? GetApprovedEnemyTarget(GameObject? requester) {
        if (requester == null) {
            return null;
        }

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
        _walkerUpdateWalkingHook = new ILHook(
            typeof(Walker).GetMethod("UpdateWalking", InstanceNonPublicFlags)!,
            ILWalkerUpdateWalking
        );

        _walkerUpdateWaitingForConditionsHook = new ILHook(
            typeof(Walker).GetMethod("UpdateWaitingForConditions", InstanceNonPublicFlags)!,
            ILWalkerUpdateWaitingForConditions
        );

        RegisterTargetedFsmActionEnterHooks();

        MonoBehaviourUtil.Instance.OnUpdateEvent += OnUpdateRetargetEnemyTransforms;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
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

        UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        ClearTargetCaches();
    }

    /// <summary>
    /// Clears the active target caches when the current game scene changes.
    /// </summary>
    /// <param name="from">The previous scene.</param>
    /// <param name="to">The newly loaded active scene.</param>
    private static void OnActiveSceneChanged(
        UnityEngine.SceneManagement.Scene from,
        UnityEngine.SceneManagement.Scene to
    ) {
        ClearTargetCaches();
    }

    /// <summary>
    /// Clears the approved target maps and the enemy target owner resolution caches.
    /// </summary>
    private static void ClearTargetCaches() {
        EnemyApprovedTargets.Clear();
        TargetOwnerCache.Clear();
    }

    /// <summary>
    /// Registers RuntimeDetour hooks for target-consuming FSM action OnEnter methods.
    /// </summary>
    private void RegisterTargetedFsmActionEnterHooks() {
        DisposeTargetedFsmActionEnterHooks();

        foreach (var actionName in ActionRegistry.TargetedFsmActionTypes) {
            var actionType = GetFsmActionTypeByName(actionName);
            if (actionType == null) {
                continue;
            }

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
    /// Resolves the approved target transform for a <see cref="Walker"/> instance.
    /// Falls back to the vanilla target field when no approved multiplayer target exists.
    /// </summary>
    /// <param name="self">The walker being queried.</param>
    /// <returns>The transform of the target, or null if none is available.</returns>
    private static Transform? GetApprovedEnemyTargetTransform(Walker self) {
        var approvedTarget = GetApprovedEnemyTarget(self.gameObject);
        if (approvedTarget != null) {
            return approvedTarget.transform;
        }

        var hero = GetWalkerHero?.Invoke(self) ?? (HeroController?) WalkerHeroField?.GetValue(self);
        return hero != null ? hero.transform : null;
    }

    /// <summary>
    /// IL hook that rewrites hero field reads in <see cref="Walker.UpdateWaitingForConditions"/>
    /// to use the approved multiplayer target instead.
    /// </summary>
    /// <param name="il">The IL context to modify.</param>
    private static void ILWalkerUpdateWaitingForConditions(ILContext il) {
        try {
            var c = new ILCursor(il);

            while (c.TryGotoNext(
                       MoveType.Before,
                       i => i.MatchLdarg(0),
                       i => i.MatchLdfld(typeof(Walker), "hero")
                   )) {
                if (c.Next.Next.Next != null &&
                    c.Next.Next.Next.MatchCallvirt(typeof(Component), "get_transform")) {
                    c.RemoveRange(3);
                    c.Emit(OpCodes.Ldarg_0);
                    c.Emit(
                        OpCodes.Call,
                        typeof(GamePatcher).GetMethod(
                            nameof(GetApprovedEnemyTargetTransform), BindingFlags.NonPublic | BindingFlags.Static
                        )!
                    );
                } else {
                    c.RemoveRange(2);
                    c.Emit(OpCodes.Ldarg_0);
                    c.Emit(
                        OpCodes.Call,
                        typeof(GamePatcher).GetMethod(
                            nameof(GetApprovedEnemyTargetTransform), BindingFlags.NonPublic | BindingFlags.Static
                        )!
                    );
                }
            }
        } catch (Exception e) {
            Logger.Error($"Could not apply ILWalkerUpdateWaitingForConditions hook: {e}");
        }
    }

    /// <summary>
    /// IL hook that rewrites hero field reads in <see cref="Walker.UpdateWalking"/>
    /// to use the approved multiplayer target instead.
    /// </summary>
    /// <param name="il">The IL context to modify.</param>
    private static void ILWalkerUpdateWalking(ILContext il) {
        try {
            var c = new ILCursor(il);

            while (c.TryGotoNext(
                       MoveType.Before,
                       i => i.MatchLdarg(0),
                       i => i.MatchLdfld(typeof(Walker), "hero")
                   )) {
                if (c.Next.Next.Next != null &&
                    c.Next.Next.Next.MatchCallvirt(typeof(Component), "get_transform")) {
                    c.RemoveRange(3);
                    c.Emit(OpCodes.Ldarg_0);
                    c.Emit(
                        OpCodes.Call,
                        typeof(GamePatcher).GetMethod(
                            nameof(GetApprovedEnemyTargetTransform), BindingFlags.NonPublic | BindingFlags.Static
                        )!
                    );
                } else {
                    c.RemoveRange(2);
                    c.Emit(OpCodes.Ldarg_0);
                    c.Emit(
                        OpCodes.Call,
                        typeof(GamePatcher).GetMethod(
                            nameof(GetApprovedEnemyTargetTransform), BindingFlags.NonPublic | BindingFlags.Static
                        )!
                    );
                }
            }
        } catch (Exception e) {
            Logger.Error($"Could not apply ILWalkerUpdateWalking hook: {e}");
        }
    }

    /// <summary>
    /// Refreshes enemy target references that need to follow the approved multiplayer target.
    /// </summary>
    private void OnUpdateRetargetEnemyTransforms() {
        if (Time.unscaledTime < _nextEnemyRetargetTime) {
            return;
        }

        _nextEnemyRetargetTime = Time.unscaledTime + EnemyRetargetIntervalSeconds;

        UpdateTargetedFsmActions();
        UpdateCachedTargetField<WalkerV2>(WalkerV2HeroField);
        UpdateCachedTargetField<ScuttlerControl>(ScuttlerControlHeroField);
    }

    /// <summary>
    /// Forces an immediate retargeting update for all targeted FSM actions and components.
    /// </summary>
    public static void ForceImmediateRetarget() {
        UpdateTargetedFsmActions();
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
        if (_entityManagerInstance == null) {
            return;
        }

        foreach (var entity in _entityManagerInstance.ActiveEntities) {
            if (entity == null) {
                continue;
            }

            var hostFsms = entity.HostFsms;
            foreach (var fsm in hostFsms) {
                RetargetActiveTargetedFsmActions(fsm);
            }

            var clientFsms = entity.ClientFsms;
            foreach (var fsm in clientFsms) {
                RetargetActiveTargetedFsmActions(fsm);
            }
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

            if (!ActionRegistry.TargetedFsmActionTypes.Contains(action.GetType().Name)) {
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

        if (currentTarget == approvedTarget && string.IsNullOrEmpty(variableName)) {
            return;
        }

        if (currentTarget != null &&
            currentTarget != approvedTarget &&
            IsSameEnemyOwner(action.Fsm?.GameObject, currentTarget)) {
            return;
        }

        var isTargetVariable = !string.IsNullOrEmpty(variableName) && IsTargetVariableName(variableName);
        var isHeroLikeValue =
            currentTarget == null || currentTarget == approvedTarget ||
            PlayerTargetRegistry.IsHeroLikeObject(currentTarget);

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
               PlayerTargetRegistry.IsHeroLikeObject(fsmGameObject.Value);
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

        if (!PlayerTargetRegistry.IsHeroLikeObject(approvedTarget)) {
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
        if (field == null || _entityManagerInstance == null) {
            return;
        }

        foreach (var entity in _entityManagerInstance.ActiveEntities) {
            if (entity == null) {
                continue;
            }

            if (entity.Object.Host != null) {
                var component = entity.Object.Host.GetComponentInChildren<TComponent>();
                if (component != null && component.isActiveAndEnabled) {
                    UpdateComponentTarget(component, field);
                }
            }

            if (entity.Object.Client != null) {
                var component = entity.Object.Client.GetComponentInChildren<TComponent>();
                if (component != null && component.isActiveAndEnabled) {
                    UpdateComponentTarget(component, field);
                }
            }
        }
    }

    /// <summary>
    /// Updates the target reference of a component instance with the approved enemy target.
    /// </summary>
    /// <typeparam name="TComponent">The behaviour type containing the cached target field.</typeparam>
    /// <param name="component">The component instance to update.</param>
    /// <param name="field">The target field to write the approved target to.</param>
    private static void UpdateComponentTarget<TComponent>(TComponent component, FieldInfo field)
        where TComponent : Behaviour {
        var target = GetApprovedEnemyTarget(component.gameObject);
        if (target == null) {
            return;
        }

        if (field.FieldType == typeof(Transform)) {
            field.SetValue(component, target.transform);
        } else if (field.FieldType == typeof(GameObject)) {
            field.SetValue(component, target);
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
    private static GameObject? GetEnemyTargetOwner(GameObject obj) {
        if (obj == null) {
            return obj;
        }

        var instanceId = obj.GetInstanceID();
        if (TargetOwnerCache.TryGetValue(instanceId, out var cachedOwner) && cachedOwner != null) {
            return cachedOwner;
        }

        GameObject resolvedOwner;
        var healthManager = obj.GetComponentInParent<HealthManager>();
        if (healthManager != null) {
            resolvedOwner = healthManager.gameObject;
        } else {
            var walker = obj.GetComponentInParent<Walker>();
            if (walker != null) {
                resolvedOwner = walker.gameObject;
            } else {
                var walkerV2 = obj.GetComponentInParent<WalkerV2>();
                if (walkerV2 != null) {
                    resolvedOwner = walkerV2.gameObject;
                } else {
                    var fsm = obj.GetComponentInParent<PlayMakerFSM>();
                    resolvedOwner = fsm != null ? fsm.gameObject : obj;
                }
            }
        }

        TargetOwnerCache[instanceId] = resolvedOwner;
        return resolvedOwner;
    }

    /// <summary>
    /// Removes cached targeting data for an entity GameObject that is being destroyed.
    /// </summary>
    public static void OnEntityDestroyed(GameObject? obj) {
        if (obj == null) return;
        var instanceId = obj.GetInstanceID();
        EnemyApprovedTargets.Remove(instanceId);
        TargetOwnerCache.Remove(instanceId);
    }
}

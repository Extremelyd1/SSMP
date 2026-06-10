using System;
using System.Linq;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace SSMP.Game.Client;

/// <summary>
/// Contains multiplayer-aware player detection patches for enemy AI and PlayMaker actions.
/// </summary>
internal partial class GamePatcher {
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

    private Hook? _getHeroOnEnterHook;
    private Hook? _alertRangeIsHeroInRangeHook;
    private Hook? _lineOfSightDetectorUpdateHook;
    private const float TargetSwitchDistanceBias = 4f;

    /// <summary>
    /// Registers detection-specific hooks.
    /// </summary>
    private void RegisterDetectionHooks() {
        _getHeroOnEnterHook = new Hook(
            typeof(GetHero).GetMethod("OnEnter", InstancePublicFlags),
            OnGetHeroOnEnter
        );

        _alertRangeIsHeroInRangeHook = new Hook(
            typeof(AlertRange).GetMethod("IsHeroInRange", InstancePublicFlags | InstanceNonPublicFlags)!,
            OnAlertRangeIsHeroInRange
        );

        _lineOfSightDetectorUpdateHook = new Hook(
            typeof(LineOfSightDetector).GetMethod("Update", InstancePublicFlags | InstanceNonPublicFlags)!,
            OnLineOfSightDetectorUpdate
        );
    }

    /// <summary>
    /// Disposes detection-specific hooks.
    /// </summary>
    private void DisposeDetectionHooks() {
        _getHeroOnEnterHook?.Dispose();
        _getHeroOnEnterHook = null;

        _alertRangeIsHeroInRangeHook?.Dispose();
        _alertRangeIsHeroInRangeHook = null;

        _lineOfSightDetectorUpdateHook?.Dispose();
        _lineOfSightDetectorUpdateHook = null;
    }

    /// <summary>
    /// Overrides the PlayMaker <see cref="GetHero"/> action to resolve the enemy-approved tracked player when possible.
    /// </summary>
    /// <param name="orig">The original method.</param>
    /// <param name="self">The action instance being executed.</param>
    private static void OnGetHeroOnEnter(Action<GetHero> orig, GetHero self) {
        if (self.storeResult == null) {
            orig(self);
            return;
        }

        var requester = self.Fsm?.GameObject;
        if (requester != null && requester.GetComponentInParent<HealthManager>() != null) {
            var approvedTarget = GetApprovedEnemyTarget(requester);
            if (approvedTarget == null) {
                orig(self);
                return;
            }

            self.storeResult.Value = approvedTarget;
            self.Finish();
            return;
        }

        orig(self);
    }

    /// <summary>
    /// Replaces the hero-only alert-range check with tracked-player evaluation scoped to this alert range.
    /// </summary>
    /// <param name="self">The alert range being queried.</param>
    /// <returns>True when the relevant tracked player is inside this alert range and visible; otherwise false.</returns>
    private static bool OnAlertRangeIsHeroInRange(AlertRange self) {
        var owner = GetEnemyTargetOwner(self.gameObject);
        var approvedTarget = GetApprovedEnemyTarget(owner);

        // Chomp / Attack / Hit / Unalert ranges must never choose a new target.
        // They only check the already-approved target.
        if (!CanAcquireMultiplayerTarget(self)) {
            if (approvedTarget == null) {
                return false;
            }

            return IsPlayerInsideAlertRange(self, approvedTarget) &&
                   HasLineOfSightToAlertRangeTarget(self, approvedTarget);
        }

        // Main acquisition range:
        // If we already have a valid target, KEEP IT. Do not nearest-player swap every frame.
        if (approvedTarget != null &&
            IsPlayerInsideAlertRange(self, approvedTarget) &&
            HasLineOfSightToAlertRangeTarget(self, approvedTarget)) {
            return true;
        }

        // Only acquire a new target when the old one is missing or invalid.
        var newTarget = GetNearestPlayerInsideAlertRange(self);
        if (newTarget == null) {
            return approvedTarget != null &&
                   IsPlayerInsideAlertRange(self, approvedTarget) &&
                   HasLineOfSightToAlertRangeTarget(self, approvedTarget);
        }

        if (!HasLineOfSightToAlertRangeTarget(self, newTarget)) {
            return approvedTarget != null &&
                   IsPlayerInsideAlertRange(self, approvedTarget) &&
                   HasLineOfSightToAlertRangeTarget(self, approvedTarget);
        }

        if (approvedTarget == null || ShouldSwitchApprovedTarget(owner, approvedTarget, newTarget)) {
            ApproveEnemyTarget(owner, newTarget);
            return true;
        }

        return IsPlayerInsideAlertRange(self, approvedTarget) &&
               HasLineOfSightToAlertRangeTarget(self, approvedTarget);
    }
    
    private static bool ShouldSwitchApprovedTarget(GameObject owner, GameObject approvedTarget, GameObject candidateTarget) {
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
    /// Recomputes line-of-sight against the enemy-approved tracked player instead of the local hero singleton.
    /// </summary>
    /// <param name="self">The detector being updated.</param>
    private static void OnLineOfSightDetectorUpdate(LineOfSightDetector self) {
        var target = GetNearestVisiblePlayer(self);
        var canSeeHero = false;

        if (target != null) {
            var origin = (Vector2) self.transform.position;
            var targetPosition = (Vector2) GetTargetAimPosition(target);
            var direction = targetPosition - origin;

            canSeeHero = direction.sqrMagnitude > 0f &&
                         !Helper.Raycast2D(origin, direction.normalized, direction.magnitude, TerrainLayerMask);

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
    /// Finds the approved visible tracked player for a line-of-sight detector without acquiring a new target.
    /// </summary>
    /// <param name="detector">The detector requesting a target.</param>
    /// <returns>The approved target if it is still inside one of the detector's alert ranges; otherwise null.</returns>
    private static GameObject? GetNearestVisiblePlayer(LineOfSightDetector detector) {
        var approvedTarget = GetApprovedEnemyTarget(detector.gameObject);
        if (approvedTarget == null) {
            return null;
        }

        if (LineOfSightDetectorAlertRangesField?.GetValue(detector) is not AlertRange[] alertRanges ||
            alertRanges.Length == 0) {
            return approvedTarget;
        }

        foreach (var alertRange in alertRanges) {
            if (alertRange == null) {
                continue;
            }

            if (IsPlayerInsideAlertRange(alertRange, approvedTarget)) {
                return approvedTarget;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the nearest tracked player that is physically inside the supplied alert range.
    /// </summary>
    /// <param name="alertRange">The alert range to test.</param>
    /// <returns>The nearest valid player inside the alert range, or <see langword="null"/>.</returns>
    private static GameObject? GetNearestPlayerInsideAlertRange(AlertRange alertRange) {
        GameObject? bestPlayer = null;
        var bestDistance = float.PositiveInfinity;

        foreach (var player in PlayerTargetRegistry.GetTrackedPlayers()) {
            if (player == null) {
                continue;
            }

            if (!IsPlayerInsideAlertRange(alertRange, player)) {
                continue;
            }

            var distance = ((Vector2) alertRange.transform.position - (Vector2) player.transform.position).sqrMagnitude;
            if (distance >= bestDistance) {
                continue;
            }

            bestDistance = distance;
            bestPlayer = player;
        }

        return bestPlayer;
    }

    /// <summary>
    /// Determines whether the supplied object represents a vanilla hero, local player, remote tracked player,
    /// or another player-like target that should be eligible for retargeting.
    /// </summary>
    /// <param name="obj">The object to inspect.</param>
    /// <returns>
    /// <see langword="true"/> when the object appears to be a hero/player target reference; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// This is used to safely replace cached single-player hero references inside FSM variables and vanilla components
    /// with the approved tracked multiplayer player.
    /// </remarks>
    private static bool IsHeroLikeObject(GameObject? obj) {
        if (obj == null) {
            return false;
        }

        return obj.name == "Hero_Hornet(Clone)" ||
               obj.name == "Hero_Hornet" ||
               obj.name == "Player Prefab" ||
               obj.CompareTag("Player") ||
               obj.GetComponent<HeroController>() != null ||
               PlayerTargetRegistry.GetTrackedPlayerRoot(obj) != null;
    }

    /// <summary>
    /// Determines whether a tracked player should be considered inside the supplied alert range.
    /// </summary>
    /// <param name="alertRange">The alert range to test against.</param>
    /// <param name="player">The tracked player object to test.</param>
    /// <returns>
    /// <see langword="true"/> when the player is already tracked by the vanilla alert range or one of its colliders
    /// overlaps the alert range collider; otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsPlayerInsideAlertRange(AlertRange alertRange, GameObject player) {
        if (alertRange.InsideGameObjects.Contains(player)) {
            return true;
        }

        var alertCollider = alertRange.GetComponent<Collider2D>();
        if (alertCollider == null || !alertCollider.enabled) {
            return false;
        }

        foreach (var playerCollider in player.GetComponentsInChildren<Collider2D>()) {
            if (playerCollider == null || !playerCollider.enabled) {
                continue;
            }

            if (alertCollider.bounds.Intersects(playerCollider.bounds)) {
                return true;
            }
        }

        return alertCollider.OverlapPoint(player.transform.position);
    }

    /// <summary>
    /// Gets the best available target position for sight checks.
    /// </summary>
    /// <param name="target">The target object to inspect.</param>
    /// <returns>The center of the target collider when available; otherwise the target transform position.</returns>
    private static Vector3 GetTargetAimPosition(GameObject target) {
        var collider = target.GetComponentInChildren<Collider2D>();
        return collider != null ? collider.bounds.center : target.transform.position;
    }

    /// <summary>
    /// Determines whether an alert range is allowed to acquire and approve a new multiplayer target.
    /// </summary>
    /// <param name="alertRange">The alert range to inspect.</param>
    /// <returns>
    /// <see langword="true"/> when the range belongs to an enemy detection setup and may acquire a target;
    /// otherwise <see langword="false"/>.
    /// </returns>
    private static bool CanAcquireMultiplayerTarget(AlertRange alertRange) {
        if (alertRange == null || !alertRange.isActiveAndEnabled) {
            return false;
        }

        if (alertRange.GetComponentInParent<HealthManager>() == null) {
            return false;
        }

        var rangeName = alertRange.name;

        return !rangeName.Contains("Chomp") &&
               !rangeName.Contains("Unalert") &&
               !rangeName.Contains("Attack") &&
               !rangeName.Contains("Hit");
    }

    /// <summary>
    /// Determines whether the alert range has line of sight to a target when line-of-sight checks are enabled.
    /// </summary>
    /// <param name="alertRange">The alert range performing the check.</param>
    /// <param name="target">The target to check visibility against.</param>
    /// <returns>
    /// <see langword="true"/> when line of sight is not required or the target is visible; otherwise
    /// <see langword="false"/>.
    /// </returns>
    private static bool HasLineOfSightToAlertRangeTarget(AlertRange alertRange, GameObject target) {
        if (!alertRange.ChecksLineOfSight) {
            return true;
        }

        var origin = GetAlertRangeLineOfSightOrigin(alertRange);
        if (origin == null) {
            return false;
        }

        var targetPosition = GetTargetAimPosition(target);
        return !Helper.LineCast2DHit(origin.position, targetPosition, TerrainLayerMask, out _);
    }
}

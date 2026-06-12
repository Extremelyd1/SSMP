using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.RuntimeDetour;
using SSMP.Game.Client.Entity;
using UnityEngine;

namespace SSMP.Game.Client;

/// <summary>
/// Contains multiplayer-aware player detection patches for enemy AI and PlayMaker actions.
/// </summary>
internal partial class GamePatcher {
    /// <summary>
    /// AlertRange line-of-sight mode value that uses the alert range object's own transform as the sight origin.
    /// </summary>
    private const int AlertRangeLineOfSightSelf = 1;

    /// <summary>
    /// AlertRange line-of-sight mode value that uses the alert range object's parent or initial parent as the sight origin.
    /// </summary>
    private const int AlertRangeLineOfSightParent = 2;

    /// <summary>
    /// Unity layer mask used for terrain obstruction checks during enemy line-of-sight tests.
    /// </summary>
    private const int TerrainLayerMask = 256;

    /// <summary>
    /// Reflected private field storing the AlertRange line-of-sight mode.
    /// </summary>
    private static readonly FieldInfo? AlertRangeLineOfSightField =
        typeof(AlertRange).GetField("lineOfSight", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the original parent transform used by AlertRange line-of-sight checks.
    /// </summary>
    private static readonly FieldInfo? AlertRangeInitialParentField =
        typeof(AlertRange).GetField("initialParent", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing the alert ranges tracked by a LineOfSightDetector.
    /// </summary>
    private static readonly FieldInfo? LineOfSightDetectorAlertRangesField =
        typeof(LineOfSightDetector).GetField("alertRanges", InstanceNonPublicFlags);

    /// <summary>
    /// Reflected private field storing whether a LineOfSightDetector can currently see the hero.
    /// </summary>
    private static readonly FieldInfo? LineOfSightDetectorCanSeeHeroField =
        typeof(LineOfSightDetector).GetField("canSeeHero", InstanceNonPublicFlags);

    /// <summary>
    /// Hook for overriding the PlayMaker GetHero action with the enemy-approved multiplayer target.
    /// </summary>
    private Hook? _getHeroOnEnterHook;

    /// <summary>
    /// Hook for replacing AlertRange hero checks with multiplayer-aware approved-target checks.
    /// </summary>
    private Hook? _alertRangeIsHeroInRangeHook;

    /// <summary>
    /// Hook for replacing LineOfSightDetector visibility updates with approved-target visibility checks.
    /// </summary>
    private Hook? _lineOfSightDetectorUpdateHook;

    /// <summary>
    /// Alert range name fragments that identify ranges which may validate an existing approved target,
    /// but must not acquire or switch to a new target.
    /// </summary>
    private static readonly string[] NonAcquiringAlertRangeNameParts = [
        "Chomp",
        "Unalert",
        "Attack",
        "Hit"
    ];

    /// <summary>
    /// Cache mapping AlertRange instances to their non-acquiring status to avoid native string name allocations.
    /// </summary>
    private static readonly ConditionalWeakTable<AlertRange, BoxedBool> NonAcquiringAlertRanges = [];

    /// <summary>
    /// Cache mapping AlertRange instances to their CachedCollider to avoid frame-by-frame GetComponent allocations.
    /// Uses ConditionalWeakTable to prevent memory leaks from keeping alive AlertRange instances that should be GC'ed.
    /// </summary>
    private static readonly ConditionalWeakTable<AlertRange, CachedCollider> AlertColliderCache = [];

    /// <summary>
    /// Cache mapping player GameObjects to their CachedPlayerInfo to avoid frame-by-frame native calls.
    /// Uses ConditionalWeakTable to prevent memory leaks from keeping alive player GameObjects.
    /// </summary>
    private static readonly ConditionalWeakTable<GameObject, CachedPlayerInfo> PlayerInfoCache = [];

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
        if (owner == null) {
            return false;
        }

        var approvedTarget = GetApprovedEnemyTarget(owner);

        if (!CanAcquireMultiplayerTarget(self)) {
            return IsValidTargetForAlertRange(self, approvedTarget);
        }

        var candidateTarget = GetNearestPlayerInsideAlertRange(self);
        if (candidateTarget != null &&
            HasLineOfSightToAlertRangeTarget(self, candidateTarget) &&
            (approvedTarget == null || ShouldSwitchApprovedTarget(owner, approvedTarget, candidateTarget))) {
            ApproveEnemyTarget(owner, candidateTarget);
            return true;
        }

        return IsValidTargetForAlertRange(self, approvedTarget);
    }

    /// <summary>
    /// Determines whether a previously approved target is still valid for a specific alert range.
    /// </summary>
    /// <param name="alertRange">The alert range to validate against.</param>
    /// <param name="target">The approved target to validate.</param>
    /// <returns>
    /// <see langword="true"/> when the target is inside the alert range and visible; otherwise
    /// <see langword="false"/>.
    /// </returns>
    private static bool IsValidTargetForAlertRange(AlertRange alertRange, GameObject? target) {
        return target != null &&
               IsPlayerInsideAlertRange(alertRange, target) &&
               HasLineOfSightToAlertRangeTarget(alertRange, target);
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

        var players = PlayerTargetRegistry.GetTrackedPlayers();
        foreach (var player in players) {
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

        if (!AlertColliderCache.TryGetValue(alertRange, out var cached)) {
            cached = new CachedCollider { Collider = alertRange.GetComponent<Collider2D>() };
            AlertColliderCache.Add(alertRange, cached);
        }

        var alertCollider = cached.Collider;
        if (alertCollider == null || !alertCollider.enabled) {
            return false;
        }

        var currentFrame = Time.frameCount;

        // Cache alert collider bounds per frame
        if (cached.FrameCount != currentFrame) {
            cached.FrameCount = currentFrame;
            cached.Bounds = alertCollider.bounds;
        }

        var alertBounds = cached.Bounds;

        // Retrieve or create cached player info
        if (!PlayerInfoCache.TryGetValue(player, out var playerInfo)) {
            playerInfo = new CachedPlayerInfo();
            PlayerInfoCache.Add(player, playerInfo);
        }

        // Cache player bounds and position per frame
        if (playerInfo.FrameCount != currentFrame) {
            playerInfo.FrameCount = currentFrame;
            playerInfo.Position = player.transform.position;

            var colliders = PlayerTargetRegistry.GetPlayerColliders(player);
            if (playerInfo.ColliderBounds.Length != colliders.Length) {
                playerInfo.ColliderBounds = new Bounds[colliders.Length];
            }

            for (int i = 0; i < colliders.Length; i++) {
                var col = colliders[i];
                if (col != null && col.enabled) {
                    playerInfo.ColliderBounds[i] = col.bounds;
                } else {
                    playerInfo.ColliderBounds[i] = default;
                }
            }
        }

        // Check collider intersections using cached bounds in 2D
        foreach (var playerBounds in playerInfo.ColliderBounds) {
            if (playerBounds.extents is { x: 0f, y: 0f }) {
                continue;
            }

            // 2D box intersection check (ignores Z)
            if (alertBounds.min.x <= playerBounds.max.x && alertBounds.max.x >= playerBounds.min.x &&
                alertBounds.min.y <= playerBounds.max.y && alertBounds.max.y >= playerBounds.min.y) {
                return true;
            }
        }

        // Fallback check (only if player position is inside the alert bounds in 2D)
        var playerPos = playerInfo.Position;
        if (playerPos.x >= alertBounds.min.x && playerPos.x <= alertBounds.max.x &&
            playerPos.y >= alertBounds.min.y && playerPos.y <= alertBounds.max.y) {
            return alertCollider.OverlapPoint(playerPos);
        }

        return false;
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

        if (NonAcquiringAlertRanges.TryGetValue(alertRange, out var boxed)) {
            return !boxed.Value;
        }

        var owner = GetEnemyTargetOwner(alertRange.gameObject);
        EntityRegistryEntry? entry = null;
        if (owner != null) {
            EntityRegistry.TryGetEntry(owner, out entry);
        }

        var isNonAcquiring = false;
        if (entry is { NonAcquiringRanges: not null }) {
            foreach (var blockedNamePart in entry.NonAcquiringRanges) {
                if (alertRange.name.Contains(blockedNamePart, StringComparison.OrdinalIgnoreCase)) {
                    isNonAcquiring = true;
                    break;
                }
            }
        } else {
            isNonAcquiring = IsNonAcquiringAlertRangeName(alertRange.name);
        }

        NonAcquiringAlertRanges.Add(alertRange, new BoxedBool { Value = isNonAcquiring });

        return !isNonAcquiring;
    }

    /// <summary>
    /// Determines whether an alert range name belongs to a range that should only validate an existing target,
    /// not acquire a new one.
    /// </summary>
    /// <param name="rangeName">The alert range object name.</param>
    /// <returns>
    /// <see langword="true"/> when the range should not acquire a new multiplayer target; otherwise
    /// <see langword="false"/>.
    /// </returns>
    private static bool IsNonAcquiringAlertRangeName(string rangeName) {
        foreach (var blockedNamePart in NonAcquiringAlertRangeNameParts) {
            if (rangeName.Contains(blockedNamePart, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
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

/// <summary>
/// A wrapper class for boolean values to allow storing them in a ConditionalWeakTable.
/// </summary>
internal class BoxedBool {
    /// <summary>
    /// The wrapped boolean value.
    /// </summary>
    public bool Value;
}

/// <summary>
/// A wrapper class for Collider2D references to allow caching them in a ConditionalWeakTable.
/// </summary>
internal class CachedCollider {
    /// <summary>
    /// The cached Collider2D reference, which may be null if none is attached.
    /// </summary>
    public Collider2D? Collider;

    /// <summary>
    /// The frame number when the bounds were last updated.
    /// </summary>
    public int FrameCount = -1;

    /// <summary>
    /// The cached bounds of the collider.
    /// </summary>
    public Bounds Bounds;
}

/// <summary>
/// A cache container for player bounds and position to avoid native property access calls within a frame.
/// </summary>
internal class CachedPlayerInfo {
    /// <summary>
    /// The frame number when the player info was last cached.
    /// </summary>
    public int FrameCount = -1;

    /// <summary>
    /// The cached bounds of the player's colliders.
    /// </summary>
    public Bounds[] ColliderBounds = [];

    /// <summary>
    /// The cached transform position of the player.
    /// </summary>
    public Vector3 Position;
}

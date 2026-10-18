using System.Collections.Generic;
using UnityEngine;

namespace SSMP.Game.Client;

/// <summary>
/// Tracks remote player objects that can be targeted by host-side enemy FSMs.
/// </summary>
internal static class PlayerTargetRegistry {
    private static readonly HashSet<GameObject> RemotePlayerObjects = [];
    private static readonly List<GameObject> TrackedPlayersCache = [];

    /// <summary>
    /// Cached collider arrays per remote player root GameObject to avoid dynamic allocations.
    /// </summary>
    private static readonly Dictionary<GameObject, Collider2D[]> PlayerCollidersCache = new();

    /// <summary>
    /// Registers a spawned remote player root object for targeting.
    /// </summary>
    /// <param name="playerObject">The remote player root object.</param>
    public static void RegisterRemotePlayer(GameObject playerObject) {
        RemotePlayerObjects.Add(playerObject);
        PlayerCollidersCache[playerObject] = playerObject.GetComponentsInChildren<Collider2D>(true);
    }

    /// <summary>
    /// Removes a remote player root object from targeting.
    /// </summary>
    /// <param name="playerObject">The remote player root object to remove.</param>
    public static void UnregisterRemotePlayer(GameObject? playerObject) {
        if (playerObject == null) {
            return;
        }

        RemotePlayerObjects.Remove(playerObject);
        PlayerCollidersCache.Remove(playerObject);
    }

    /// <summary>
    /// Clears all tracked remote players.
    /// </summary>
    public static void ClearRemotePlayers() {
        RemotePlayerObjects.Clear();
        PlayerCollidersCache.Clear();
    }

    /// <summary>
    /// Gets cached colliders for the player GameObject to avoid dynamic GetComponentsInChildren allocations.
    /// </summary>
    public static Collider2D[] GetPlayerColliders(GameObject player) {
        if (PlayerCollidersCache.TryGetValue(player, out var colliders)) {
            return colliders;
        }

        // Fallback (e.g. local player or un-cached remote player)
        colliders = player.GetComponentsInChildren<Collider2D>(true);
        PlayerCollidersCache[player] = colliders;
        return colliders;
    }


    /// <summary>
    /// Walks up the transform hierarchy to find the tracked player root object.
    /// </summary>
    /// <param name="gameObject">The object to inspect.</param>
    /// <returns>The tracked player root if found; otherwise null.</returns>
    public static GameObject? GetTrackedPlayerRoot(GameObject? gameObject) {
        var heroObject = HeroController.instance == null ? null : HeroController.instance.gameObject;

        while (gameObject != null) {
            if (gameObject == heroObject || RemotePlayerObjects.Contains(gameObject)) {
                return gameObject;
            }

            var parent = gameObject.transform.parent;
            gameObject = parent == null ? null : parent.gameObject;
        }

        return null;
    }

    /// <summary>
    /// Gets the nearest tracked player to the given requester object.
    /// </summary>
    /// <param name="requester">The object requesting a target.</param>
    /// <returns>The nearest tracked player if one exists; otherwise null.</returns>
    public static GameObject? GetNearestPlayer(GameObject? requester) {
        var requesterPosition = requester == null
            ? Vector3.zero
            : requester.transform.position;

        return GetNearestPlayer(requesterPosition);
    }

    /// <summary>
    /// Gets the nearest tracked player to the given world position.
    /// </summary>
    /// <param name="requesterPosition">The position to measure from.</param>
    /// <returns>The nearest tracked player if one exists; otherwise null.</returns>
    private static GameObject? GetNearestPlayer(Vector3 requesterPosition) {
        return GetNearestPlayer(requesterPosition, GetTrackedPlayers());
    }

    /// <summary>
    /// Gets the nearest tracked player from a set of candidate objects.
    /// </summary>
    /// <param name="requesterPosition">The position to measure from.</param>
    /// <param name="candidates">The candidate objects to evaluate.</param>
    /// <returns>The nearest tracked player if one exists; otherwise null.</returns>
    private static GameObject? GetNearestPlayer(Vector3 requesterPosition, List<GameObject> candidates) {
        GameObject? nearestPlayer = null;
        var nearestDistanceSqr = float.MaxValue;

        foreach (var playerObject in candidates) {
            if (playerObject == null || !playerObject.activeInHierarchy) {
                continue;
            }

            var distanceSqr = (playerObject.transform.position - requesterPosition).sqrMagnitude;
            if (distanceSqr < nearestDistanceSqr) {
                nearestPlayer = playerObject;
                nearestDistanceSqr = distanceSqr;
            }
        }

        return nearestPlayer;
    }

    /// <summary>
    /// Gets a list of all currently tracked active player root objects.
    /// </summary>
    /// <returns>A pre-allocated list containing the local hero root and active registered remote player roots.</returns>
    public static List<GameObject> GetTrackedPlayers() {
        TrackedPlayersCache.Clear();
        if (HeroController.instance != null) {
            TrackedPlayersCache.Add(HeroController.instance.gameObject);
        }

        foreach (var playerObject in RemotePlayerObjects) {
            if (playerObject != null && playerObject.activeInHierarchy) {
                TrackedPlayersCache.Add(playerObject);
            }
        }

        return TrackedPlayersCache;
    }

    /// <summary>
    /// Determines whether the supplied object represents a vanilla hero, local player, remote tracked player,
    /// or another player-like target that should be eligible for retargeting.
    /// </summary>
    public static bool IsHeroLikeObject(GameObject? obj) {
        if (obj == null) {
            return false;
        }

        return obj.name == "Hero_Hornet(Clone)" ||
               obj.name == "Hero_Hornet" ||
               obj.name == "Player Prefab" ||
               obj.CompareTag("Player") ||
               obj.GetComponent<HeroController>() != null ||
               GetTrackedPlayerRoot(obj) != null;
    }
}

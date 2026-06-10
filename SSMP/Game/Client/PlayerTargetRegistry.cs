using System.Collections.Generic;
using UnityEngine;

namespace SSMP.Game.Client;

/// <summary>
/// Tracks remote player objects that can be targeted by host-side enemy FSMs.
/// </summary>
internal static class PlayerTargetRegistry {
    private static readonly HashSet<GameObject> RemotePlayerObjects = [];

    /// <summary>
    /// Registers a spawned remote player root object for targeting.
    /// </summary>
    /// <param name="playerObject">The remote player root object.</param>
    public static void RegisterRemotePlayer(GameObject playerObject) {
        RemotePlayerObjects.Add(playerObject);
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
    }

    /// <summary>
    /// Clears all tracked remote players.
    /// </summary>
    public static void ClearRemotePlayers() {
        RemotePlayerObjects.Clear();
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
    private static GameObject? GetNearestPlayer(Vector3 requesterPosition, IEnumerable<GameObject> candidates) {
        GameObject? nearestPlayer = null;
        var nearestDistanceSqr = float.MaxValue;
        var seenPlayers = new HashSet<GameObject>();

        void Consider(GameObject? playerObject) {
            playerObject = GetTrackedPlayerRoot(playerObject);
            if (playerObject == null || !playerObject.activeInHierarchy || !seenPlayers.Add(playerObject)) {
                return;
            }

            var distanceSqr = (playerObject.transform.position - requesterPosition).sqrMagnitude;
            if (distanceSqr >= nearestDistanceSqr) {
                return;
            }

            nearestPlayer = playerObject;
            nearestDistanceSqr = distanceSqr;
        }

        foreach (var playerObject in candidates) {
            Consider(playerObject);
        }

        return nearestPlayer;
    }

    /// <summary>
    /// Enumerates all currently tracked player root objects.
    /// </summary>
    /// <returns>The local hero root and all registered remote player roots.</returns>
    public static IEnumerable<GameObject> GetTrackedPlayers() {
        if (HeroController.instance != null) {
            yield return HeroController.instance.gameObject;
        }

        foreach (var playerObject in RemotePlayerObjects) {
            if (playerObject != null && playerObject.activeInHierarchy) {
                yield return playerObject;
            }
        }
    }
}

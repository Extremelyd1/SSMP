using UnityEngine;

namespace SSMP.Util;

/// <summary>
/// Marks the GameObject it is attached to as an attack of a remote player.
/// </summary>
internal class RemoteAttackComponent : MonoBehaviour {
    /// <summary>
    /// Whether the given GameObject is part of an attack of a remote player. Also checks the parents of the object,
    /// since attack objects spawn child objects that do their own collision handling.
    /// </summary>
    /// <param name="gameObject">The GameObject to check.</param>
    /// <returns>true if the GameObject or any of its parents is a remote attack; otherwise false.</returns>
    public static bool IsRemoteAttack(GameObject? gameObject) {
        return gameObject != null && gameObject.GetComponentInParent<RemoteAttackComponent>(true) != null;
    }
}

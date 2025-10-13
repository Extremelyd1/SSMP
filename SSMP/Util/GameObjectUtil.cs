using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SSMP.Util;

/// <summary>
/// Class for GameObject utility methods and extensions.
/// </summary>
internal static class GameObjectUtil {
    /// <summary>
    /// Find a GameObject with the given name in the children of the given GameObject.
    /// </summary>
    /// <param name="gameObject">The GameObject to search in.</param>
    /// <param name="name">The name of the GameObject to search for.</param>
    /// <returns>The GameObject if found, null otherwise.</returns>
    public static GameObject? FindGameObjectInChildren(
        this GameObject gameObject,
        string name
    ) {
        if (gameObject == null) {
            return null;
        }

        for (var i = 0; i < gameObject.transform.childCount; i++) {
            var child = gameObject.transform.GetChild(i);
            if (child != null && child.name == name) {
                return child.gameObject;
            }
        }
        // foreach (var componentsInChild in gameObject.GetComponentsInChildren<Transform>(true)) {
        //     if (componentsInChild.name == name) {
        //         return componentsInChild.gameObject;
        //     }
        // }

        return null;
    }

    /// <summary>
    /// Get a list of the children of the given GameObject.
    /// </summary>
    /// <param name="gameObject">The GameObject to get the children for.</param>
    /// <returns>A list of the children of the GameObject.</returns>
    public static List<GameObject> GetChildren(this GameObject gameObject) {
        var children = new List<GameObject>();
        for (var i = 0; i < gameObject.transform.childCount; i++) {
            children.Add(gameObject.transform.GetChild(i).gameObject);
        }

        return children;
    }
    
    /// <summary>
    /// Find an inactive GameObject with the given name.
    /// </summary>
    /// <param name="name">The name of the GameObject.</param>
    /// <returns>The GameObject is it exists, null otherwise.</returns>
    public static GameObject? FindInactiveGameObject(string name) {
        var transforms = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (var transform in transforms) {
            if (transform.hideFlags == HideFlags.None) {
                if (transform.name == name) {
                    return transform.gameObject;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Destroy given object after given time (in seconds).
    /// </summary>
    /// <param name="obj">The game object to destroy.</param>
    /// <param name="time">The time in seconds as a float.</param>
    /// <param name="coroutineOrigin">The <see cref="MonoBehaviour"/> to use for starting the coroutine.</param>
    public static void DestroyAfterTime(this GameObject obj, float time, MonoBehaviour? coroutineOrigin = null) {
        if (coroutineOrigin == null) {
            MonoBehaviourUtil.Instance.StartCoroutine(WaitDestroy());
        } else {
            coroutineOrigin.StartCoroutine(WaitDestroy());
        }

        return;

        IEnumerator WaitDestroy() {
            yield return new WaitForSeconds(time);

            Object.Destroy(obj);
        }
    }
    
    /// <summary>
    /// Activate given object after given time (in seconds).
    /// </summary>
    /// <param name="obj">The game object to activate.</param>
    /// <param name="time">The time in seconds as a float.</param>
    /// <param name="coroutineOrigin">The <see cref="MonoBehaviour"/> to use for starting the coroutine.</param>
    public static void ActivateAfterTime(this GameObject obj, float time, MonoBehaviour? coroutineOrigin = null) {
        if (coroutineOrigin == null) {
            MonoBehaviourUtil.Instance.StartCoroutine(WaitActivate());
        } else {
            coroutineOrigin.StartCoroutine(WaitActivate());
        }

        return;

        IEnumerator WaitActivate() {
            yield return new WaitForSeconds(time);

            obj.SetActive(true);
        }
    }
}

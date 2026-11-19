using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SSMP.Util;

/// <summary>
/// Class for GameObject utility methods and extensions.
/// </summary>
internal static class GameObjectUtil {
    /// <param name="gameObject">The GameObject to search in.</param>
    extension(GameObject gameObject) {
        /// <summary>
        /// Find a GameObject with the given name in the children of the given GameObject.
        /// </summary>
        /// <param name="name">The name of the GameObject to search for.</param>
        /// <returns>The GameObject if found, null otherwise.</returns>
        public GameObject? FindGameObjectInChildren(
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
        /// Destroys a GameObject with the given name in the children of the given GameObject, if there is one.
        /// </summary>
        /// <param name="name">The name of the GameObject to search for.</param>
        public void DestroyGameObjectInChildren(string name) {
            var child = FindGameObjectInChildren(gameObject, name);
            if (child != null) {
                Object.Destroy(child);
            }
        }

        /// <summary>
        /// Get a list of the children of the given GameObject.
        /// </summary>
        /// <returns>A list of the children of the GameObject.</returns>
        public List<GameObject> GetChildren() {
            var children = new List<GameObject>();
            for (var i = 0; i < gameObject.transform.childCount; i++) {
                children.Add(gameObject.transform.GetChild(i).gameObject);
            }

            return children;
        }

        /// <summary>
        /// Removes a component of the given type from the GameObject with
        /// <see cref="Object.DestroyImmediate(Object)"/>, if it has one.
        /// </summary>
        /// <typeparam name="T">The component type to remove.</typeparam>
        public void DestroyComponent<T>() where T : Component {
            if (gameObject != null && gameObject.TryGetComponent<T>(out var component)) {
                Object.DestroyImmediate(component);
            }
        }

        /// <summary>
        /// Removes all components of the given type from the GameObject and all of its children with
        /// <see cref="Object.DestroyImmediate(Object)"/>.
        /// </summary>
        /// <typeparam name="T">The type of component to remove.</typeparam>
        public void DestroyComponentsInChildren<T>() where T : Component {
            if (gameObject == null) {
                return;
            }

            foreach (var component in gameObject.GetComponentsInChildren<T>()) {
                Object.DestroyImmediate(component);
            }
        }

        /// <summary>
        /// Destroy given object after given time (in seconds).
        /// </summary>
        /// <param name="time">The time in seconds as a float.</param>
        /// <param name="coroutineOrigin">The <see cref="MonoBehaviour"/> to use for starting the coroutine.</param>
        public void DestroyAfterTime(float time, MonoBehaviour? coroutineOrigin = null) {
            if (coroutineOrigin == null) {
                MonoBehaviourUtil.Instance.StartCoroutine(WaitDestroy());
            } else {
                coroutineOrigin.StartCoroutine(WaitDestroy());
            }

            return;

            IEnumerator WaitDestroy() {
                yield return new WaitForSeconds(time);

                Object.Destroy(gameObject);
            }
        }

        /// <summary>
        /// Activate given object after given time (in seconds).
        /// </summary>
        /// <param name="time">The time in seconds as a float.</param>
        /// <param name="coroutineOrigin">The <see cref="MonoBehaviour"/> to use for starting the coroutine.</param>
        public void ActivateAfterTime(float time, MonoBehaviour? coroutineOrigin = null) {
            if (coroutineOrigin == null) {
                MonoBehaviourUtil.Instance.StartCoroutine(WaitActivate());
            } else {
                coroutineOrigin.StartCoroutine(WaitActivate());
            }

            return;

            IEnumerator WaitActivate() {
                yield return new WaitForSeconds(time);

                gameObject.SetActive(true);
            }
        }
    }

    /// <summary>
    /// Walks up the transform parent hierarchy to find the top-level root transform.
    /// </summary>
    /// <param name="source">The source game object to walk up from.</param>
    /// <returns>The root transform, or null if source or its transform is null.</returns>
    public static Transform? GetHitSourceRoot(this GameObject? source) {
        var transform = source?.transform;
        if (transform == null) {
            return null;
        }

        while (transform.parent != null) {
            transform = transform.parent;
        }

        return transform;
    }
}

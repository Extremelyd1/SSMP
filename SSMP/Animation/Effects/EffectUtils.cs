using HutongGames.PlayMaker.Actions;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects;

internal static class EffectUtils {
    /// <summary>
    /// Removes the AutoRecycleSelf component from a specified GameObject, ensuring that any active timers and
    /// subscriptions are stopped before removal.
    /// </summary>
    /// <param name="obj">The GameObject from which to remove the AutoRecycleSelf from.</param>
    public static void SafelyRemoveAutoRecycle(GameObject obj) {
        var recycler = obj.GetComponent<AutoRecycleSelf>();
        if (recycler != null) {
            // Stop listeners before destroying
            recycler.recycleTimerRunning = false;
            recycler.subbed = false;

            Component.Destroy(recycler);
        }
    }

    /// <summary>
    /// Instantiates an object from the global pool at the specified location
    /// </summary>
    /// <param name="spawner">The spawner responsible for providing the global pool object.</param>
    /// <param name="spawnLocation">The location where the object will be spawned.</param>
    /// <param name="destroyAfterDelay">The duration, in seconds, after which the spawned object will be destroyed.</param>
    /// <param name="keepParent">Whether to keep the parent or unparent the new object</param>
    /// <returns>A newly spawned GameObject from the global pool.</returns>
    public static GameObject? SpawnGlobalPoolObject(SpawnObjectFromGlobalPool? spawner, Transform spawnLocation, float destroyAfterDelay, bool keepParent = false) {
        if (spawner == null) {
            Logger.Warn("Unable to find global pool object");
            return null;
        }

        return SpawnGlobalPoolObject(spawner.gameObject.Value, spawnLocation, destroyAfterDelay, keepParent);
    }

    /// <inheritdoc cref="SpawnGlobalPoolObject(SpawnObjectFromGlobalPool?, Transform, float, bool)"/>
    /// <param name="globalObj">The GameObject to spawn</param>
    /// <param name="spawnLocation"><inheritdoc/></param>
    /// <param name="destroyAfterDelay"><inheritdoc/></param>
    /// <param name="keepParent"><inheritdoc/></param>
    public static GameObject? SpawnGlobalPoolObject(GameObject? globalObj, Transform spawnLocation, float destroyAfterDelay, bool keepParent = false) {
        if (globalObj == null) {
            Logger.Warn("Unable to find global pool object");
            return null;
        }

        var newObj = GameObject.Instantiate(globalObj, spawnLocation);
        if (newObj == null) {
            Logger.Warn($"Unable to spawn global pool object {globalObj.name}");
            return null;
        }

        // This is ugly and i hate it, but it works
        if (!keepParent) {
            newObj.transform.SetParent(null);
            newObj.transform.position = spawnLocation.position;
        }

        newObj.SetActive(true);

        SafelyRemoveAutoRecycle(newObj);

        if (destroyAfterDelay > 0) {
            newObj.DestroyAfterTime(destroyAfterDelay);
        }

        return newObj;
    }
}

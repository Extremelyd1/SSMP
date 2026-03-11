using HutongGames.PlayMaker.Actions;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects;

internal static class EffectUtils {
    public static void SafelyRemoveAutoRecycle(GameObject obj) {
        var recycler = obj.GetComponent<AutoRecycleSelf>();
        if (recycler != null) {
            // Stop listeners before destroying
            recycler.recycleTimerRunning = false;
            recycler.subbed = false;

            Component.Destroy(recycler);
        }
    }

    public static GameObject? SpawnGlobalPoolObject(SpawnObjectFromGlobalPool? spawner, Transform spawnLocation, float destroyAfterDelay, bool keepParent = false) {
        if (spawner == null) {
            Logger.Warn("Unable to find global pool object");
            return null;
        }

        return SpawnGlobalPoolObject(spawner.gameObject.Value, spawnLocation, destroyAfterDelay, keepParent);
    }

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

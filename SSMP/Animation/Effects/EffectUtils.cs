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

    public static GameObject SpawnGlobalPoolObject(GameObject globalObj, Transform spawnLocation, bool keepParent = false) {
        var newObj = GameObject.Instantiate(globalObj, spawnLocation);
        
        if (!keepParent) {
            newObj.transform.SetParent(null);
            newObj.transform.position = spawnLocation.position;
        }
        newObj.SetActive(true);

        SafelyRemoveAutoRecycle(newObj);
        return newObj;
    }
}

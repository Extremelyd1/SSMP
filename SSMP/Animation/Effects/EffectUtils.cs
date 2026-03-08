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
}

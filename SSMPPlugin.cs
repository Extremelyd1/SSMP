using BepInEx;
using SSMP.Game.Settings;
using SSMP.Logging;
using SSMP.Util;
using UnityEngine;

namespace SSMP;

[BepInAutoPlugin(id: "ssmp")]
public partial class SSMPPlugin : BaseUnityPlugin {
    private void Awake() {
        Logging.Logger.AddLogger(new BepInExLogger());

        Logging.Logger.Info($"Plugin {Name} ({Id}) has loaded!");

        // Add the MonoBehaviourUtil to the game object associated with this plugin
        gameObject.AddComponent<MonoBehaviourUtil>();

        new Game.GameManager().Initialize();
    }
}

using BepInEx;
using SSMP.Game.Settings;
using SSMP.Logging;
using SSMP.Util;
using UnityEngine;

namespace SSMP;

[BepInAutoPlugin(id: "ssmp")]
public partial class SSMPPlugin : BaseUnityPlugin {
    /// <summary>
    /// Statically create Settings object, so it can be accessed early.
    /// </summary>
    private ModSettings _modSettings = new ModSettings();
    
    /// <summary>
    /// The game manager instance.
    /// </summary>
    private Game.GameManager _gameManager;
    
    private void Awake() {
        Logging.Logger.AddLogger(new BepInExLogger());

        Logging.Logger.Info($"Plugin {Name} ({Id}) has loaded!");
        
        // Create a persistent gameObject where we can add the MonoBehaviourUtil to
        var persistentObject = new GameObject("SSMP Persistent GameObject");
        DontDestroyOnLoad(persistentObject);
        persistentObject.AddComponent<MonoBehaviourUtil>();

        _gameManager = new Game.GameManager(_modSettings);
    }
}
using BepInEx;
using SSMP.Hooks;
using SSMP.Logging;
using SSMP.Util;

namespace SSMP;

[BepInAutoPlugin(id: "ssmp")]
public partial class SSMPPlugin : BaseUnityPlugin {
    /// <summary>
    /// Plugin constructor that initializes the static classes with hooks.
    /// </summary>
    public SSMPPlugin() {
        Logging.Logger.AddLogger(new BepInExLogger());
        
        EventHooks.Initialize();
        CustomHooks.Initialize();
    }
    
    private void Awake() {
        Logging.Logger.Info($"Plugin {Name} ({Id}) has loaded!");

        // Register the event to initialize SSMP once we enter the main menu.
        EventHooks.UIManagerUIGoToMainMenu += Initialize;
    }

    /// <summary>
    /// Initializes the mod by initializing the <see cref="GameManager"/>.
    /// </summary>
    private void Initialize() {
        Logging.Logger.Info("Initializing SSMP");
        
        EventHooks.UIManagerUIGoToMainMenu -= Initialize;
        
        // Add the MonoBehaviourUtil to the game object associated with this plugin
        gameObject.AddComponent<MonoBehaviourUtil>();
        
        new Game.GameManager().Initialize();
    }
}

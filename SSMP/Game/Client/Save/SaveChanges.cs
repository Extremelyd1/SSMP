using System.Linq;
using SSMP.Util;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Game.Client.Save;

/// <summary>
/// Class that handles incoming save changes that have an immediate effect in the current scene for the local player.
/// E.g. breakable walls that also break in another scene, tollgates that are being paid, stag station being bought.
/// </summary>
internal class SaveChanges {
    /// <summary>
    /// Apply a change in player data from a save update for the given name immediately. This checks whether
    /// the local player is in a scene where the changes in player data have an effect on the environment.
    /// For example, a breakable wall that also opens up in another scene or a stag station being bought.
    /// </summary>
    /// <param name="name">The name of the PlayerData entry.</param>
    public void ApplyPlayerDataSaveChange(string? name) {
        Logger.Debug($"ApplyPlayerData for name: {name}");
        
        var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    }

    /// <summary>
    /// Apply a change in persistent values from a save update for the given name immediately. This checks whether
    /// the local player is in a scene where the changes in player data have an effect on the environment.
    /// For example, a breakable wall that also opens up in another scene or a stag station being bought.
    /// </summary>
    /// <param name="itemKey">The persistent item key containing the ID and scene name of the changed object.</param>
    public void ApplyPersistentValueSaveChange(PersistentItemKey itemKey) {
        Logger.Debug($"ApplyPersistent for item data: {itemKey}");

        var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    }

    /// <summary>
    /// Whether the local player is currently in a toll machine dialogue prompt that has claimed control of the
    /// character.
    /// </summary>
    /// <param name="currentStateName">The name of the current state of the dialogue FSM.</param>
    /// <returns>true if the player is in dialogue, false otherwise.</returns>
    private bool IsInTollMachineDialogue(string currentStateName) {
        string[] outOfDialogueStateNames = [
            "Out Of Range", "In Range", "Can Inspect?", "Cancel Frame", "Pause", "Activated?", "Paid?", "Get Price", "Init", 
            "Regain Control"
        ];

        return !outOfDialogueStateNames.Contains(currentStateName);
    }

    /// <summary>
    /// Hide the currently active dialogue box by setting the state of the 'Dialogue Page Control' FSM of the 'Text YN'
    /// game object. Needs to be amended if this method should also hide dialogue boxes of other dialogue types.
    /// </summary>
    // private void HideDialogueBox() {
    //     var gc = GameCameras.instance;
    //     if (gc == null) {
    //         Logger.Warn("Could not find GameCameras instance");
    //         return;
    //     }
    //
    //     var hudCamera = gc.hudCamera;
    //     if (hudCamera == null) {
    //         Logger.Warn("Could not find hudCamera");
    //         return;
    //     }
    //
    //     var dialogManager = hudCamera.gameObject.FindGameObjectInChildren("DialogueManager");
    //     if (dialogManager == null) {
    //         Logger.Warn("Could not find dialogueManager");
    //         return;
    //     }
    //
    //     void HideDialogueObject(string objectName, string heroDmgState) {
    //         var obj = dialogManager.FindGameObjectInChildren(objectName);
    //         if (obj != null) {
    //             var dialogueBox = obj.GetComponent<DialogueBox>();
    //             if (dialogueBox == null) {
    //                 Logger.Warn($"Could not find {objectName} DialogueBox");
    //                 return;
    //             }
    //
    //             var hidden = ReflectionHelper.GetField<DialogueBox, bool>(dialogueBox, "hidden");
    //             if (hidden) {
    //                 return;
    //             }
    //         
    //             var pageControlFsm = obj.LocateMyFSM("Dialogue Page Control");
    //             if (pageControlFsm == null) {
    //                 Logger.Warn($"Could not find {objectName} DialoguePageControl FSM");
    //                 return;
    //             }
    //             pageControlFsm.SetState(heroDmgState);
    //         }
    //     }
    //     
    //     HideDialogueObject("Text YN", "Hero Damaged");
    //     HideDialogueObject("Text", "Pause");
    // }
}

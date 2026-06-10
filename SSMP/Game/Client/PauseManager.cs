using System.Collections;
using System.Reflection;
using GlobalEnums;
using SSMP.Networking.Client;
using UnityEngine;
using MonoMod.RuntimeDetour;
using SSMP.Hooks;

namespace SSMP.Game.Client;

/// <summary>
/// Handles pause related things to prevent player being invincible in pause menu while connected to a server.
/// </summary>
internal class PauseManager {
    /// <summary>
    /// The net client instance.
    /// </summary>
    private readonly NetClient _netClient;

    private Hook? _uiManagerTogglePauseGameHook;
    private Hook? _heroControllerPauseHook;
    private Hook? _transitionPointOnTriggerEnter2DHook;
    private Hook? _heroControllerDieFromHazardHook;

    public PauseManager(NetClient netClient) {
        _netClient = netClient;
    }

    /// <summary>
    /// Registers the required method hooks.
    /// </summary>
    public void RegisterHooks() {
        _uiManagerTogglePauseGameHook = new Hook(
            typeof(UIManager).GetMethod(
                "TogglePauseGame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )!,
            UIManagerOnTogglePauseGame
        );
        _heroControllerPauseHook = new Hook(
            typeof(HeroController).GetMethod(
                "Pause", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )!,
            HeroControllerOnPause
        );
        _transitionPointOnTriggerEnter2DHook = new Hook(
            typeof(TransitionPoint).GetMethod(
                "OnTriggerEnter2D", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )!,
            TransitionPointOnOnTriggerEnter2D
        );
        _heroControllerDieFromHazardHook = new Hook(
            typeof(HeroController).GetMethod(
                "DieFromHazard", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )!,
            HeroControllerOnDieFromHazard
        );

        EventHooks.HeroControllerDie += HeroControllerDieHook;
    }

    /// <summary>
    /// Deregisters the required method hooks.
    /// </summary>
    public void DeregisterHooks() {
        _uiManagerTogglePauseGameHook?.Dispose();
        _uiManagerTogglePauseGameHook = null;

        _heroControllerPauseHook?.Dispose();
        _heroControllerPauseHook = null;

        _transitionPointOnTriggerEnter2DHook?.Dispose();
        _transitionPointOnTriggerEnter2DHook = null;

        _heroControllerDieFromHazardHook?.Dispose();
        _heroControllerDieFromHazardHook = null;

        EventHooks.HeroControllerDie -= HeroControllerDieHook;
    }

    private delegate void OrigTogglePauseGame(UIManager self);

    private void UIManagerOnTogglePauseGame(OrigTogglePauseGame orig, UIManager self) {
        if (!_netClient.IsConnected) {
            orig(self);
            return;
        }

        var field = typeof(UIManager).GetField(
            "ignoreUnpause", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        );
        var setTimeScale = !(bool) (field?.GetValue(self) ?? false);

        orig(self);

        if (setTimeScale) {
            SetTimeScale(1f);
        }
    }

    private void HeroControllerDieHook(bool nonLethal, bool frostDeath) {
        OnDeath();
    }

    /// <summary>
    /// Callback method for when the player dies.
    /// If we are paused while the player dies, the game enters a state where the cursor is visible
    /// while not in the pause menu, but not being able to give any input apart from opening the pause menu.
    /// Therefore, we unpause immediately before dying to prevent this.
    /// </summary>
    private void OnDeath() {
        ImmediateUnpauseIfPaused();
    }

    private delegate IEnumerator OrigDieFromHazard(HeroController self, HazardType hazardType, float angle);

    private IEnumerator HeroControllerOnDieFromHazard(
        OrigDieFromHazard orig,
        HeroController self,
        HazardType hazardType,
        float angle
    ) {
        ImmediateUnpauseIfPaused();

        return orig(self, hazardType, angle);
    }

    private delegate void OrigOnTriggerEnter2D(TransitionPoint self, Collider2D obj);

    private void TransitionPointOnOnTriggerEnter2D(
        OrigOnTriggerEnter2D orig,
        TransitionPoint self,
        Collider2D obj
    ) {
        if (!self.isADoor) {
            ImmediateUnpauseIfPaused();
        }

        orig(self, obj);
    }

    private delegate void OrigPause(HeroController self);

    private void HeroControllerOnPause(OrigPause orig, HeroController self) {
        if (!_netClient.IsConnected) {
            orig(self);
            return;
        }

        typeof(HeroController).InvokeMember(
            "ResetInput",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.InvokeMethod,
            null,
            HeroController.instance,
            null
        );
    }

    /// <summary>
    /// Unpauses the game immediately if it was paused.
    /// </summary>
    private static void ImmediateUnpauseIfPaused() {
        if (UIManager.instance != null) {
            if (UIManager.instance.uiState.Equals(UIState.PAUSED)) {
                var gm = global::GameManager.instance;

                gm.gameCams.ResumeCameraShake();
                gm.inputHandler.PreventPause();
                gm.actorSnapshotUnpaused.TransitionTo(0f);
                gm.isPaused = false;
                gm.ui.AudioGoToGameplay(0.2f);
                gm.ui.SetState(UIState.PLAYING);
                gm.SetState(GameState.PLAYING);
                if (HeroController.instance != null) {
                    HeroController.instance.UnPause();
                }

                MenuButtonList.ClearAllLastSelected();
                gm.inputHandler.AllowPause();
            }
        }
    }

    /// <summary>
    /// Sets the time scale similarly to the method GameManager#SetTimeScale.
    /// </summary>
    /// <param name="timeScale">The new time scale.</param>
    public static void SetTimeScale(float timeScale) {
        Time.timeScale = timeScale > 0.00999999977648258f ? timeScale : 0.0f;
    }
}

using System.Collections;
using System.Reflection;
using GlobalEnums;
using MonoMod.RuntimeDetour;
using SSMP.Hooks;
using SSMP.Networking.Client;
using UnityEngine;

namespace SSMP.Game.Client;

/// <summary>
/// Handles pause-related behavior while connected to a server.
/// </summary>
/// <remarks>
/// Known quirk: because multiplayer pause keeps world time running, Hornet can preserve some pre-pause physics
/// momentum instead of freezing instantly like vanilla pause. Do not fix unless it becomes gameplay-impacting.
/// </remarks>
internal sealed class PauseManager {
    /// <summary>
    /// Minimum positive time scale accepted by the vanilla time-scale logic.
    /// Smaller values are treated as paused.
    /// </summary>
    private const float MinPositiveTimeScale = 0.00999999977648258f;

    /// <summary>
    /// Binding flags used to access vanilla instance members regardless of visibility.
    /// </summary>
    private const BindingFlags InstanceMemberFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Whether the vanilla pause menu is currently open while connected to a server.
    /// </summary>
    public static bool IsMultiplayerPauseMenuOpen { get; private set; }

    /// <summary>
    /// The net client used to determine whether multiplayer pause handling should be active.
    /// </summary>
    private readonly NetClient _netClient;

    /// <summary>
    /// Hook for <c>UIManager.TogglePauseGame</c>.
    /// </summary>
    private Hook? _uiManagerTogglePauseGameHook;

    /// <summary>
    /// Hook for <c>HeroController.Pause</c>.
    /// Used only to restore world time after vanilla pauses the local hero.
    /// </summary>
    private Hook? _heroControllerPauseHook;

    /// <summary>
    /// Hook for <c>TransitionPoint.OnTriggerEnter2D</c>.
    /// </summary>
    private Hook? _transitionPointOnTriggerEnter2DHook;

    /// <summary>
    /// Hook for <c>HeroController.DieFromHazard</c>.
    /// </summary>
    private Hook? _heroControllerDieFromHazardHook;

    /// <summary>
    /// Creates a new pause manager.
    /// </summary>
    /// <param name="netClient">The client used to determine multiplayer connection state.</param>
    public PauseManager(NetClient netClient) {
        _netClient = netClient;
    }

    /// <summary>
    /// Registers pause-related hooks.
    /// </summary>
    public void RegisterHooks() {
        _uiManagerTogglePauseGameHook = new Hook(
            typeof(UIManager).GetMethod("TogglePauseGame", InstanceMemberFlags)!,
            UIManagerOnTogglePauseGame
        );

        _heroControllerPauseHook = new Hook(
            typeof(HeroController).GetMethod("Pause", InstanceMemberFlags)!,
            HeroControllerOnPause
        );

        _transitionPointOnTriggerEnter2DHook = new Hook(
            typeof(TransitionPoint).GetMethod("OnTriggerEnter2D", InstanceMemberFlags)!,
            TransitionPointOnTriggerEnter2D
        );

        _heroControllerDieFromHazardHook = new Hook(
            typeof(HeroController).GetMethod("DieFromHazard", InstanceMemberFlags)!,
            HeroControllerOnDieFromHazard
        );

        EventHooks.HeroControllerDie += HeroControllerDieHook;
    }

    /// <summary>
    /// Deregisters pause-related hooks.
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

        IsMultiplayerPauseMenuOpen = false;
    }

    /// <summary>
    /// Original <c>UIManager.TogglePauseGame</c> delegate signature.
    /// </summary>
    /// <param name="self">The UI manager instance.</param>
    private delegate void OrigTogglePauseGame(UIManager self);

    /// <summary>
    /// Detour for <c>UIManager.TogglePauseGame</c>.
    /// Lets vanilla pause and unpause run normally, then restores simulation time while connected.
    /// </summary>
    /// <param name="orig">The original vanilla method.</param>
    /// <param name="self">The UI manager instance.</param>
    private void UIManagerOnTogglePauseGame(OrigTogglePauseGame orig, UIManager self) {
        if (!_netClient.IsConnected) {
            IsMultiplayerPauseMenuOpen = false;
            orig(self);
            return;
        }

        orig(self);

        IsMultiplayerPauseMenuOpen = self.uiState == UIState.PAUSED;

        if (IsMultiplayerPauseMenuOpen) {
            SetTimeScale(1f);
        }
    }


    /// <summary>
    /// Event callback fired when the hero dies.
    /// </summary>
    /// <param name="nonLethal">Whether the death was non-lethal.</param>
    /// <param name="frostDeath">Whether the death was caused by frost.</param>
    private void HeroControllerDieHook(bool nonLethal, bool frostDeath) {
        ImmediateUnpauseIfPaused();
    }

    /// <summary>
    /// Original <c>HeroController.DieFromHazard</c> delegate signature.
    /// </summary>
    /// <param name="self">The hero controller instance.</param>
    /// <param name="hazardType">The hazard type that caused the death.</param>
    /// <param name="angle">The hazard impact angle.</param>
    /// <returns>The original hazard death coroutine.</returns>
    private delegate IEnumerator OrigDieFromHazard(HeroController self, HazardType hazardType, float angle);

    /// <summary>
    /// Detour for <c>HeroController.DieFromHazard</c>.
    /// Forces an immediate unpause before starting the hazard death coroutine.
    /// </summary>
    /// <param name="orig">The original vanilla method.</param>
    /// <param name="self">The hero controller instance.</param>
    /// <param name="hazardType">The hazard type that caused the death.</param>
    /// <param name="angle">The hazard impact angle.</param>
    /// <returns>The original hazard death coroutine.</returns>
    private IEnumerator HeroControllerOnDieFromHazard(
        OrigDieFromHazard orig,
        HeroController self,
        HazardType hazardType,
        float angle
    ) {
        ImmediateUnpauseIfPaused();
        return orig(self, hazardType, angle);
    }

    /// <summary>
    /// Original <c>TransitionPoint.OnTriggerEnter2D</c> delegate signature.
    /// </summary>
    /// <param name="self">The transition point instance.</param>
    /// <param name="obj">The collider that entered the transition trigger.</param>
    private delegate void OrigOnTriggerEnter2D(TransitionPoint self, Collider2D obj);

    /// <summary>
    /// Detour for <c>TransitionPoint.OnTriggerEnter2D</c>.
    /// Forces an immediate unpause before non-door scene transitions.
    /// </summary>
    /// <param name="orig">The original vanilla method.</param>
    /// <param name="self">The transition point instance.</param>
    /// <param name="obj">The collider that entered the transition trigger.</param>
    private void TransitionPointOnTriggerEnter2D(
        OrigOnTriggerEnter2D orig,
        TransitionPoint self,
        Collider2D obj
    ) {
        if (!self.isADoor) {
            ImmediateUnpauseIfPaused();
        }

        orig(self, obj);
    }


    /// <summary>
    /// Original <c>HeroController.Pause</c> delegate signature.
    /// </summary>
    /// <param name="self">The hero controller instance.</param>
    private delegate void OrigPause(HeroController self);

    /// <summary>
    /// Detour for <c>HeroController.Pause</c>.
    /// Lets vanilla pause Hornet normally, then restores world time while connected.
    /// </summary>
    /// <param name="orig">The original vanilla method.</param>
    /// <param name="self">The hero controller instance.</param>
    private void HeroControllerOnPause(OrigPause orig, HeroController self) {
        orig(self);

        if (!_netClient.IsConnected) {
            return;
        }

        if (UIManager.instance != null && UIManager.instance.uiState == UIState.PAUSED) {
            IsMultiplayerPauseMenuOpen = true;
            SetTimeScale(1f);
        }
    }

    /// <summary>
    /// Unpauses the game immediately if the UI is currently in the paused state.
    /// Used for death, hazard death, and scene transitions where the pause menu must not remain open.
    /// </summary>
    private static void ImmediateUnpauseIfPaused() {
        IsMultiplayerPauseMenuOpen = false;

        if (UIManager.instance == null || UIManager.instance.uiState != UIState.PAUSED) {
            SetTimeScale(1f);
            return;
        }

        var gameManager = global::GameManager.instance;

        gameManager.gameCams.ResumeCameraShake();
        gameManager.inputHandler.PreventPause();
        gameManager.actorSnapshotUnpaused.TransitionTo(0f);
        gameManager.isPaused = false;
        gameManager.ui.AudioGoToGameplay(0.2f);
        gameManager.ui.SetState(UIState.PLAYING);
        gameManager.SetState(GameState.PLAYING);

        if (HeroController.instance != null) {
            HeroController.instance.UnPause();
        }

        MenuButtonList.ClearAllLastSelected();
        gameManager.inputHandler.AllowPause();

        SetTimeScale(1f);
    }

    /// <summary>
    /// Sets Unity time scale using the same lower-bound behavior as <c>GameManager.SetTimeScale</c>.
    /// </summary>
    /// <param name="timeScale">The requested time scale.</param>
    public static void SetTimeScale(float timeScale) {
        Time.timeScale = timeScale > MinPositiveTimeScale ? timeScale : 0f;
    }
}

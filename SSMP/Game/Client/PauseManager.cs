using System.Collections;
using System.Reflection;
using GlobalEnums;
using SSMP.Hooks;
using SSMP.Networking.Client;
using UnityEngine;
using MonoMod.RuntimeDetour;

namespace SSMP.Game.Client;

/// <summary>
/// Handles pause-related behavior while connected to a server.
/// </summary>
internal class PauseManager {
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
    /// Binding flags used to invoke private vanilla instance methods.
    /// </summary>
    private const BindingFlags PrivateInstanceInvokeFlags =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.InvokeMethod;

    /// <summary>
    /// The net client used to check whether multiplayer pause handling should be active.
    /// </summary>
    private readonly NetClient _netClient;

    /// <summary>
    /// Hook for <c>UIManager.TogglePauseGame</c>.
    /// </summary>
    private Hook? _uiManagerTogglePauseGameHook;

    /// <summary>
    /// Hook for <c>HeroController.Pause</c>.
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
    /// Registers the hooks used to override vanilla pause behavior while connected.
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
            TransitionPointOnOnTriggerEnter2D
        );

        _heroControllerDieFromHazardHook = new Hook(
            typeof(HeroController).GetMethod("DieFromHazard", InstanceMemberFlags)!,
            HeroControllerOnDieFromHazard
        );

        EventHooks.HeroControllerDie += HeroControllerDieHook;
    }

    /// <summary>
    /// Deregisters all pause-related hooks and event subscriptions.
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

    /// <summary>
    /// Original <c>UIManager.TogglePauseGame</c> delegate signature.
    /// </summary>
    /// <param name="self">The UI manager instance.</param>
    private delegate void OrigTogglePauseGame(UIManager self);

    /// <summary>
    /// Detour for <c>UIManager.TogglePauseGame</c>.
    /// Keeps time running after using the pause menu while connected to a server.
    /// </summary>
    /// <param name="orig">The original vanilla method.</param>
    /// <param name="self">The UI manager instance.</param>
    private void UIManagerOnTogglePauseGame(OrigTogglePauseGame orig, UIManager self) {
        if (!_netClient.IsConnected) {
            orig(self);
            return;
        }

        var field = typeof(UIManager).GetField("ignoreUnpause", InstanceMemberFlags);
        var shouldRestoreTimeScale = !(bool) (field?.GetValue(self) ?? false);

        orig(self);

        if (shouldRestoreTimeScale) {
            SetTimeScale(1f);
        }
    }

    /// <summary>
    /// Event callback fired when the hero dies.
    /// </summary>
    /// <param name="nonLethal">Whether the death was non-lethal.</param>
    /// <param name="frostDeath">Whether the death was caused by frost.</param>
    private void HeroControllerDieHook(bool nonLethal, bool frostDeath) {
        OnDeath();
    }

    /// <summary>
    /// Handles hero death by forcing the game out of pause before the death flow continues.
    /// </summary>
    private void OnDeath() {
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

    /// <summary>
    /// Original <c>HeroController.Pause</c> delegate signature.
    /// </summary>
    /// <param name="self">The hero controller instance.</param>
    private delegate void OrigPause(HeroController self);

    /// <summary>
    /// Detour for <c>HeroController.Pause</c>.
    /// Prevents the vanilla pause state while connected, but still resets input.
    /// </summary>
    /// <param name="orig">The original vanilla method.</param>
    /// <param name="self">The hero controller instance.</param>
    private void HeroControllerOnPause(OrigPause orig, HeroController self) {
        if (!_netClient.IsConnected) {
            orig(self);
            return;
        }

        typeof(HeroController).InvokeMember(
            "ResetInput",
            PrivateInstanceInvokeFlags,
            null,
            HeroController.instance,
            null
        );
    }

    /// <summary>
    /// Unpauses the game immediately if the UI is currently in the paused state.
    /// </summary>
    private static void ImmediateUnpauseIfPaused() {
        if (UIManager.instance == null || !UIManager.instance.uiState.Equals(UIState.PAUSED)) {
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
    }

    /// <summary>
    /// Sets the Unity time scale using the same lower-bound behavior as <c>GameManager.SetTimeScale</c>.
    /// </summary>
    /// <param name="timeScale">The requested time scale.</param>
    public static void SetTimeScale(float timeScale) {
        Time.timeScale = timeScale > MinPositiveTimeScale ? timeScale : 0.0f;
    }
}

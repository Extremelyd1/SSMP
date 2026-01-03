using System;
using System.Collections;
using System.Collections.Generic;
using GlobalEnums;
using SSMP.Api.Client;
using SSMP.Game.Settings;
using SSMP.Hooks;
using SSMP.Networking.Client;
using SSMP.Networking.Transport.Common;
using SSMP.Ui.Chat;
using SSMP.Util;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Ui;

/// <inheritdoc />
internal class UiManager : IUiManager {
    #region Constants
    
    /// <summary>
    /// The font size for normal text elements (24 pixels).
    /// Used for general UI labels and buttons.
    /// </summary>
    public const int NormalFontSize = 24;

    /// <summary>
    /// The font size for chat messages (22 pixels).
    /// Slightly smaller for better chat readability.
    /// </summary>
    public const int ChatFontSize = 22;

    /// <summary>
    /// The font size for subtitle and secondary text (22 pixels).
    /// </summary>
    public const int SubTextFontSize = 22;
    
    /// <summary>
    /// The localhost IP address used for self-connecting.
    /// When hosting, the host automatically connects to their own server using this address.
    /// </summary>
    private const string LocalhostAddress = "127.0.0.1";
    
    /// <summary>
    /// Button name for the multiplayer menu button in the main menu.
    /// </summary>
    private const string MultiplayerButtonName = "StartMultiplayerButton";
    
    /// <summary>
    /// Localization key for the multiplayer button text.
    /// </summary>
    private const string MultiplayerButtonKey = "StartMultiplayerBtn";
    
    /// <summary>
    /// Localization sheet name for main menu text.
    /// </summary>
    private const string MainMenuSheet = "MainMenu";
    
    /// <summary>
    /// Name of the back button in save profile menu.
    /// </summary>
    private const string BackButtonName = "BackButton";

    /// <summary>
    /// Ratio for scaling UI elements based on screen height.
    /// Calculated as (actual screen height) / 1080, where 1080 is the reference resolution.
    /// </summary>
    public static readonly float ScreenHeightRatio = Screen.height / 1080f;

    #endregion

    #region Singleton Accessors

    /// <summary>
    /// Shorthand accessor for the GameManager singleton instance.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    private static GameManager GM => GameManager.instance;

    /// <summary>
    /// Shorthand accessor for the UIManager singleton instance.
    /// Hollow Knight's built-in UI manager for menu navigation.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    private static UIManager UM => UIManager.instance;

    /// <summary>
    /// Shorthand accessor for the InputHandler singleton instance.
    /// Handles keyboard/controller input for Hollow Knight.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    private static InputHandler IH => InputHandler.Instance;

    #endregion

    #region Static Fields

    /// <summary>
    /// Root GameObject containing all multiplayer UI elements.
    /// Persists across scene changes (DontDestroyOnLoad).
    /// </summary>
    internal static GameObject? UiGameObject;

    /// <summary>
    /// The chat box component for in-game text communication.
    /// Visible during gameplay for sending and receiving messages.
    /// </summary>
    internal static ChatBox InternalChatBox = null!;

    /// <summary>
    /// Event raised when text is submitted in the chat box.
    /// Subscribers process the message for network transmission.
    /// </summary>
    internal static event Action<string>? ChatInputEvent;

    #endregion

    #region Events

    /// <summary>
    /// Event raised when the user requests to start hosting a server from the UI.
    /// Parameters: address, port, username, transport type.
    /// </summary>
    public event Action<string, int, string, TransportType>? RequestServerStartHostEvent;

    /// <summary>
    /// Event raised when the user requests to stop hosting the current server.
    /// </summary>
    public event Action? RequestServerStopHostEvent;

    /// <summary>
    /// Event raised when the user requests to connect to a server from the UI.
    /// Parameters: address, port, username, transport type, is auto-connect (localhost).
    /// </summary>
    public event Action<string, int, string, TransportType, bool>? RequestClientConnectEvent;

    /// <summary>
    /// Event raised when the user requests to disconnect from the current server.
    /// </summary>
    public event Action? RequestClientDisconnectEvent;

    #endregion

    #region Fields

    /// <summary>
    /// Mod settings instance containing user preferences.
    /// Used to load saved connection details and display settings.
    /// </summary>
    private readonly ModSettings _modSettings;

    /// <summary>
    /// Network client instance for checking connection state.
    /// Used to determine if UI should show connected/disconnected state.
    /// </summary>
    private readonly NetClient _netClient;

    /// <summary>
    /// The connection interface UI component.
    /// Provides host/join controls and connection status display.
    /// </summary>
    private ConnectInterface _connectInterface = null!;

    /// <summary>
    /// Unity EventSystem for handling UI input and navigation.
    /// Required for button clicks and keyboard navigation.
    /// </summary>
    private EventSystem _eventSystem = null!;
    
    /// <summary>
    /// Component group controlling visibility of connection UI elements.
    /// Shown in main menu, hidden during gameplay.
    /// </summary>
    private ComponentGroup _connectGroup = null!;

    /// <summary>
    /// Component group for in-game UI elements (chat, ping).
    /// Shown during gameplay, hidden in menus and non-gameplay scenes.
    /// </summary>
    private ComponentGroup? _inGameGroup;
    
    /// <summary>
    /// The ping display interface showing network latency.
    /// Only visible when connected to a server.
    /// </summary>
    private PingInterface _pingInterface = null!;

    /// <summary>
    /// Original event triggers for the save selection screen's back button.
    /// Stored when overridden to return to multiplayer menu instead of main menu.
    /// Restored when exiting save selection.
    /// </summary>
    private List<EventTrigger.Entry> _originalBackTriggers = null!;

    /// <summary>
    /// Callback action executed when save slot selection finishes.
    /// Boolean parameter: true if save was selected, false if back button was pressed.
    /// </summary>
    private Action<bool>? _saveSlotSelectedAction;

    /// <summary>
    /// Whether the save slot selection menu is currently active/opening.
    /// Prevents re-entrancy and duplicate hook registration.
    /// </summary>
    private bool _isSlotSelectionActive;

    #endregion

    #region Properties

    /// <summary>
    /// Public accessor for the connect interface.
    /// Used by server manager to access MmsClient for HolePunch lobby cleanup.
    /// </summary>
    public ConnectInterface ConnectInterface => _connectInterface;

    /// <summary>
    /// Gets the chat box interface for sending and receiving messages.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if UiManager not initialized yet</exception>
    public IChatBox ChatBox {
        get {
            if (InternalChatBox == null) {
                throw new InvalidOperationException("UiManager is not initialized yet, cannot obtain chat box");
            }

            return InternalChatBox;
        }
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="UiManager"/> class.
    /// </summary>
    /// <param name="modSettings">Mod settings for loading user preferences</param>
    /// <param name="netClient">Network client for checking connection state</param>
    public UiManager(ModSettings modSettings, NetClient netClient) {
        _modSettings = modSettings;
        _netClient = netClient;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the UI manager by creating all UI components and registering event hooks.
    /// Should be called once during mod startup.
    /// </summary>
    /// <remarks>
    /// Initialization process:
    /// 1. Registers hooks for UI state changes (paused, scene changes)
    /// 2. Registers language hooks for multiplayer button text
    /// 3. Sets up event hooks for menu navigation
    /// 4. Creates all UI components (connection menu, chat, ping)
    /// 5. Registers input checking for hotkeys
    /// </remarks>
    public void Initialize() {
        RegisterEventHooks();
        SetupUi();
        
        MonoBehaviourUtil.Instance.OnUpdateEvent += CheckKeyBinds;
        
        // Hook to make sure that after game completion cutscenes we do not head to the main menu, but stay hosting/
        // connected to the server. Otherwise, if the host would go to the main menu, every other player would be
        // disconnected
        // On.CutsceneHelper.DoSceneLoad += (orig, self) => {
        //     if (!_netClient.IsConnected) {
        //         orig(self);
        //         return;
        //     }
        //
        //     var sceneName = self.gameObject.scene.name;
        //     
        //     Logger.Debug($"DoSceneLoad of CutsceneHelper for next scene type: {self.nextSceneType}, scene name: {sceneName}");
        //
        //     var toMainMenu = self.nextSceneType.Equals(CutsceneHelper.NextScene.MainMenu) 
        //                      || self.nextSceneType.Equals(CutsceneHelper.NextScene.MainMenuNoSave);
        //     if (self.nextSceneType.Equals(CutsceneHelper.NextScene.PermaDeathUnlock)) {
        //         toMainMenu |= GM.GetStatusRecordInt("RecPermadeathMode") != 0;
        //     }
        //
        //     if (toMainMenu) {
        //         if (PlayerData.instance.GetInt("permadeathMode") != 0) {
        //             // We are running Steel Soul mode, so we disconnect and go to main menu instead of reloading to
        //             // the last save point
        //             Logger.Debug("  NextSceneType is main menu, disconnecting because of Steel Soul");
        //             
        //             RequestClientDisconnectEvent?.Invoke();
        //             RequestServerStopHostEvent?.Invoke();
        //             
        //             orig(self);
        //             return;
        //         }
        //         
        //         Logger.Debug("  NextSceneType is main menu, transitioning to last save point instead");
        //         
        //         GameManager.instance.ContinueGame();
        //         return;
        //     }
        //
        //     orig(self);
        // };
        
    }

    /// <summary>
    /// Registers all event hooks for UI state management and menu integration.
    /// </summary>
    private void RegisterEventHooks() {
        RegisterUiStateHooks();
        RegisterLanguageHooks();
        RegisterMenuHooks();
    }

    /// <summary>
    /// Registers hooks for UI state changes (pause, scene changes).
    /// </summary>
    private void RegisterUiStateHooks() {
        EventHooks.UIManagerSetState += OnUiStateChanged;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
    }

    /// <summary>
    /// Registers language system hooks for multiplayer button text.
    /// </summary>
    private void RegisterLanguageHooks() {
        EventHooks.LanguageHas += OnLanguageHas;
        EventHooks.LanguageGet += OnLanguageGet;
    }

    /// <summary>
    /// Registers menu navigation hooks.
    /// </summary>
    private void RegisterMenuHooks() {
        EventHooks.UIManagerUIGoToMainMenu += TryAddMultiplayerScreen;
        EventHooks.UIManagerReturnToMainMenu += OnReturnToMainMenu;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles UI state changes to show/hide in-game UI elements.
    /// </summary>
    private void OnUiStateChanged(object _, UIState state) {
        var shouldShow = state != UIState.PAUSED && !SceneUtil.IsNonGameplayScene(SceneUtil.GetCurrentSceneName());
        _inGameGroup?.SetActive(shouldShow);
    }

    /// <summary>
    /// Handles scene changes to manage UI visibility and event system state.
    /// </summary>
    private void OnSceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene) {
        var isNonGameplayScene = SceneUtil.IsNonGameplayScene(newScene.name);

        if (_eventSystem != null) {
            _eventSystem.enabled = !isNonGameplayScene;
        }

        _inGameGroup?.SetActive(!isNonGameplayScene);
    }

    /// <summary>
    /// Handles language system queries for custom text keys.
    /// </summary>
    private bool? OnLanguageHas(string key, string sheet) =>
        IsMultiplayerButtonKey(key, sheet) ? true : null;

    /// <summary>
    /// Provides localized text for custom UI elements.
    /// </summary>
    private string? OnLanguageGet(string key, string sheet) =>
        IsMultiplayerButtonKey(key, sheet) ? "Start Multiplayer" : null;

    /// <summary>
    /// Checks if the provided key and sheet match the multiplayer button localization.
    /// </summary>
    private static bool IsMultiplayerButtonKey(string key, string sheet) =>
        key == MultiplayerButtonKey && sheet == MainMenuSheet;

    /// <summary>
    /// Handles return to main menu by disconnecting from server.
    /// </summary>
    private void OnReturnToMainMenu() {
        RequestClientDisconnectEvent?.Invoke();
        RequestServerStopHostEvent?.Invoke();
    }

    #endregion

    #region UI Setup

    /// <summary>
    /// Sets up all UI components including canvas, event system, and multiplayer interfaces.
    /// Creates the UI hierarchy and initializes all interface components.
    /// </summary>
    private void SetupUi() {
        Resources.FontManager.LoadFonts();
        
        CreateRootCanvas();
        CreateEventSystem();
        CreateUiComponents();
        RegisterInterfaceCallbacks();
        
        TryAddMultiplayerScreen();
    }

    /// <summary>
    /// Creates the root canvas for all multiplayer UI.
    /// </summary>
    private void CreateRootCanvas() {
        UiGameObject = new GameObject("SSMP_UI");

        var canvas = UiGameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var canvasScaler = UiGameObject.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasScaler.matchWidthOrHeight = 1f;
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        
        UiGameObject.AddComponent<GraphicRaycaster>();
        Object.DontDestroyOnLoad(UiGameObject);
    }

    /// <summary>
    /// Creates the Unity EventSystem for handling UI input.
    /// </summary>
    private void CreateEventSystem() {
        var eventSystemObj = new GameObject("SSMP_EventSystem");

        _eventSystem = eventSystemObj.AddComponent<EventSystem>();
        _eventSystem.sendNavigationEvents = true;
        _eventSystem.pixelDragThreshold = 10;

        eventSystemObj.AddComponent<StandaloneInputModule>();
        Object.DontDestroyOnLoad(eventSystemObj);
    }

    /// <summary>
    /// Creates all UI component groups and interfaces.
    /// </summary>
    private void CreateUiComponents() {
        var uiGroup = new ComponentGroup();

        CreateConnectionInterface(uiGroup);
        CreateInGameInterface(uiGroup);
    }

    /// <summary>
    /// Creates the connection interface for the multiplayer menu.
    /// </summary>
    private void CreateConnectionInterface(ComponentGroup parent) {
        _connectGroup = new ComponentGroup(false, parent);
        _connectInterface = new ConnectInterface(_modSettings, _connectGroup);
    }

    /// <summary>
    /// Creates the in-game interface (chat and ping display).
    /// </summary>
    private void CreateInGameInterface(ComponentGroup parent) {
        _inGameGroup = new ComponentGroup(parent: parent);

        var infoBoxGroup = new ComponentGroup(parent: _inGameGroup);
        InternalChatBox = new ChatBox(infoBoxGroup, _modSettings);
        InternalChatBox.ChatInputEvent += input => ChatInputEvent?.Invoke(input);

        var pingGroup = new ComponentGroup(parent: _inGameGroup);
        _pingInterface = new PingInterface(pingGroup, _modSettings, _netClient);
    }

    /// <summary>
    /// Registers callbacks for interface button events.
    /// </summary>
    private void RegisterInterfaceCallbacks() {
        _connectInterface.StartHostButtonPressed += OnStartHostRequested;
        _connectInterface.ConnectButtonPressed += OnConnectRequested;
    }

    /// <summary>
    /// Handles host button press by opening save selection.
    /// </summary>
    private void OnStartHostRequested(string address, int port, string username, TransportType transportType) {
        OpenSaveSlotSelection(saveSelected => {
            if (!saveSelected) return;

            RequestServerStartHostEvent?.Invoke(address, port, username, transportType);
            RequestClientConnectEvent?.Invoke(LocalhostAddress, port, username, transportType, true);
        });
    }

    /// <summary>
    /// Handles connect button press.
    /// </summary>
    private void OnConnectRequested(string address, int port, string username, TransportType transportType) =>
        RequestClientConnectEvent?.Invoke(address, port, username, transportType, false);

    #endregion

    #region Public Methods

    /// <summary>
    /// Enters the game from the multiplayer menu with current PlayerData.
    /// Assumes PlayerData is already populated with save file data.
    /// </summary>
    /// <param name="newGame">True to start a new game, false to continue existing save</param>
    public void EnterGameFromMultiplayerMenu(bool newGame) {
        IH.StopUIInput();
        _connectGroup.SetActive(false);
        PlayMenuTransitionAudio();

        Logger.Debug($"Entering game from MP menu for {(newGame ? "new" : "continued")} game");
        
        if (newGame) {
            GM.StartCoroutine(GM.RunStartNewGame());
        } else {
            GM.ContinueGame();
        }
    }

    /// <summary>
    /// Plays audio for menu transition to game.
    /// </summary>
    private void PlayMenuTransitionAudio() {
        UM.uiAudioPlayer.PlayStartGame();
        MenuStyles.Instance?.StopAudio();
    }

    /// <summary>
    /// Returns to the main menu from in-game.
    /// Used when player disconnects from the current server.
    /// </summary>
    /// <param name="save">Whether to save the game before returning to menu</param>
    public void ReturnToMainMenuFromGame(bool save = true) =>
        GM.StartCoroutine(GM.ReturnToMainMenu(save));

    /// <summary>
    /// Callback invoked when client successfully connects to a server.
    /// Updates UI to show connected state and enables ping display.
    /// </summary>
    public void OnSuccessfulConnect() {
        _connectInterface.OnSuccessfulConnect();
        _pingInterface.SetEnabled(true);
    }

    /// <summary>
    /// Callback invoked when client fails to connect to a server.
    /// Updates UI to show error message based on failure reason.
    /// </summary>
    /// <param name="result">The reason for connection failure</param>
    public void OnFailedConnect(ConnectionFailedResult result) =>
        _connectInterface.OnFailedConnect(result);

    /// <summary>
    /// Callback invoked when client disconnects from the server.
    /// Updates UI to show disconnected state and disables ping display.
    /// </summary>
    public void OnClientDisconnect() {
        _connectInterface.OnClientDisconnect();
        _pingInterface.SetEnabled(false);
    }

    #endregion

    #region Save Slot Selection

    /// <summary>
    /// Opens the save slot selection screen from the multiplayer menu.
    /// Used when hosting to select which save file to load.
    /// </summary>
    /// <param name="callback">
    /// Action executed when save selection finishes.
    /// Boolean parameter: true if save selected, false if back pressed.
    /// </param>
    public void OpenSaveSlotSelection(Action<bool>? callback = null) {
        if (_isSlotSelectionActive) {
            Logger.Info("Save slot selection already active, ignoring request");
            return;
        }

        _isSlotSelectionActive = true;
        _saveSlotSelectedAction = CreateSaveSlotCallback(callback);
        
        // Ensure we don't have duplicate hooks
        UnregisterSaveSlotHooks();
        RegisterSaveSlotHooks();
        
        UM.StartCoroutine(GoToSaveMenu());
    }

    /// <summary>
    /// Creates a wrapped callback that cleans up event hooks.
    /// </summary>
    private Action<bool> CreateSaveSlotCallback(Action<bool>? callback) {
        return saveSelected => {
            callback?.Invoke(saveSelected);
            UnregisterSaveSlotHooks();
            // Note: _isSlotSelectionActive is NOT reset here because if save is selected, 
            // the game loads and we don't want to allow reopening the menu.
            // It will be reset if Back is pressed.
            if (!saveSelected) {
                _isSlotSelectionActive = false;
            }
        };
    }

    /// <summary>
    /// Registers event hooks for save slot selection.
    /// </summary>
    private void RegisterSaveSlotHooks() {
        EventHooks.GameManagerStartNewGame += OnSaveSlotSelected;
        EventHooks.GameManagerContinueGame += OnSaveSlotSelected;
    }

    /// <summary>
    /// Removes save slot event hooks.
    /// </summary>
    private void UnregisterSaveSlotHooks() {
        EventHooks.GameManagerStartNewGame -= OnSaveSlotSelected;
        EventHooks.GameManagerContinueGame -= OnSaveSlotSelected;
    }

    /// <summary>
    /// Callback for when a save slot is selected (new game or continue).
    /// </summary>
    private void OnSaveSlotSelected() => _saveSlotSelectedAction?.Invoke(true);

    #endregion

    #region Multiplayer Button

    /// <summary>
    /// Attempts to add the "Start Multiplayer" button to the main menu.
    /// Does nothing if button already exists.
    /// </summary>
    private void TryAddMultiplayerScreen() {
        if (!ValidateMainMenuExists()) return;

        var existingButton = FindMultiplayerButton();
        if (existingButton != null) {
            FixMultiplayerButtonNavigation(existingButton);
            return;
        }

        CreateMultiplayerButton();
    }

    /// <summary>
    /// Validates that the main menu exists and is accessible.
    /// </summary>
    private bool ValidateMainMenuExists() {
        if (UM.mainMenuButtons?.gameObject != null) return true;
        
        Logger.Info("Main menu not available yet");
        return false;
    }

    /// <summary>
    /// Finds the existing multiplayer button if it exists.
    /// </summary>
    private GameObject? FindMultiplayerButton() {
        var button = UM.mainMenuButtons.gameObject.FindGameObjectInChildren(MultiplayerButtonName);
        if (button != null) {
            Logger.Info("Multiplayer button already exists");
        }
        return button;
    }

    /// <summary>
    /// Creates the multiplayer button by cloning the start game button.
    /// </summary>
    private void CreateMultiplayerButton() {
        var startGameBtn = UM.mainMenuButtons.startButton?.gameObject;
        if (startGameBtn == null) {
            Logger.Info("Start game button not found");
            return;
        }

        var multiplayerBtn = Object.Instantiate(startGameBtn, startGameBtn.transform.parent);
        ConfigureMultiplayerButton(multiplayerBtn);
        FixMultiplayerButtonNavigation(multiplayerBtn);
        
        UM.StartCoroutine(FixNavigationAfterInput(multiplayerBtn));
    }

    /// <summary>
    /// Configures the multiplayer button properties.
    /// </summary>
    private void ConfigureMultiplayerButton(GameObject button) {
        button.name = MultiplayerButtonName;
        button.transform.SetSiblingIndex(1);

        ConfigureButtonLocalization(button);
        ConfigureButtonTriggers(button);
    }

    /// <summary>
    /// Configures button text via localization system.
    /// </summary>
    private void ConfigureButtonLocalization(GameObject button) {
        var autoLocalize = button.GetComponent<AutoLocalizeTextUI>();
        if (autoLocalize == null) return;

        autoLocalize.textKey = MultiplayerButtonKey;
        autoLocalize.sheetTitle = MainMenuSheet;
        autoLocalize.OnValidate();
        autoLocalize.RefreshTextFromLocalization();
    }

    /// <summary>
    /// Configures button click event triggers.
    /// </summary>
    private void ConfigureButtonTriggers(GameObject button) {
        var eventTrigger = button.GetComponent<EventTrigger>();
        if (eventTrigger == null) return;

        eventTrigger.triggers.Clear();
        AddButtonTriggers(eventTrigger, () => UM.StartCoroutine(GoToMultiplayerMenu()));
    }

    /// <summary>
    /// Fixes navigation after input system is ready.
    /// </summary>
    private IEnumerator FixNavigationAfterInput(GameObject button) {
        yield return new WaitUntil(() => IH.acceptingInput);
        FixMultiplayerButtonNavigation(button);
    }

    /// <summary>
    /// Fixes keyboard/controller navigation for the multiplayer button.
    /// Ensures proper up/down navigation between menu buttons.
    /// </summary>
    private void FixMultiplayerButtonNavigation(GameObject buttonObject) {
        var multiplayerBtn = buttonObject.GetComponent<MenuButton>();
        if (multiplayerBtn == null) return;

        var startBtn = UM.mainMenuButtons.startButton;
        var optionsBtn = UM.mainMenuButtons.optionsButton;

        SetNavigation(startBtn, selectOnDown: multiplayerBtn);
        SetNavigation(optionsBtn, selectOnUp: multiplayerBtn);
        SetNavigation(multiplayerBtn, selectOnUp: startBtn);
    }

    /// <summary>
    /// Helper method to set navigation for a button.
    /// </summary>
    private void SetNavigation(MenuButton button, MenuButton? selectOnUp = null, MenuButton? selectOnDown = null) {
        if (button == null) return;

        var nav = button.navigation;
        if (selectOnUp != null) nav.selectOnUp = selectOnUp;
        if (selectOnDown != null) nav.selectOnDown = selectOnDown;
        button.navigation = nav;
    }

    #endregion

    #region Menu Navigation

    /// <summary>
    /// Coroutine to transition from main menu to multiplayer connection menu.
    /// </summary>
    private IEnumerator GoToMultiplayerMenu() {
        IH.StopUIInput();
        yield return FadeOutCurrentMenu();
        IH.StartUIInput();
        ShowMultiplayerMenu();
    }

    /// <summary>
    /// Fades out the current menu based on state.
    /// </summary>
    private IEnumerator FadeOutCurrentMenu() {
        switch (UM.menuState) {
            case MainMenuState.MAIN_MENU:
                UM.StartCoroutine(UM.FadeOutSprite(UM.gameTitle));
                UM.subtitleFSM.SendEvent("FADE OUT");
                yield return UM.StartCoroutine(UM.FadeOutCanvasGroup(UM.mainMenuScreen));
                break;
            
            case MainMenuState.SAVE_PROFILES:
                yield return UM.StartCoroutine(UM.HideSaveProfileMenu(false));
                break;
        }
    }

    /// <summary>
    /// Shows the multiplayer connection interface.
    /// </summary>
    private void ShowMultiplayerMenu() {
        _connectGroup.SetActive(true);
        _connectInterface.SetMenuActive(true);
    }

    /// <summary>
    /// Hides the multiplayer connection interface.
    /// </summary>
    private void HideMultiplayerMenu() {
        _connectGroup.SetActive(false);
        _connectInterface.SetMenuActive(false);
    }

    /// <summary>
    /// Coroutine to transition from multiplayer menu to save selection screen.
    /// </summary>
    private IEnumerator GoToSaveMenu() {
        HideMultiplayerMenu();
        yield return UM.HideCurrentMenu();
        
        // Safety check before verifying game UI state
        if (UM != null) {
             yield return UM.GoToProfileMenu();
             OverrideSaveMenuBackButton();
        } else {
             Logger.Error("UIManager instance is null, cannot go to profile menu");
             _isSlotSelectionActive = false;
        }
    }

    /// <summary>
    /// Overrides the save menu back button to return to multiplayer menu.
    /// </summary>
    private void OverrideSaveMenuBackButton() {
        var backButton = FindSaveMenuBackButton();
        if (backButton == null) return;

        var eventTrigger = backButton.GetComponent<EventTrigger>();
        if (eventTrigger == null) return;

        _originalBackTriggers = eventTrigger.triggers;
        eventTrigger.triggers = [];
        
        AddButtonTriggers(eventTrigger, OnSaveMenuBackPressed);
    }

    /// <summary>
    /// Finds the back button in the save profile menu.
    /// </summary>
    private GameObject? FindSaveMenuBackButton() {
        var backButton = UM.saveProfileControls?.gameObject.FindGameObjectInChildren(BackButtonName);
        if (backButton == null) {
            Logger.Info("Save profiles back button not found");
        }
        return backButton;
    }

    /// <summary>
    /// Handles back button press in save menu.
    /// </summary>
    private void OnSaveMenuBackPressed() {
        UnregisterSaveSlotHooks();
        _isSlotSelectionActive = false;
        _saveSlotSelectedAction?.Invoke(false);
        
        UM.StartCoroutine(GoToMultiplayerMenu());
        RestoreSaveMenuBackButton();
    }

    /// <summary>
    /// Restores original back button behavior for save menu.
    /// </summary>
    private void RestoreSaveMenuBackButton() {
        var backButton = FindSaveMenuBackButton();
        var eventTrigger = backButton?.GetComponent<EventTrigger>();
        if (eventTrigger != null) {
            eventTrigger.triggers = _originalBackTriggers;
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Adds click and submit triggers to a button.
    /// </summary>
    private void AddButtonTriggers(EventTrigger eventTrigger, Action action) {
        AddTrigger(eventTrigger, EventTriggerType.Submit, action);
        AddTrigger(eventTrigger, EventTriggerType.PointerClick, action);
    }

    /// <summary>
    /// Adds a single trigger to an EventTrigger component.
    /// </summary>
    private void AddTrigger(EventTrigger eventTrigger, EventTriggerType triggerType, Action action) {
        var entry = new EventTrigger.Entry { eventID = triggerType };
        entry.callback.AddListener(_ => action.Invoke());
        eventTrigger.triggers.Add(entry);
    }

    /// <summary>
    /// Checks for hotkey presses to exit multiplayer menu.
    /// Called every frame by Unity's update system.
    /// </summary>
    private void CheckKeyBinds() {
        if (_connectGroup.IsActive() && InputHandler.Instance.inputActions.Pause.IsPressed) {
            HandlePauseKey();
        }
    }

    /// <summary>
    /// Handles pause key press to return to main menu.
    /// </summary>
    private void HandlePauseKey() {
        UM.StartCoroutine(UM.HideCurrentMenu());
        UM.UIGoToMainMenu();
        HideMultiplayerMenu();
    }

    #endregion
}

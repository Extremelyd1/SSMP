using System;
using SSMP.Game;
using SSMP.Game.Settings;
using SSMP.Networking.Client;
using SSMP.Ui.Component;
using Steamworks;
using SSMP.Networking.Transport.Common;
using SSMP.Networking;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Ui;

/// <summary>
/// Class for creating and managing the connect interface with tabbed navigation.
/// </summary>
internal class ConnectInterface {
    #region Layout Constants

    /// <summary>
    /// The indent of some text elements.
    /// </summary>
    private const float TextIndentWidth = 5f;

    /// <summary>
    /// The standard width for content elements in the interface.
    /// </summary>
    private const float ContentWidth = 410f;

    /// <summary>
    /// The standard height for interactive elements (buttons, inputs).
    /// </summary>
    private const float UniformHeight = 50f;

    /// <summary>
    /// The initial X position for the UI elements.
    /// </summary>
    private const float InitialX = 960f;

    /// <summary>
    /// The initial Y position for the UI elements.
    /// </summary>
    private const float InitialY = 780f;

    /// <summary>
    /// The width of the header text.
    /// </summary>
    private const float HeaderWidth = 400f;

    /// <summary>
    /// The height of the header text.
    /// </summary>
    private const float HeaderHeight = 40f;

    /// <summary>
    /// The vertical spacing between the header and the notch.
    /// </summary>
    private const float HeaderToNotchSpacing = 45f;

    /// <summary>
    /// The vertical spacing between the notch and the background panel.
    /// </summary>
    private const float NotchToPanelSpacing = 35f;

    /// <summary>
    /// The vertical spacing between the background panel start and the first element.
    /// </summary>
    private const float PanelPaddingTop = 25f;

    /// <summary>
    /// The height of label texts.
    /// </summary>
    private const float LabelHeight = 20f;

    /// <summary>
    /// The vertical spacing between a label and its input.
    /// </summary>
    private const float LabelToInputSpacing = 44f;

    /// <summary>
    /// The vertical spacing after an input field.
    /// </summary>
    private const float InputSpacing = 40f;

    /// <summary>
    /// The width of the tab buttons.
    /// </summary>
    private const float TabButtonWidth = 150f;

    /// <summary>
    /// The vertical spacing after the tab buttons.
    /// </summary>
    private const float TabSpacing = 70f;

    /// <summary>
    /// The height of the description text.
    /// </summary>
    private const float DescriptionHeight = 40f;

    /// <summary>
    /// The vertical spacing after the join session header.
    /// </summary>
    private const float JoinHeaderSpacing = 45f;

    /// <summary>
    /// The vertical spacing after the join session description.
    /// </summary>
    private const float JoinDescSpacing = 95f;

    /// <summary>
    /// The vertical spacing between the Lobby ID label and input.
    /// </summary>
    private const float LobbyIdLabelSpacing = 54f;

    /// <summary>
    /// The vertical spacing after the Steam connected text.
    /// </summary>
    private const float SteamTextSpacing = 40f;

    /// <summary>
    /// The vertical spacing between Steam buttons.
    /// </summary>
    private const float SteamButtonSpacing = 10f;

    /// <summary>
    /// The vertical spacing between the server address label and input.
    /// </summary>
    private const float ServerAddressLabelSpacing = 54f;

    /// <summary>
    /// The vertical spacing after the server address input.
    /// </summary>
    private const float ServerAddressInputSpacing = 28f;

    /// <summary>
    /// The vertical spacing between the port label and input.
    /// </summary>
    private const float PortLabelSpacing = 54f;

    /// <summary>
    /// The vertical offset for the feedback text.
    /// </summary>
    private const float FeedbackTextOffset = 320f;



    #endregion

    #region Text Constants



    /// <summary>
    /// The text of the connection button while connecting.
    /// </summary>
    private const string ConnectingText = "Connecting...";

    /// <summary>
    /// The text for the main header.
    /// </summary>
    private const string HeaderText = "M U L T I P L A Y E R";

    /// <summary>
    /// The text for the identity section label.
    /// </summary>
    private const string IdentityLabelText = "Identity";

    /// <summary>
    /// The placeholder text for the username input.
    /// </summary>
    private const string UsernamePlaceholder = "Enter Username";

    /// <summary>
    /// The text for the Matchmaking tab.
    /// </summary>
    private const string MatchmakingTabText = "Matchmaking";

    /// <summary>
    /// The text for the Steam tab.
    /// </summary>
    private const string SteamTabText = "Steam";

    /// <summary>
    /// The text for the Direct IP tab.
    /// </summary>
    private const string DirectIpTabText = "Direct IP";

    /// <summary>
    /// The text for the join session header.
    /// </summary>
    private const string JoinSessionText = "JOIN SESSION";

    /// <summary>
    /// The text for the join session description.
    /// </summary>
    private const string JoinSessionDescText = "Enter the unique Lobby ID\nto join an existing session.";

    /// <summary>
    /// The text for the Lobby ID label.
    /// </summary>
    private const string LobbyIdLabelText = "Lobby ID";

    /// <summary>
    /// The placeholder text for the Lobby ID input.
    /// </summary>
    private const string LobbyIdPlaceholder = "e.g. 8x92-AC44";

    /// <summary>
    /// The text for the lobby connect button.
    /// </summary>
    private const string LobbyConnectButtonText = "CONNECT TO LOBBY";

    /// <summary>
    /// The text displayed when connected to Steam Workshop.
    /// </summary>
    private const string SteamConnectedText = "Connected to Steam Workshop";

    /// <summary>
    /// The text for the create lobby button.
    /// </summary>
    private const string CreateLobbyButtonText = "+ CREATE LOBBY";

    /// <summary>
    /// The text for the browse lobby button.
    /// </summary>
    private const string BrowseLobbyButtonText = "☰ BROWSE PUBLIC LOBBIES";

    /// <summary>
    /// The text for the join friend button.
    /// </summary>
    private const string JoinFriendButtonText = "→ JOIN FRIEND (INVITE)";

    /// <summary>
    /// The text for the server address label.
    /// </summary>
    private const string ServerAddressLabelText = "Server Address";

    /// <summary>
    /// The placeholder text for the server address input.
    /// </summary>
    private const string ServerAddressPlaceholder = "127.0.0.1";

    /// <summary>
    /// The text for the port label.
    /// </summary>
    private const string PortLabelText = "Port";

    /// <summary>
    /// The placeholder text for the port input.
    /// </summary>
    private const string PortPlaceholder = "26960";

    /// <summary>
    /// The text for the direct connect button.
    /// </summary>
    private const string DirectConnectButtonText = "CONNECT";

    /// <summary>
    /// The text for the host button.
    /// </summary>
    private const string HostButtonText = "HOST";

    #endregion

    #region Message Constants



    /// <summary>
    /// Error message when address is missing.
    /// </summary>
    private const string ErrorEnterAddress = "Failed to connect:\nYou must enter an address";

    /// <summary>
    /// Error message when port is invalid.
    /// </summary>
    private const string ErrorEnterValidPort = "Failed to connect:\nYou must enter a valid port";

    /// <summary>
    /// Error message when port is invalid for hosting.
    /// </summary>
    private const string ErrorEnterValidPortHost = "Failed to host:\nYou must enter a valid port";



    /// <summary>
    /// Message displayed upon successful connection.
    /// </summary>
    private const string MsgConnected = "Successfully connected";

    /// <summary>
    /// Error message for invalid addons.
    /// </summary>
    private const string ErrorInvalidAddons = "Failed to connect:\nInvalid addons";

    /// <summary>
    /// Error message for internal errors.
    /// </summary>
    private const string ErrorInternal = "Failed to connect:\nInternal error";

    /// <summary>
    /// Error message for connection timeout.
    /// </summary>
    private const string ErrorTimeout = "Failed to connect:\nConnection timed out";

    /// <summary>
    /// Error message for unknown connection failures.
    /// </summary>
    private const string ErrorUnknown = "Failed to connect:\nUnknown reason";

    #endregion

    #region Fields

    /// <summary>
    /// The mod settings.
    /// </summary>
    private readonly ModSettings _modSettings;

    /// <summary>
    /// The username input component.
    /// </summary>
    private readonly IInputComponent _usernameInput;

    /// <summary>
    /// The feedback text component.
    /// </summary>
    private readonly ITextComponent _feedbackText;

    /// <summary>
    /// The background panel GameObject for the menu.
    /// </summary>
    private readonly GameObject _backgroundPanel;

    /// <summary>
    /// The component group containing all background panel elements.
    /// </summary>
    private readonly ComponentGroup _backgroundGroup;

    /// <summary>
    /// The coroutine that hides the feedback text after a delay.
    /// </summary>
    private Coroutine? _feedbackHideCoroutine;

    /// <summary>
    /// The glowing notch GameObject displayed under the header.
    /// </summary>
    private readonly GameObject _glowingNotch;

    /// <summary>
    /// The matchmaking tab button component.
    /// </summary>
    private readonly TabButtonComponent _matchmakingTab;

    /// <summary>
    /// The Steam tab button component.
    /// </summary>
    private readonly TabButtonComponent? _steamTab;

    /// <summary>
    /// The Direct IP tab button component.
    /// </summary>
    private readonly TabButtonComponent _directIpTab;

    /// <summary>
    /// The component group containing matchmaking tab content.
    /// </summary>
    private readonly ComponentGroup _matchmakingGroup;

    /// <summary>
    /// The component group containing Steam tab content.
    /// </summary>
    private readonly ComponentGroup? _steamGroup;

    /// <summary>
    /// The component group containing Direct IP tab content.
    /// </summary>
    private readonly ComponentGroup _directIpGroup;

    /// <summary>
    /// The lobby ID input component in the matchmaking tab.
    /// </summary>
    private readonly IInputComponent _lobbyIdInput;

    /// <summary>
    /// The lobby connect button component in the matchmaking tab.
    /// </summary>
    private readonly IButtonComponent _lobbyConnectButton;

    /// <summary>
    /// The create lobby button component in the Steam tab.
    /// </summary>
    private readonly IButtonComponent _createLobbyButton;

    /// <summary>
    /// The browse lobby button component in the Steam tab.
    /// </summary>
    private readonly IButtonComponent _browseLobbyButton;

    /// <summary>
    /// The join friend button component in the Steam tab.
    /// </summary>
    private readonly IButtonComponent _joinFriendButton;

    /// <summary>
    /// The address input component in the Direct IP tab.
    /// </summary>
    private readonly IInputComponent _addressInput;

    /// <summary>
    /// The port input component in the Direct IP tab.
    /// </summary>
    private readonly IInputComponent _portInput;

    /// <summary>
    /// The direct connect button component in the Direct IP tab.
    /// </summary>
    private readonly IButtonComponent _directConnectButton;

    /// <summary>
    /// The server/host button component in the Direct IP tab.
    /// </summary>
    private readonly IButtonComponent _serverButton;

    /// <summary>
    /// Current active tab.
    /// </summary>
    private enum Tab { Matchmaking, Steam, DirectIp }

    /// <summary>
    /// The currently active tab in the interface.
    /// </summary>
    private Tab _activeTab = Tab.Matchmaking;



    /// <summary>
    /// Event that is executed when the connect button is pressed.
    /// </summary>
    public event Action<string, int, string, TransportType>? ConnectButtonPressed;

    /// <summary>
    /// Event that is executed when the start hosting button is pressed.
    /// </summary>
    public event Action<string, int, string, TransportType>? StartHostButtonPressed;

    #endregion

    /// <summary>
    /// Initializes a new instance of the ConnectInterface class.
    /// </summary>
    /// <param name="modSettings">The mod settings.</param>
    /// <param name="connectGroup">The component group for the connect interface.</param>
    public ConnectInterface(ModSettings modSettings, ComponentGroup connectGroup) {
        _modSettings = modSettings;
        
        // Subscribe to Steam lobby events
        //(UNCOMMENT)SteamManager.LobbyCreatedEvent += OnSteamLobbyCreated;
        //(UNCOMMENT)SteamManager.LobbyListReceivedEvent += OnLobbyListReceived;
        //(UNCOMMENT)SteamManager.LobbyJoinedEvent += OnLobbyJoined;

        var x = InitialX;
        var y = InitialY;

        // Header
        new TextComponent(connectGroup, new Vector2(x, y), new Vector2(HeaderWidth, HeaderHeight), 
            HeaderText, 32, alignment: TextAnchor.MiddleCenter);
        y -= HeaderToNotchSpacing;
        
        // Create white notch with glow effect under header
        _glowingNotch = ConnectInterfaceHelpers.CreateGlowingNotch(x, y);
        y -= NotchToPanelSpacing;

        // Background panel
        _backgroundPanel = ConnectInterfaceHelpers.CreateBackgroundPanel(x, y);
        _backgroundGroup = new ComponentGroup(parent: connectGroup);
        y -= PanelPaddingTop;

        // Username section
        new TextComponent(_backgroundGroup, new Vector2(x + TextIndentWidth, y), 
            new Vector2(ContentWidth, LabelHeight), IdentityLabelText, UiManager.NormalFontSize, alignment: TextAnchor.MiddleLeft);
        y -= LabelToInputSpacing;
        _usernameInput = new InputComponent(_backgroundGroup, new Vector2(x, y), new Vector2(ContentWidth, UniformHeight),
            _modSettings.Username, UsernamePlaceholder, characterLimit: 32,
            onValidateInput: (_, _, addedChar) => char.IsLetterOrDigit(addedChar) ? addedChar : '\0');
        y -= UniformHeight + InputSpacing;

        // Tab buttons
        var tabY = y;
        var tabWidth = TabButtonWidth;
        _matchmakingTab = ConnectInterfaceHelpers.CreateTabButton(_backgroundGroup, x - tabWidth, tabY, tabWidth, MatchmakingTabText, () => SwitchTab(Tab.Matchmaking));
        
        if (true){
        //(UNCOMMENT)if (SteamManager.IsInitialized) {
            _steamTab = ConnectInterfaceHelpers.CreateTabButton(_backgroundGroup, x, tabY, tabWidth, SteamTabText, () => SwitchTab(Tab.Steam));
        }
        
        _directIpTab = ConnectInterfaceHelpers.CreateTabButton(_backgroundGroup, x + tabWidth, tabY, tabWidth, DirectIpTabText, () => SwitchTab(Tab.DirectIp));
        y -= TabSpacing;

        var contentY = y;

        // Matchmaking tab
        _matchmakingGroup = new ComponentGroup(parent: _backgroundGroup);
        var matchY = contentY;
        new TextComponent(_matchmakingGroup, new Vector2(x, matchY), new Vector2(ContentWidth, LabelHeight), 
            JoinSessionText, 18, alignment: TextAnchor.MiddleCenter);
        matchY -= JoinHeaderSpacing;
        new TextComponent(_matchmakingGroup, new Vector2(x, matchY), new Vector2(ContentWidth, DescriptionHeight), 
            JoinSessionDescText, UiManager.SubTextFontSize, alignment: TextAnchor.MiddleCenter);
        matchY -= JoinDescSpacing;
        new TextComponent(_matchmakingGroup, new Vector2(x + TextIndentWidth, matchY), 
            new Vector2(ContentWidth, LabelHeight), LobbyIdLabelText, UiManager.NormalFontSize, alignment: TextAnchor.MiddleLeft);
        matchY -= LobbyIdLabelSpacing;
        _lobbyIdInput = new InputComponent(_matchmakingGroup, new Vector2(x, matchY), new Vector2(ContentWidth, UniformHeight), "", LobbyIdPlaceholder, characterLimit: 12);
        matchY -= UniformHeight + InputSpacing;
        _lobbyConnectButton = new ButtonComponent(_matchmakingGroup, new Vector2(x, matchY), new Vector2(ContentWidth, UniformHeight), LobbyConnectButtonText,
            Resources.TextureManager.ButtonBg, Resources.FontManager.UIFontRegular, UiManager.NormalFontSize);
        _lobbyConnectButton.SetOnPress(OnLobbyConnectButtonPressed);

        // Steam tab
        //(UNCOMMENT)if (SteamManager.IsInitialized) {
         if (true){
            _steamGroup = new ComponentGroup(activeSelf: false, parent: _backgroundGroup);
            var steamY = contentY;
            
            new TextComponent(_steamGroup, new Vector2(x, steamY), new Vector2(ContentWidth, LabelHeight), 
                SteamConnectedText, UiManager.SubTextFontSize, alignment: TextAnchor.MiddleCenter);
            steamY -= SteamTextSpacing;
            
            _createLobbyButton = new ButtonComponent(_steamGroup, new Vector2(x, steamY), 
                new Vector2(ContentWidth, UniformHeight), CreateLobbyButtonText, 
                Resources.TextureManager.ButtonBg, Resources.FontManager.UIFontRegular, UiManager.NormalFontSize);
            _createLobbyButton.SetOnPress(OnCreateLobbyButtonPressed);
            steamY -= UniformHeight + SteamButtonSpacing;
            
            _browseLobbyButton = new ButtonComponent(_steamGroup, new Vector2(x, steamY), 
                new Vector2(ContentWidth, UniformHeight), BrowseLobbyButtonText, 
                Resources.TextureManager.ButtonBg, Resources.FontManager.UIFontRegular, UiManager.NormalFontSize);
            _browseLobbyButton.SetOnPress(OnBrowseLobbyButtonPressed);
            steamY -= UniformHeight + SteamButtonSpacing;
            
            _joinFriendButton = new ButtonComponent(_steamGroup, new Vector2(x, steamY), 
                new Vector2(ContentWidth, UniformHeight), JoinFriendButtonText, 
                Resources.TextureManager.ButtonBg, Resources.FontManager.UIFontRegular, UiManager.NormalFontSize);
            _joinFriendButton.SetOnPress(OnJoinFriendButtonPressed);
        }

        // Direct IP tab
        _directIpGroup = new ComponentGroup(activeSelf: false, parent: _backgroundGroup);
        var directY = contentY;
        new TextComponent(_directIpGroup, new Vector2(x + TextIndentWidth, directY), 
            new Vector2(ContentWidth, LabelHeight), ServerAddressLabelText, UiManager.NormalFontSize, alignment: TextAnchor.MiddleLeft);
        directY -= ServerAddressLabelSpacing;
        _addressInput = new IpInputComponent(_directIpGroup, new Vector2(x, directY), new Vector2(ContentWidth, UniformHeight), _modSettings.ConnectAddress, ServerAddressPlaceholder);
        directY -= UniformHeight + ServerAddressInputSpacing;
        new TextComponent(_directIpGroup, new Vector2(x + TextIndentWidth, directY), 
            new Vector2(ContentWidth, LabelHeight), PortLabelText, UiManager.NormalFontSize, alignment: TextAnchor.MiddleLeft);
        directY -= PortLabelSpacing;
        var joinPort = _modSettings.ConnectPort;
        _portInput = new PortInputComponent(_directIpGroup, new Vector2(x, directY), new Vector2(ContentWidth, UniformHeight), joinPort == -1 ? "" : joinPort.ToString(), PortPlaceholder);
        directY -= UniformHeight + InputSpacing;
        
        // Two buttons side by side
        var buttonWidth = (ContentWidth - 10f) / 2f;
        _directConnectButton = new ButtonComponent(_directIpGroup, new Vector2(x - buttonWidth / 2f - 5f, directY), 
            new Vector2(buttonWidth, UniformHeight), DirectConnectButtonText, 
            Resources.TextureManager.ButtonBg, Resources.FontManager.UIFontRegular, UiManager.NormalFontSize);
        _directConnectButton.SetOnPress(OnDirectConnectButtonPressed);
        
        _serverButton = new ButtonComponent(_directIpGroup, new Vector2(x + buttonWidth / 2f + 5f, directY), 
            new Vector2(buttonWidth, UniformHeight), HostButtonText, 
            Resources.TextureManager.ButtonBg, Resources.FontManager.UIFontRegular, UiManager.NormalFontSize);
        _serverButton.SetOnPress(OnStartButtonPressed);

        // Feedback text
        _feedbackText = new TextComponent(_backgroundGroup, new Vector2(x, contentY - FeedbackTextOffset), 
            new Vector2(ContentWidth, LabelHeight), new Vector2(0.5f, 1f), "", UiManager.SubTextFontSize, alignment: TextAnchor.UpperCenter);
        _feedbackText.SetActive(false);

        ConnectInterfaceHelpers.ReparentComponentGroup(_backgroundGroup, _backgroundPanel);
        ConnectInterfaceHelpers.PositionTabButtonsFixed(_backgroundPanel, _matchmakingTab, _steamTab, _directIpTab);
        SwitchTab(Tab.Matchmaking);
    }



    /// <summary>
    /// Switches to a different tab.
    /// </summary>
    /// <param name="tab">The tab to switch to.</param>
    private void SwitchTab(Tab tab) {
        _activeTab = tab;
        
        // Update tab button states
        _matchmakingTab.SetTabActive(tab == Tab.Matchmaking);
        _steamTab?.SetTabActive(tab == Tab.Steam);
        _directIpTab.SetTabActive(tab == Tab.DirectIp);
        
        // Show/hide content groups
        _matchmakingGroup.SetActive(tab == Tab.Matchmaking);
        _steamGroup?.SetActive(tab == Tab.Steam);
        _directIpGroup.SetActive(tab == Tab.DirectIp);
    }

    /// <summary>
    /// Sets whether the multiplayer menu (including background panel) is active.
    /// </summary>
    /// <param name="active">Whether the menu should be active.</param>
    public void SetMenuActive(bool active) {
        _backgroundPanel.SetActive(active);
        _glowingNotch.SetActive(active);
    }

    /// <summary>
    /// Callback method for when the lobby connect button is pressed.
    /// </summary>
    private void OnLobbyConnectButtonPressed() {
        //(UNCOMMENT)if (!SteamManager.IsInitialized) {
        if (true){
            _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.red, "Steam is not available.", _feedbackHideCoroutine);
            return;
        }
        
        _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.yellow, "Searching for lobbies...", _feedbackHideCoroutine);
        //(UNCOMMENT)SteamManager.RequestLobbyList();
    }

    /// <summary>
    /// Callback method for when the create lobby button is pressed.
    /// </summary>
    private void OnCreateLobbyButtonPressed() {
        //(UNCOMMENT)if (!SteamManager.IsInitialized) {
        if (true){
            _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.red, "Steam is not available. Please ensure Steam is running.", _feedbackHideCoroutine);
            Logger.Warn("Cannot create Steam lobby: Steam is not initialized");
            return;
        }

        if (!ConnectInterfaceHelpers.ValidateUsername(_usernameInput, _feedbackText, out var username, _feedbackHideCoroutine, out var newCoroutine)) {
            _feedbackHideCoroutine = newCoroutine;
            return;
        }

        _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.yellow, "Creating Steam lobby...", _feedbackHideCoroutine);
        Logger.Info($"Create lobby requested for user: {username}");

        // Create lobby via SteamManager
        //(UNCOMMENT)SteamManager.CreateLobby(username);
    }

    /// <summary>
    /// Callback method for when the browse lobby button is pressed.
    /// </summary>
    private void OnBrowseLobbyButtonPressed() {
        //(UNCOMMENT)if (!SteamManager.IsInitialized) {
        if (true){
            _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.red, "Steam is not available.", _feedbackHideCoroutine);
            return;
        }
        
        _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.yellow, "Refreshing lobby list...", _feedbackHideCoroutine);
        //(UNCOMMENT)SteamManager.RequestLobbyList();
    }

    /// <summary>
    /// Callback for when a Steam lobby is created.
    /// </summary>
    private void OnSteamLobbyCreated(CSteamID lobbyId, string username) {
        Logger.Info($"Lobby created: {lobbyId}");
        _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.green, $"Lobby created! Friends can join via Steam overlay.", _feedbackHideCoroutine);

        // Fire event to start server hosting
        // Port is ignored for Steam P2P, but we pass 0 for consistency
        StartHostButtonPressed?.Invoke("", 0, username, TransportType.Steam); 
    }

    /// <summary>
    /// Callback for Join Friend button (Steam tab).
    /// </summary>
    private void OnJoinFriendButtonPressed() {
        //(UNCOMMENT)if (!SteamManager.IsInitialized) {
        if (true){
            _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.red, "Steam is not available.", _feedbackHideCoroutine);
            return;
        }

        // Open Steam Friends overlay
        SteamFriends.ActivateGameOverlay("Friends");
        _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.yellow, "Opened Steam Friends. Right-click friend to Join Game.", _feedbackHideCoroutine);
    }

    /// <summary>
    /// Callback for when a list of lobbies is received.
    /// </summary>
    private void OnLobbyListReceived(CSteamID[] lobbyIds) {
        if (lobbyIds.Length == 0) {
            _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.yellow, "No lobbies found.", _feedbackHideCoroutine);
            return;
        }

        Logger.Info($"Found {lobbyIds.Length} lobbies. Auto-joining first one for now.");
        _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.yellow, $"Found {lobbyIds.Length} lobbies. Joining first...", _feedbackHideCoroutine);
        
        // For now, just join the first one
        //(UNCOMMENT)SteamManager.JoinLobby(lobbyIds[0]);
    }

    /// <summary>
    /// Callback for when a lobby is joined.
    /// </summary>
    private void OnLobbyJoined(CSteamID lobbyId) {
        Logger.Info($"Joined lobby: {lobbyId}");
        _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.green, "Joined lobby! Connecting to host...", _feedbackHideCoroutine);

        var hostId = 0; //SteamManager.GetLobbyOwner(lobbyId);
        
        if (!ConnectInterfaceHelpers.ValidateUsername(_usernameInput, _feedbackText, out var username, _feedbackHideCoroutine, out var newCoroutine)) {
            _feedbackHideCoroutine = newCoroutine;
            return;
        }

        // Connect using Steam ID as address
        ConnectButtonPressed?.Invoke(hostId.ToString(), 0, username, TransportType.Steam);
    }

    /// <summary>
    /// Callback method for when the direct connect button is pressed.
    /// </summary>
    private void OnDirectConnectButtonPressed() {
        var address = _addressInput.GetInput();
        if (address.Length == 0) {
            _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.red, ErrorEnterAddress, _feedbackHideCoroutine);
            return;
        }
        
        var portString = _portInput.GetInput();
        if (!int.TryParse(portString, out var port) || port == 0) {
            _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.red, ErrorEnterValidPort, _feedbackHideCoroutine);
            return;
        }
        
        if (!ConnectInterfaceHelpers.ValidateUsername(_usernameInput, _feedbackText, out var username, _feedbackHideCoroutine, out var newCoroutine)) {
            _feedbackHideCoroutine = newCoroutine;
            return;
        }
        
        // Save settings
        _modSettings.ConnectAddress = address;
        _modSettings.ConnectPort = port;
        _modSettings.Username = username;
        _modSettings.Save();
        
        _directConnectButton.SetText(ConnectingText);
        _directConnectButton.SetInteractable(false);
        
        Logger.Debug($"Connecting to {address}:{port} as {username}");
        ConnectButtonPressed?.Invoke(address, port, username, TransportType.Udp);
    }

    /// <summary>
    /// Callback method for when the start hosting button is pressed.
    /// </summary>
    private void OnStartButtonPressed() {
        var portString = _portInput.GetInput();
        if (!int.TryParse(portString, out var port) || port == 0) {
            _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.red, ErrorEnterValidPortHost, _feedbackHideCoroutine);
            return;
        }
        if (!ConnectInterfaceHelpers.ValidateUsername(_usernameInput, _feedbackText, out var username, _feedbackHideCoroutine, out var newCoroutine)) {
            _feedbackHideCoroutine = newCoroutine;
            return;
        }
        StartHostButtonPressed?.Invoke("", port, username, TransportType.Udp);
    }

    /// <summary>
    /// Callback method for when the client disconnects.
    /// </summary>
    public void OnClientDisconnect() {
        ConnectInterfaceHelpers.ResetConnectButtons(_directConnectButton, _lobbyConnectButton);
    }

    /// <summary>
    /// Callback method for when the client successfully connects.
    /// </summary>
    public void OnSuccessfulConnect() {
        _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.green, MsgConnected, _feedbackHideCoroutine);
        ConnectInterfaceHelpers.ResetConnectButtons(_directConnectButton, _lobbyConnectButton);
    }

    /// <summary>
    /// Callback method for when the client fails to connect.
    /// </summary>
    /// <param name="result">The result of the failed connection.</param>
    public void OnFailedConnect(ConnectionFailedResult result) {
        var message = result.Reason switch {
            ConnectionFailedReason.InvalidAddons => ErrorInvalidAddons,
            ConnectionFailedReason.SocketException or ConnectionFailedReason.IOException => 
                ErrorInternal,
            ConnectionFailedReason.TimedOut => ErrorTimeout,
            ConnectionFailedReason.Other => $"Failed to connect:\n{((ConnectionFailedMessageResult)result).Message}",
            _ => ErrorUnknown
        };
        
        _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(_feedbackText, Color.red, message, _feedbackHideCoroutine);
        ConnectInterfaceHelpers.ResetConnectButtons(_directConnectButton, _lobbyConnectButton);
    }


}

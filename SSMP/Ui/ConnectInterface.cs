using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using SSMP.Game;
using SSMP.Game.Settings;
using SSMP.Networking.Client;
using SSMP.Networking.Matchmaking;
using SSMP.Ui.Component;
using Steamworks;
using SSMP.Networking.Transport.Common;
using SSMP.Ui.Util;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

// ReSharper disable ObjectCreationAsStatement
// ReSharper disable HeuristicUnreachableCode
// ReSharper disable UnusedMember.Local
#pragma warning disable CS0162 // Unreachable code detected

namespace SSMP.Ui;

/// <summary>
/// Manages the multiplayer connection interface with tabbed navigation for Matchmaking, Steam, and Direct IP connections.
/// </summary>
[SuppressMessage("Compiler", "CS0162:Unreachable code detected")]
internal class ConnectInterface {
    #region Layout Constants

    /// <summary>
    /// Horizontal indentation for text labels relative to the panel edge.
    /// </summary>
    private const float TextIndentWidth = 5f;

    /// <summary>
    /// Default width for content elements like input fields and buttons.
    /// </summary>
    private const float ContentWidth = 360f;

    /// <summary>
    /// Standard height for input fields and buttons.
    /// </summary>
    private const float UniformHeight = 50f;

    /// <summary>
    /// Initial X position for the UI panel (centered at 1920x1080).
    /// </summary>
    private const float InitialX = 960f;

    /// <summary>
    /// Initial Y position for the UI panel.
    /// </summary>
    private const float InitialY = 850f;

    // Header dimensions

    /// <summary>
    /// Width of the "MULTIPLAYER" header text.
    /// </summary>
    private const float HeaderWidth = 400f;

    /// <summary>
    /// Height of the "MULTIPLAYER" header text.
    /// </summary>
    private const float HeaderHeight = 40f;

    /// <summary>
    /// Spacing between the header and the glowing notch.
    /// </summary>
    private const float HeaderToNotchSpacing = 45f;

    /// <summary>
    /// Spacing between the glowing notch and the main panel.
    /// </summary>
    private const float NotchToPanelSpacing = 35f;

    // Panel and element spacing

    /// <summary>
    /// Padding at the top of the background panel.
    /// </summary>
    private const float PanelPaddingTop = 35f;

    /// <summary>
    /// Standard height for text labels.
    /// </summary>
    private const float LabelHeight = 20f;

    /// <summary>
    /// Spacing between a label and its associated input field.
    /// </summary>
    private const float LabelToInputSpacing = 38f;

    /// <summary>
    /// Spacing between consecutive input fields.
    /// </summary>
    private const float InputSpacing = 30f;

    // Tab configuration

    /// <summary>
    /// Width of each tab button.
    /// </summary>
    private const float TabButtonWidth = 150f;

    /// <summary>
    /// Vertical spacing reserved for the tab row.
    /// </summary>
    private const float TabSpacing = 60f;

    // Content-specific spacing

    /// <summary>
    /// Height allocated for description text blocks.
    /// </summary>
    private const float DescriptionHeight = 40f;

    /// <summary>
    /// Spacing for the "JOIN SESSION" header.
    /// </summary>
    private const float JoinHeaderSpacing = 45f;

    /// <summary>
    /// Spacing for the join session description text.
    /// </summary>
    private const float JoinDescSpacing = 55f;

    /// <summary>
    /// Spacing for the Lobby ID label.
    /// </summary>
    private const float LobbyIdLabelSpacing = 42f;

    /// <summary>
    /// Spacing for simple text messages in the Steam tab.
    /// </summary>
    private const float SteamTextSpacing = 40f;

    /// <summary>
    /// Spacing between buttons in the Steam tab.
    /// </summary>
    private const float SteamButtonSpacing = 10f;

    /// <summary>
    /// Spacing for the Server Address label.
    /// </summary>
    private const float ServerAddressLabelSpacing = 44f;

    /// <summary>
    /// Spacing between Server Address input and the next element.
    /// </summary>
    private const float ServerAddressInputSpacing = 12f;

    /// <summary>
    /// Spacing for the Port label.
    /// </summary>
    private const float PortLabelSpacing = 38f;

    /// <summary>
    /// Y-offset for the feedback/error text relative to the content area.
    /// </summary>
    private const float FeedbackTextOffset = 310f;

    #endregion

    #region UI Text Constants

    /// <summary>
    /// Text displayed on buttons while a connection is being attempted.
    /// </summary>
    private const string ConnectingText = "Connecting...";

    /// <summary>
    /// Main title text for the multiplayer interface.
    /// </summary>
    private const string HeaderText = "M U L T I P L A Y E R";

    /// <summary>
    /// Label text for the player identification section.
    /// </summary>
    private const string IdentityLabelText = "Identity";

    /// <summary>
    /// Placeholder text for the username input field.
    /// </summary>
    private const string UsernamePlaceholder = "Enter Username";

    // Tab names

    /// <summary>
    /// Label for the Matchmaking tab.
    /// </summary>
    private const string MatchmakingTabText = "Matchmaking";

    /// <summary>
    /// Label for the Steam tab.
    /// </summary>
    private const string SteamTabText = "Steam";

    /// <summary>
    /// Label for the Direct IP tab.
    /// </summary>
    private const string DirectIpTabText = "Direct IP";

    // Matchmaking tab

    /// <summary>
    /// Header text for the Matchmaking tab.
    /// </summary>
    private const string JoinSessionText = "JOIN SESSION";

    /// <summary>
    /// Description/instructions for the Matchmaking tab.
    /// </summary>
    private const string JoinSessionDescText = "Enter the unique Lobby ID\nto join an existing session.";

    /// <summary>
    /// Label for the Lobby ID input field.
    /// </summary>
    private const string LobbyIdLabelText = "Lobby ID";

    /// <summary>
    /// Placeholder text for the Lobby ID input.
    /// </summary>
    private const string LobbyIdPlaceholder = "e.g. 8x92-AC44";

    /// <summary>
    /// Text for the Connect button in the Matchmaking tab.
    /// </summary>
    private const string LobbyConnectButtonText = "CONNECT";

    /// <summary>
    /// Text for the Host Lobby button in the Matchmaking tab.
    /// </summary>
    private const string HostLobbyButtonText = "HOST LOBBY";

    // Steam tab

    /// <summary>
    /// Status text when connected to Steam.
    /// </summary>
    private const string SteamConnectedText = "Connected to Steam Workshop";

    /// <summary>
    /// Text for the Create Lobby button.
    /// </summary>
    private const string CreateLobbyButtonText = "+ CREATE LOBBY";

    /// <summary>
    /// Text for the Browse Lobbies button.
    /// </summary>
    private const string BrowseLobbyButtonText = "☰ BROWSE PUBLIC LOBBIES";

    /// <summary>
    /// Text for the Join Friend button.
    /// </summary>
    private const string JoinFriendButtonText = "→ JOIN FRIEND (INVITE)";

    // Direct IP tab

    /// <summary>
    /// Label for the Server Address input.
    /// </summary>
    private const string ServerAddressLabelText = "Server Address";

    /// <summary>
    /// Placeholder for the Server Address input.
    /// </summary>
    private const string ServerAddressPlaceholder = "127.0.0.1";

    /// <summary>
    /// Label for the Port input.
    /// </summary>
    private const string PortLabelText = "Port";

    /// <summary>
    /// Placeholder for the Port input.
    /// </summary>
    private const string PortPlaceholder = "26960";

    /// <summary>
    /// Text for the Connect button in Direct IP tab. Public for access by helpers if needed.
    /// </summary>
    public const string DirectConnectButtonText = "CONNECT";

    /// <summary>
    /// Text for the Host button in Direct IP tab.
    /// </summary>
    private const string HostButtonText = "HOST";

    #endregion

    #region Error & Status Messages

    /// <summary>
    /// Error message when address input is missing.
    /// </summary>
    private const string ErrorEnterAddress = "Failed to connect:\nYou must enter an address";

    /// <summary>
    /// Error message when port input is invalid or missing.
    /// </summary>
    private const string ErrorEnterValidPort = "Failed to connect:\nYou must enter a valid port";

    /// <summary>
    /// Error message when hosting port is invalid.
    /// </summary>
    private const string ErrorEnterValidPortHost = "Failed to host:\nYou must enter a valid port";

    /// <summary>
    /// Status message for successful connection.
    /// </summary>
    private const string MsgConnected = "Successfully connected";

    /// <summary>
    /// Error message for addon mismatch.
    /// </summary>
    private const string ErrorInvalidAddons = "Failed to connect:\nInvalid addons";

    /// <summary>
    /// Error message for internal exceptions (Socket/IO).
    /// </summary>
    private const string ErrorInternal = "Failed to connect:\nInternal error";

    /// <summary>
    /// Error message for connection timeout.
    /// </summary>
    private const string ErrorTimeout = "Failed to connect:\nConnection timed out";

    /// <summary>
    /// Fallback error message for unknown failures.
    /// </summary>
    private const string ErrorUnknown = "Failed to connect:\nUnknown reason";

    #endregion

    #region Fields

    /// <summary>
    /// Persistent mod settings for storing connection preferences.
    /// </summary>
    private readonly ModSettings _modSettings;

    // Core UI components
    /// <summary>
    /// Input field for the username.
    /// </summary>
    private readonly IInputComponent _usernameInput;

    /// <summary>
    /// Text component used to display status messages and errors.
    /// </summary>
    private readonly ITextComponent _feedbackText;

    /// <summary>
    /// Main background panel of the interface.
    /// </summary>
    private readonly GameObject _backgroundPanel;

    /// <summary>
    /// Decorative glowing notch at the top of the interface.
    /// </summary>
    private readonly GameObject _glowingNotch;

    /// <summary>
    /// Component group containing the background elements.
    /// </summary>
    private readonly ComponentGroup _backgroundGroup;

    // Tab system
    /// <summary>
    /// Tab button for the Matchmaking section.
    /// </summary>
    private readonly TabButtonComponent _matchmakingTab;

    /// <summary>
    /// Tab button for the Steam section (nullable if Steam is not active).
    /// </summary>
    private readonly TabButtonComponent? _steamTab;

    /// <summary>
    /// Tab button for the Direct IP section.
    /// </summary>
    private readonly TabButtonComponent _directIpTab;

    // Tab content groups
    /// <summary>
    /// Component group holding Matchmaking tab content.
    /// </summary>
    private readonly ComponentGroup _matchmakingGroup;

    /// <summary>
    /// Component group holding Steam tab content.
    /// </summary>
    private readonly ComponentGroup? _steamGroup;

    /// <summary>
    /// Component group holding Direct IP tab content.
    /// </summary>
    private readonly ComponentGroup _directIpGroup;

    // Matchmaking tab components
    /// <summary>
    /// Input field for entering a Lobby ID.
    /// </summary>
    // ReSharper disable once NotAccessedField.Local
    private readonly IInputComponent _lobbyIdInput;

    /// <summary>
    /// Button to connect to a lobby via ID.
    /// </summary>
    private readonly IButtonComponent _lobbyConnectButton;

    /// <summary>
    /// Scrollable panel for browsing public lobbies.
    /// </summary>
    private readonly LobbyBrowserPanel _lobbyBrowserPanel;

    /// <summary>
    /// Configuration panel for hosting a matchmaking lobby.
    /// </summary>
    private readonly LobbyConfigPanel _lobbyConfigPanel;

    // Steam tab components
    /// <summary>
    /// Button to create a new Steam lobby.
    /// </summary>
    // ReSharper disable once NotAccessedField.Local
    private IButtonComponent? _createLobbyButton;

    /// <summary>
    /// Button to open the lobby browser.
    /// </summary>
    // ReSharper disable once NotAccessedField.Local
    private IButtonComponent? _browseLobbyButton;

    /// <summary>
    /// Scrollable panel for browsing public lobbies on Steam tab.
    /// </summary>
    private readonly LobbyBrowserPanel? _steamLobbyBrowserPanel;

    /// <summary>
    /// Configuration panel for hosting a Steam lobby.
    /// </summary>
    private readonly LobbyConfigPanel? _steamLobbyConfigPanel;

    /// <summary>
    /// Button to join a friend via invite.
    /// </summary>
    // ReSharper disable once NotAccessedField.Local
    private IButtonComponent? _joinFriendButton;

    // Direct IP tab components
    /// <summary>
    /// Input field for the server IP address.
    /// </summary>
    private readonly IInputComponent _addressInput;

    /// <summary>
    /// Input field for the server port.
    /// </summary>
    private readonly IInputComponent _portInput;

    /// <summary>
    /// Button to connect via Direct IP.
    /// </summary>
    private readonly IButtonComponent _directConnectButton;

    /// <summary>
    /// Button to start hosting a server.
    /// </summary>
    private readonly IButtonComponent _serverButton;

    /// <summary>
    /// Coroutine handle for hiding feedback text after a delay.
    /// </summary>
    private Coroutine? _feedbackHideCoroutine;

    /// <summary>
    /// Client for the MatchMaking Service (MMS).
    /// </summary>
    private readonly MmsClient _mmsClient;

    /// <summary>
    /// Public accessor for the MMS client.
    /// Used by server manager to pass to HolePunch transport for lobby cleanup.
    /// </summary>
    public MmsClient MmsClient => _mmsClient;

    #endregion

    #region Events

    /// <summary>
    /// Fired when the user attempts to connect to a server.
    /// Parameters: address, port, username, transportType
    /// </summary>
    public event Action<string, int, string, TransportType>? ConnectButtonPressed;

    /// <summary>
    /// Fired when the user attempts to start hosting a server.
    /// Parameters: address, port, username, transportType
    /// </summary>
    public event Action<string, int, string, TransportType>? StartHostButtonPressed;

    #endregion

    #region Tab Enum

    /// <summary>
    /// Available tabs in the connection interface.
    /// </summary>
    private enum Tab {
        Matchmaking,
        Steam,
        DirectIp
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the multiplayer connection interface with all tabs and UI elements.
    /// </summary>
    /// <param name="modSettings">Persistent mod settings for storing connection preferences.</param>
    /// <param name="connectGroup">Parent component group for the interface.</param>
    public ConnectInterface(ModSettings modSettings, ComponentGroup connectGroup) {
        _modSettings = modSettings;
        _mmsClient = new MmsClient(modSettings.MmsUrl);

        SubscribeToSteamEvents();

        var currentY = InitialY;

        _backgroundGroup = new ComponentGroup(parent: connectGroup);

        // Build UI from top to bottom
        CreateHeader(connectGroup, ref currentY);
        _glowingNotch = CreateNotch(ref currentY);
        _backgroundPanel = CreateBackgroundPanel(ref currentY);

        _usernameInput = CreateUsernameSection(ref currentY);
        var tabElements = CreateTabButtons(ref currentY);
        _matchmakingTab = tabElements.matchmaking;
        _steamTab = tabElements.steam;
        _directIpTab = tabElements.directIp;

        // Create tab-specific content
        var matchmakingComponents = CreateMatchmakingTab(currentY);
        _matchmakingGroup = matchmakingComponents.group;
        _lobbyIdInput = matchmakingComponents.lobbyIdInput;
        _lobbyConnectButton = matchmakingComponents.connectButton;

        // Create lobby browser panel
        _lobbyBrowserPanel = new LobbyBrowserPanel(
            _backgroundGroup,
            new Vector2(InitialX, currentY),
            new Vector2(ContentWidth, 280f)
        );
        _lobbyBrowserPanel.SetOnLobbySelected(lobby => {
                _lobbyIdInput.SetInput(lobby.LobbyCode);
                _lobbyBrowserPanel.Hide();
                _matchmakingGroup.SetActive(true); 
                ShowFeedback(Color.green, $"Selected lobby: {lobby.LobbyCode}");
            }
        );
        _lobbyBrowserPanel.SetOnBack(() => {
                _lobbyBrowserPanel.Hide();
                _matchmakingGroup.SetActive(true); 
            }
        );
        _lobbyBrowserPanel.SetOnRefresh(() => { MonoBehaviourUtil.Instance.StartCoroutine(FetchLobbiesCoroutine()); });

        var steamComponents = CreateSteamTab(currentY);
        _steamGroup = steamComponents.group;
        _createLobbyButton = steamComponents.createButton;
        _browseLobbyButton = steamComponents.browseButton;
        _joinFriendButton = steamComponents.joinButton;

        // Create Steam lobby browser panel (same layout as matchmaking)
        if (_steamGroup != null) {
            _steamLobbyBrowserPanel = new LobbyBrowserPanel(
                _backgroundGroup,
                new Vector2(InitialX, currentY),
                new Vector2(ContentWidth, 280f)
            );
            _steamLobbyBrowserPanel.SetOnLobbySelected(lobby => {
                    _steamLobbyBrowserPanel.Hide();
                    _steamGroup.SetActive(true);
                    
                    // Steam lobbies join via Steam ID (ConnectionData)
                    if (lobby.LobbyType == "steam") {
                        JoinSteamLobbyFromBrowser(lobby.ConnectionData);
                    } else {
                        ShowFeedback(Color.red, "Invalid Steam lobby");
                    }
                }
            );
            _steamLobbyBrowserPanel.SetOnBack(() => {
                    _steamLobbyBrowserPanel.Hide();
                    _steamGroup.SetActive(true);
                }
            );
            _steamLobbyBrowserPanel.SetOnRefresh(() => {
                    MonoBehaviourUtil.Instance.StartCoroutine(FetchSteamLobbiesCoroutine());
                }
            );

            // Create Steam lobby config panel
            _steamLobbyConfigPanel = new LobbyConfigPanel(
                _backgroundGroup,
                new Vector2(InitialX, currentY),
                new Vector2(ContentWidth, 280f),
                "steam"
            );
            _steamLobbyConfigPanel.SetOnCreate((name, visibility) => {
                    _steamLobbyConfigPanel.Hide();
                    _steamGroup?.SetActive(true);
                    CreateSteamLobbyWithConfig(name, visibility);
                }
            );
            _steamLobbyConfigPanel.SetOnCancel(() => {
                    _steamLobbyConfigPanel.Hide();
                    _steamGroup?.SetActive(true);
                }
            );
        }

        // Create matchmaking lobby config panel
        _lobbyConfigPanel = new LobbyConfigPanel(
            _backgroundGroup,
            new Vector2(InitialX, currentY),
            new Vector2(ContentWidth, 280f)
        );
        _lobbyConfigPanel.SetOnCreate((name, visibility) => {
                _lobbyConfigPanel.Hide();
                _matchmakingGroup.SetActive(true);
                CreateMatchmakingLobbyWithConfig(name, visibility);
            }
        );
        _lobbyConfigPanel.SetOnCancel(() => {
                _lobbyConfigPanel.Hide();
                _matchmakingGroup.SetActive(true);
            }
        );

        var directIpComponents = CreateDirectIpTab(currentY);
        _directIpGroup = directIpComponents.group;
        _addressInput = directIpComponents.addressInput;
        _portInput = directIpComponents.portInput;
        _directConnectButton = directIpComponents.connectButton;
        _serverButton = directIpComponents.hostButton;

        currentY -= FeedbackTextOffset / UiManager.ScreenHeightRatio;

        _feedbackText = CreateFeedbackText(currentY);

        FinalizeLayout();

        SwitchTab(Tab.Matchmaking);
    }

    /// <summary>
    /// Subscribes to Steam lobby-related events if Steam is available.
    /// </summary>
    private void SubscribeToSteamEvents() {
        SteamManager.LobbyCreatedEvent += OnSteamLobbyCreated;
        SteamManager.LobbyListReceivedEvent += OnLobbyListReceived;
        SteamManager.LobbyJoinedEvent += OnLobbyJoined;
    }

    /// <summary>
    /// Creates the main header text at the top of the interface.
    /// </summary>
    private void CreateHeader(ComponentGroup parent, ref float currentY) {
        new TextComponent(
            parent,
            new Vector2(InitialX, currentY),
            new Vector2(HeaderWidth, HeaderHeight),
            HeaderText,
            fontSize: 32,
            alignment: TextAnchor.MiddleCenter
        );

        currentY -= HeaderToNotchSpacing / UiManager.ScreenHeightRatio;
    }

    /// <summary>
    /// Creates the glowing decorative notch below the header.
    /// </summary>
    private GameObject CreateNotch(ref float currentY) {
        var notch = ConnectInterfaceHelpers.CreateGlowingNotch(InitialX, currentY);
        return notch;
    }

    /// <summary>
    /// Creates the main background panel with resolution-aware height scaling.
    /// </summary>
    private GameObject CreateBackgroundPanel(ref float currentY) {
        return ConnectInterfaceHelpers.CreateBackgroundPanel(
            InitialX,
            currentY - NotchToPanelSpacing / UiManager.ScreenHeightRatio
        );
    }

    /// <summary>
    /// Creates the username input section at the top of the panel.
    /// </summary>
    private IInputComponent CreateUsernameSection(ref float currentY) {
        currentY -= (NotchToPanelSpacing + PanelPaddingTop) / UiManager.ScreenHeightRatio;

        new TextComponent(
            _backgroundGroup,
            new Vector2(InitialX + TextIndentWidth, currentY),
            new Vector2(ContentWidth, LabelHeight),
            IdentityLabelText,
            UiManager.NormalFontSize,
            alignment: TextAnchor.MiddleLeft
        );

        currentY -= LabelToInputSpacing / UiManager.ScreenHeightRatio;

        var usernameInput = new InputComponent(
            _backgroundGroup,
            new Vector2(InitialX, currentY),
            new Vector2(ContentWidth, UniformHeight),
            _modSettings.Username,
            UsernamePlaceholder,
            characterLimit: 32,
            onValidateInput: (_, _, addedChar) => char.IsLetterOrDigit(addedChar) ? addedChar : '\0'
        );

        currentY -= (UniformHeight + InputSpacing) / UiManager.ScreenHeightRatio;

        return usernameInput;
    }

    /// <summary>
    /// Creates the tab navigation buttons (Matchmaking, Steam, Direct IP).
    /// </summary>
    private (TabButtonComponent matchmaking, TabButtonComponent? steam, TabButtonComponent directIp)
        CreateTabButtons(ref float currentY) {
        var matchmaking = ConnectInterfaceHelpers.CreateTabButton(
            _backgroundGroup,
            InitialX - TabButtonWidth,
            currentY,
            TabButtonWidth,
            MatchmakingTabText,
            () => SwitchTab(Tab.Matchmaking)
        );

        TabButtonComponent? steam = null;
        if (SteamManager.IsInitialized) {
            steam = ConnectInterfaceHelpers.CreateTabButton(
                _backgroundGroup,
                InitialX,
                currentY,
                TabButtonWidth,
                SteamTabText,
                () => SwitchTab(Tab.Steam)
            );
        }

        // Position DirectIp tab next to Steam, or in center if Steam not available
        var directIpX = SteamManager.IsInitialized ? InitialX + TabButtonWidth : InitialX;
        var directIp = ConnectInterfaceHelpers.CreateTabButton(
            _backgroundGroup,
            directIpX,
            currentY,
            TabButtonWidth,
            DirectIpTabText,
            () => SwitchTab(Tab.DirectIp)
        );

        currentY -= TabSpacing / UiManager.ScreenHeightRatio;

        return (matchmaking, steam, directIp);
    }

    #endregion

    #region Tab Content Creation

    /// <summary>
    /// Creates the Matchmaking tab content with lobby ID input and connect/host buttons.
    /// </summary>
    private (ComponentGroup group, IInputComponent lobbyIdInput, IButtonComponent connectButton, IButtonComponent
        hostButton)
        CreateMatchmakingTab(float startY) {
        var group = new ComponentGroup(parent: _backgroundGroup);
        var y = startY;

        // Header
        new TextComponent(
            group,
            new Vector2(InitialX, y),
            new Vector2(ContentWidth, LabelHeight),
            JoinSessionText,
            fontSize: 18,
            alignment: TextAnchor.MiddleCenter
        );
        y -= JoinHeaderSpacing / UiManager.ScreenHeightRatio;

        // Description
        new TextComponent(
            group,
            new Vector2(InitialX, y),
            new Vector2(ContentWidth, DescriptionHeight),
            JoinSessionDescText,
            UiManager.SubTextFontSize,
            alignment: TextAnchor.MiddleCenter
        );
        y -= JoinDescSpacing / UiManager.ScreenHeightRatio;

        // Lobby ID label
        new TextComponent(
            group,
            new Vector2(InitialX + TextIndentWidth, y),
            new Vector2(ContentWidth, LabelHeight),
            LobbyIdLabelText,
            UiManager.NormalFontSize,
            alignment: TextAnchor.MiddleLeft
        );
        y -= LobbyIdLabelSpacing / UiManager.ScreenHeightRatio;

        // Lobby ID input
        var lobbyIdInput = new InputComponent(
            group,
            new Vector2(InitialX, y),
            new Vector2(ContentWidth, UniformHeight),
            "",
            LobbyIdPlaceholder,
            characterLimit: 12
        );
        y -= (UniformHeight + 20f) / UiManager.ScreenHeightRatio;

        // Two buttons side-by-side (same layout as Direct IP tab)
        var buttonGap = 10f;
        var buttonWidth = (ContentWidth - buttonGap) / 2f;
        var buttonOffset = ((buttonWidth + buttonGap) / 2f) / (float) System.Math.Pow(UiManager.ScreenHeightRatio, 2);

        // Connect button (left)
        var connectButton = new ButtonComponent(
            group,
            new Vector2(InitialX - buttonOffset, y),
            new Vector2(buttonWidth, UniformHeight),
            LobbyConnectButtonText,
            Resources.TextureManager.ButtonBg,
            Resources.FontManager.UIFontRegular,
            UiManager.NormalFontSize
        );
        connectButton.SetOnPress(OnLobbyConnectButtonPressed);

        // Host Lobby button (right)
        var hostButton = new ButtonComponent(
            group,
            new Vector2(InitialX + buttonOffset, y),
            new Vector2(buttonWidth, UniformHeight),
            HostLobbyButtonText,
            Resources.TextureManager.ButtonBg,
            Resources.FontManager.UIFontRegular,
            UiManager.NormalFontSize
        );
        hostButton.SetOnPress(OnHostLobbyButtonPressed);

        y -= (UniformHeight + 15f) / UiManager.ScreenHeightRatio;

        // Browse Lobbies button (full width)
        var browseButton = new ButtonComponent(
            group,
            new Vector2(InitialX, y),
            new Vector2(ContentWidth, UniformHeight),
            "☰ BROWSE PUBLIC LOBBIES",
            Resources.TextureManager.ButtonBg,
            Resources.FontManager.UIFontRegular,
            UiManager.NormalFontSize
        );
        browseButton.SetOnPress(OnBrowseMatchmakingLobbiesPressed);

        return (group, lobbyIdInput, connectButton, hostButton);
    }

    /// <summary>
    /// Creates the Steam tab content with lobby management buttons.
    /// </summary>
    private (ComponentGroup? group, IButtonComponent? createButton, IButtonComponent? browseButton,
        IButtonComponent? joinButton) CreateSteamTab(float startY) {
        if (!SteamManager.IsInitialized) {
            return (null, null, null, null);
        }

        var group = new ComponentGroup(activeSelf: false, parent: _backgroundGroup);
        var y = startY;

        // Status text
        new TextComponent(
            group,
            new Vector2(InitialX, y),
            new Vector2(ContentWidth, LabelHeight),
            SteamConnectedText,
            UiManager.SubTextFontSize,
            alignment: TextAnchor.MiddleCenter
        );
        y -= SteamTextSpacing / UiManager.ScreenHeightRatio;

        // Create lobby button
        var createButton = new ButtonComponent(
            group,
            new Vector2(InitialX, y),
            new Vector2(ContentWidth, UniformHeight),
            CreateLobbyButtonText,
            Resources.TextureManager.ButtonBg,
            Resources.FontManager.UIFontRegular,
            UiManager.NormalFontSize
        );
        createButton.SetOnPress(OnCreateLobbyButtonPressed);
        y -= (UniformHeight + SteamButtonSpacing) / UiManager.ScreenHeightRatio;

        // Browse lobbies button
        var browseButton = new ButtonComponent(
            group,
            new Vector2(InitialX, y),
            new Vector2(ContentWidth, UniformHeight),
            BrowseLobbyButtonText,
            Resources.TextureManager.ButtonBg,
            Resources.FontManager.UIFontRegular,
            UiManager.NormalFontSize
        );
        browseButton.SetOnPress(OnBrowseLobbyButtonPressed);
        y -= (UniformHeight + SteamButtonSpacing) / UiManager.ScreenHeightRatio;

        // Join friend button
        var joinButton = new ButtonComponent(
            group,
            new Vector2(InitialX, y),
            new Vector2(ContentWidth, UniformHeight),
            JoinFriendButtonText,
            Resources.TextureManager.ButtonBg,
            Resources.FontManager.UIFontRegular,
            UiManager.NormalFontSize
        );
        joinButton.SetOnPress(OnJoinFriendButtonPressed);

        return (group, createButton, browseButton, joinButton);
    }

    /// <summary>
    /// Creates the Direct IP tab content with address/port inputs and connect/host buttons.
    /// </summary>
    private (ComponentGroup group, IInputComponent addressInput, IInputComponent portInput,
        IButtonComponent connectButton, IButtonComponent hostButton)
        CreateDirectIpTab(float startY) {
        var group = new ComponentGroup(activeSelf: false, parent: _backgroundGroup);
        var y = startY;

        // Server address section
        new TextComponent(
            group,
            new Vector2(InitialX + TextIndentWidth, y),
            new Vector2(ContentWidth, LabelHeight),
            ServerAddressLabelText,
            UiManager.NormalFontSize,
            alignment: TextAnchor.MiddleLeft
        );
        y -= ServerAddressLabelSpacing / UiManager.ScreenHeightRatio;

        var addressInput = new IpInputComponent(
            group,
            new Vector2(InitialX, y),
            new Vector2(ContentWidth, UniformHeight),
            _modSettings.ConnectAddress,
            ServerAddressPlaceholder
        );
        y -= (UniformHeight + ServerAddressInputSpacing) / UiManager.ScreenHeightRatio;

        // Port section
        new TextComponent(
            group,
            new Vector2(InitialX + TextIndentWidth, y),
            new Vector2(ContentWidth, LabelHeight),
            PortLabelText,
            UiManager.NormalFontSize,
            alignment: TextAnchor.MiddleLeft
        );
        y -= PortLabelSpacing / UiManager.ScreenHeightRatio;

        var joinPort = _modSettings.ConnectPort;
        var portInput = new PortInputComponent(
            group,
            new Vector2(InitialX, y),
            new Vector2(ContentWidth, UniformHeight),
            joinPort == -1 ? "" : joinPort.ToString(),
            PortPlaceholder
        );
        y -= (UniformHeight + 20f) / UiManager.ScreenHeightRatio;

        // Direct IP button values
        var buttonGap = 10f;
        var buttonWidth = (ContentWidth - buttonGap) / 2f;
        var buttonOffset = ((buttonWidth + buttonGap) / 2f) / (float) System.Math.Pow(UiManager.ScreenHeightRatio, 2);

        // Connect button (left)
        var connectButton = new ButtonComponent(
            group,
            new Vector2(InitialX - buttonOffset, y),
            new Vector2(buttonWidth, UniformHeight),
            DirectConnectButtonText,
            Resources.TextureManager.ButtonBg,
            Resources.FontManager.UIFontRegular,
            UiManager.NormalFontSize
        );
        connectButton.SetOnPress(OnDirectConnectButtonPressed);

        // Host button (right)
        var hostButton = new ButtonComponent(
            group,
            new Vector2(InitialX + buttonOffset, y),
            new Vector2(buttonWidth, UniformHeight),
            HostButtonText,
            Resources.TextureManager.ButtonBg,
            Resources.FontManager.UIFontRegular,
            UiManager.NormalFontSize
        );
        hostButton.SetOnPress(OnStartButtonPressed);

        return (group, addressInput, portInput, connectButton, hostButton);
    }

    /// <summary>
    /// Creates the feedback text component that displays connection status and errors.
    /// </summary>
    private ITextComponent CreateFeedbackText(float contentY) {
        var feedback = new TextComponent(
            _backgroundGroup,
            new Vector2(InitialX, contentY),
            new Vector2(ContentWidth, LabelHeight),
            new Vector2(0.5f, 1f),
            "",
            UiManager.SubTextFontSize,
            alignment: TextAnchor.UpperCenter
        );

        feedback.SetActive(false);

        return feedback;
    }

    /// <summary>
    /// Finalizes the UI layout by reparenting components and positioning tabs correctly.
    /// </summary>
    private void FinalizeLayout() {
        ConnectInterfaceHelpers.ReparentComponentGroup(_backgroundGroup, _backgroundPanel);
        ConnectInterfaceHelpers.PositionTabButtonsFixed(
            _backgroundPanel,
            _matchmakingTab,
            _steamTab,
            _directIpTab
        );

        LogFinalPositions();
    }

    /// <summary>
    /// Logs the final Unity RectTransform positions after reparenting for debugging.
    /// </summary>
    private void LogFinalPositions() {
        if (_portInput is Component.Component portComp &&
            _directConnectButton is Component.Component connectComp &&
            _serverButton is Component.Component hostComp) {
            var portRect = portComp.GameObject.GetComponent<RectTransform>();
            var connectRect = connectComp.GameObject.GetComponent<RectTransform>();
            var hostRect = hostComp.GameObject.GetComponent<RectTransform>();

            Logger.Info($"[ConnectInterface] Final Unity positions:");
            Logger.Info($"  Port: pos={portRect.anchoredPosition}, size={portRect.sizeDelta}");
            Logger.Info($"  Connect: pos={connectRect.anchoredPosition}, size={connectRect.sizeDelta}");
            Logger.Info($"  Host: pos={hostRect.anchoredPosition}, size={hostRect.sizeDelta}");
        }
    }

    #endregion

    #region Tab Management

    /// <summary>
    /// Switches the active tab and updates button states and content visibility.
    /// </summary>
    /// <param name="tab">The tab to activate.</param>
    private void SwitchTab(Tab tab) {
        // Hide lobby browsers and config panels if visible
        _lobbyBrowserPanel.Hide();
        _steamLobbyBrowserPanel?.Hide();
        _lobbyConfigPanel.Hide();
        _steamLobbyConfigPanel?.Hide();
        // Update tab button visual states
        _matchmakingTab.SetTabActive(tab == Tab.Matchmaking);
        _steamTab?.SetTabActive(tab == Tab.Steam);
        _directIpTab.SetTabActive(tab == Tab.DirectIp);

        // Show only the active tab's content
        _matchmakingGroup.SetActive(tab == Tab.Matchmaking);
        _steamGroup?.SetActive(tab == Tab.Steam);
        _directIpGroup.SetActive(tab == Tab.DirectIp);
    }

    /// <summary>
    /// Shows or hides the entire multiplayer menu interface.
    /// </summary>
    /// <param name="active">True to show the menu, false to hide it.</param>
    public void SetMenuActive(bool active) {
        _backgroundPanel.SetActive(active);
        _glowingNotch.SetActive(active);
    }

    #endregion

    #region Button Callbacks - Matchmaking Tab

    /// <summary>
    /// Handles the Matchmaking tab's "Connect to Lobby" button press.
    /// Looks up lobby via MMS and connects to the host.
    /// </summary>
    private void OnLobbyConnectButtonPressed() {
        if (!ValidateUsername(out var username)) {
            return;
        }

        var lobbyId = _lobbyIdInput.GetInput();
        if (string.IsNullOrWhiteSpace(lobbyId)) {
            ShowFeedback(Color.red, "Enter a lobby ID");
            return;
        }

        ShowFeedback(Color.yellow, "Connecting...");
        MonoBehaviourUtil.Instance.StartCoroutine(JoinLobbyCoroutine(lobbyId, username));
    }

    /// <summary>
    /// Coroutine to join a lobby, handling both Matchmaking and Steam types.
    /// </summary>
    private IEnumerator JoinLobbyCoroutine(string lobbyId, string username) {
        ShowFeedback(Color.yellow, "Discovering endpoint...");

        // Discover our public endpoint and keep the socket for reuse (needed for Matchmaking)
        var stunResult = StunClient.DiscoverPublicEndpointWithSocket();
        if (stunResult == null) {
            ShowFeedback(Color.red, "Failed to discover public endpoint");
            yield break;
        }

        var (clientIp, clientPort, socket) = stunResult.Value;

        // Store socket for HolePunchEncryptedTransport to use
        StunClient.PreBoundSocket = socket;

        ShowFeedback(Color.yellow, "Joining lobby...");

        // Join lobby and register our endpoint for punch-back
        var task = _mmsClient.JoinLobbyAsync(lobbyId, clientPort);
        yield return new WaitUntil(() => task.IsCompleted);

        var result = task.Result;

        if (result == null) {
            StunClient.PreBoundSocket?.Dispose();
            StunClient.PreBoundSocket = null;
            ShowFeedback(Color.red, "Lobby not found, offline, or join failed");
            yield break;
        }

        var (connectionData, lobbyType) = result.Value;

        if (lobbyType == "steam") {
            // Steam Connection
            // We don't need the hole punch socket for Steam
            StunClient.PreBoundSocket?.Dispose();
            StunClient.PreBoundSocket = null;

            if (!SteamManager.IsInitialized) {
                ShowFeedback(Color.red, "Steam is not initialized");
                yield break;
            }

            ShowFeedback(Color.green, "Joining Steam lobby...");
            // Pass Steam Lobby ID as IP, Port 0, Transport SteamP2P
            ConnectButtonPressed?.Invoke(connectionData, 0, username, TransportType.Steam);
        } 
        else {
            // Matchmaking Connection (IP:Port)
            var parts = connectionData.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var hostPort)) {
                ShowFeedback(Color.red, "Invalid connection data");
                StunClient.PreBoundSocket?.Dispose();
                StunClient.PreBoundSocket = null;
                yield break;
            }
            var hostIp = parts[0];


            ShowFeedback(Color.green, $"Connecting to {hostIp}:{hostPort}...");
            ConnectButtonPressed?.Invoke(hostIp, hostPort, username, TransportType.HolePunch);
        }
    }

    /// <summary>
    /// Handles the Matchmaking tab's "Host Lobby" button press.
    /// Shows the lobby configuration panel.
    /// </summary>
    private void OnHostLobbyButtonPressed() {
        if (!ValidateUsername(out var username)) {
            return;
        }

        // Show config panel with default name
        _matchmakingGroup.SetActive(false);
        _lobbyConfigPanel.SetDefaultName($"{username}'s Lobby");
        _lobbyConfigPanel.Show();
    }

    /// <summary>
    /// Creates a matchmaking lobby with the specified configuration.
    /// Called from the config panel's Create callback.
    /// </summary>
    private void CreateMatchmakingLobbyWithConfig(string lobbyName, LobbyVisibility visibility) {
        if (!ValidateUsername(out var username)) {
            return;
        }

        ShowFeedback(Color.yellow, "Creating lobby...");
        Logger.Info($"Host lobby requested: '{lobbyName}' ({visibility}) - HolePunch transport");

        MonoBehaviourUtil.Instance.StartCoroutine(
            CreateLobbyWithConfigCoroutine(lobbyName, visibility, "matchmaking", username)
        );
    }

    /// <summary>
    /// Creates a Steam lobby with the specified configuration.
    /// Called from the Steam config panel's Create callback.
    /// </summary>
    private void CreateSteamLobbyWithConfig(string lobbyName, LobbyVisibility visibility) {
        if (!ValidateUsername(out var username)) {
            return;
        }

        ShowFeedback(Color.yellow, "Creating Steam lobby...");
        Logger.Info($"Steam lobby requested: '{lobbyName}' ({visibility})");

        // Convert visibility to Steam lobby type
        var steamLobbyType = visibility switch {
            LobbyVisibility.Public => ELobbyType.k_ELobbyTypePublic,
            LobbyVisibility.FriendsOnly => ELobbyType.k_ELobbyTypeFriendsOnly,
            LobbyVisibility.Private => ELobbyType.k_ELobbyTypePrivate,
            _ => ELobbyType.k_ELobbyTypeFriendsOnly
        };

        // Capture visibility for callback closure
        var isPublic = visibility == LobbyVisibility.Public;

        SteamManager.LobbyCreatedEvent += OnSteamLobbyCreated;

        // Create native Steam lobby (uses Steam's default max = 250)
        SteamManager.CreateLobby(username, lobbyType: steamLobbyType);
        return;

        // Subscribe to lobby created event (one-time)
        void OnSteamLobbyCreated(CSteamID steamLobbyId, string hostName) {
            // Unsubscribe immediately
            SteamManager.LobbyCreatedEvent -= OnSteamLobbyCreated;

            // Only PUBLIC Steam lobbies register with MMS for browser visibility
            // Private and Friends-Only lobbies use Steam's native discovery only
            if (isPublic) {
                MonoBehaviourUtil.Instance.StartCoroutine(
                    RegisterSteamLobbyForBrowserCoroutine(steamLobbyId.m_SteamID.ToString(), lobbyName, username)
                );
            } else {
                ShowFeedback(Color.green, "Steam lobby created!");
                StartHostButtonPressed?.Invoke("0.0.0.0", 0, username, TransportType.Steam);
            }
        }
    }

    /// <summary>
    /// Registers a public Steam lobby with MMS for browser visibility (no invite code).
    /// </summary>
    private IEnumerator RegisterSteamLobbyForBrowserCoroutine(
        string steamLobbyId,
        string lobbyName,
        string username
    ) {
        var task = _mmsClient.RegisterSteamLobbyAsync(
            steamLobbyId,
            lobbyName,
            isPublic: true,
            gameVersion: Application.version
        );

        yield return new WaitUntil(() => task.IsCompleted);

        // Don't show invite code for Steam lobbies - they use Steam's native join flow
        if (task.Result == null) {
            ShowFeedback(Color.yellow, "Steam lobby created (browser listing failed)");
        } else {
            ShowFeedback(Color.green, "Steam lobby created!");
        }

        StartHostButtonPressed?.Invoke("0.0.0.0", 0, username, TransportType.Steam);
    }


    /// <summary>
    /// Coroutine for async lobby creation with config.
    /// </summary>
    private IEnumerator CreateLobbyWithConfigCoroutine(
        string lobbyName,
        LobbyVisibility visibility,
        string lobbyType,
        string username
    ) {
        var isPublic = visibility == LobbyVisibility.Public;
        var task = _mmsClient.CreateLobbyAsync(
            hostPort: 26960,
            lobbyName: lobbyName,
            isPublic: isPublic,
            gameVersion: Application.version,
            lobbyType: lobbyType
        );

        yield return new WaitUntil(() => task.IsCompleted);

        var lobbyId = task.Result;
        if (lobbyId == null) {
            ShowFeedback(Color.red, "Failed to create lobby. Is MMS running?");
            yield break;
        }

        // Start polling for pending clients to punch back
        _mmsClient.StartPendingClientPolling();

        // For private lobbies, show invite code in ChatBox so it's easily shareable
        if (visibility == LobbyVisibility.Private) {
            UiManager.InternalChatBox.AddMessage($"<color=yellow>[Private Lobby]</color> Invite code: <color=lime>{lobbyId}</color>");
            ShowFeedback(Color.green, "Private lobby created!");
        } else {
            ShowFeedback(Color.green, $"Lobby: {lobbyId}");
        }
        
        StartHostButtonPressed?.Invoke("0.0.0.0", 26960, username, TransportType.HolePunch);
    }

    /// <summary>
    /// Legacy coroutine for async lobby creation (without config panel).
    /// </summary>
    private IEnumerator CreateLobbyCoroutine(string username) {
        var lobbyName = $"{username}'s Lobby";
        var task = _mmsClient.CreateLobbyAsync(
            hostPort: 26960,
            lobbyName: lobbyName,
            isPublic: true,
            gameVersion: Application.version
        );

        // Wait for async operation without blocking main thread
        yield return new WaitUntil(() => task.IsCompleted);

        var lobbyId = task.Result;
        if (lobbyId == null) {
            ShowFeedback(Color.red, "Failed to create lobby. Is MMS running?");
            yield break;
        }

        // Start polling for pending clients to punch back
        _mmsClient.StartPendingClientPolling();

        ShowFeedback(Color.green, $"Lobby: {lobbyId}");
        StartHostButtonPressed?.Invoke("0.0.0.0", 26960, username, TransportType.HolePunch);
    }

    /// <summary>
    /// Handles the Matchmaking tab's "Browse Lobbies" button press.
    /// Fetches and displays public lobbies from the MMS.
    /// </summary>
    private void OnBrowseMatchmakingLobbiesPressed() {
        // Hide matchmaking content and show lobby browser
        _matchmakingGroup.SetActive(false);
        _lobbyBrowserPanel.Show();

        ShowFeedback(Color.yellow, "Fetching lobbies...");
        MonoBehaviourUtil.Instance.StartCoroutine(FetchLobbiesCoroutine());
    }

    /// <summary>
    /// Coroutine for async lobby fetching (Matchmaking tab).
    /// </summary>
    private IEnumerator FetchLobbiesCoroutine() {
        var task = _mmsClient.GetPublicLobbiesAsync("matchmaking");

        // Wait for async operation without blocking main thread
        yield return new WaitUntil(() => task.IsCompleted);

        var lobbies = task.Result;
        if (lobbies == null) {
            ShowFeedback(Color.red, "Failed to fetch lobbies. Is MMS running?");
            yield break;
        }

        // Update panel with lobbies and show
        _lobbyBrowserPanel.SetLobbies(lobbies);
        _lobbyBrowserPanel.Show();

        if (lobbies.Count == 0) {
            ShowFeedback(Color.yellow, "No public lobbies found.");
        } else {
            var word = lobbies.Count == 1 ? "Lobby" : "Lobbies";
            ShowFeedback(Color.green, $"Found {lobbies.Count} {word}");
        }

        Logger.Info($"ConnectInterface: Displaying {lobbies.Count} public lobbies");
    }

    #endregion

    #region Button Callbacks - Steam Tab

    /// <summary>
    /// Handles the Steam tab's "Create Lobby" button press.
    /// Shows the Steam lobby configuration panel.
    /// </summary>
    private void OnCreateLobbyButtonPressed() {
        if (!SteamManager.IsInitialized) {
            ShowFeedback(Color.red, "Steam is not available. Please ensure Steam is running.");
            Logger.Warn("Cannot create Steam lobby: Steam is not initialized");
            return;
        }

        if (!ValidateUsername(out var username)) {
            return;
        }

        if (_steamLobbyConfigPanel == null || _steamGroup == null) return;

        // Show config panel with default name
        _steamGroup.SetActive(false);
        _steamLobbyConfigPanel.SetDefaultName($"{username}'s Lobby");
        _steamLobbyConfigPanel.Show();
    }

    /// <summary>
    /// Handles the Steam tab's "Browse Public Lobbies" button press.
    /// Requests a list of available public Steam lobbies from MMS.
    /// </summary>
    private void OnBrowseLobbyButtonPressed() {
        if (!SteamManager.IsInitialized) {
            ShowFeedback(Color.red, "Steam is not available.");
            return;
        }

        if (_steamLobbyBrowserPanel == null || _steamGroup == null) return;

        // Hide Steam content and show lobby browser
        _steamGroup.SetActive(false);
        _steamLobbyBrowserPanel.Show();

        ShowFeedback(Color.yellow, "Fetching lobbies...");
        MonoBehaviourUtil.Instance.StartCoroutine(FetchSteamLobbiesCoroutine());
    }

    /// <summary>
    /// Coroutine for async Steam lobby fetching from MMS.
    /// </summary>
    private IEnumerator FetchSteamLobbiesCoroutine() {
        var task = _mmsClient.GetPublicLobbiesAsync("steam"); // Filter by steam type

        yield return new WaitUntil(() => task.IsCompleted);

        var lobbies = task.Result;
        if (lobbies == null) {
            ShowFeedback(Color.red, "Failed to fetch lobbies. Is MMS running?");
            yield break;
        }

        _steamLobbyBrowserPanel?.SetLobbies(lobbies);

        ShowFeedback(
            lobbies.Count == 0 ? Color.yellow : Color.green,
            lobbies.Count == 0
                ? "No public lobbies found."
                : $"Found {lobbies.Count} {(lobbies.Count == 1 ? "Lobby" : "Lobbies")}"
        );

        Logger.Info($"ConnectInterface: Displaying {lobbies.Count} public lobbies (Steam tab)");
    }

    /// <summary>
    /// Joins a Steam lobby from the browser using the Steam lobby ID.
    /// Uses Steam's native join flow, not MMS invite codes.
    /// </summary>
    /// <param name="steamLobbyIdString">The Steam lobby ID as a string.</param>
    private void JoinSteamLobbyFromBrowser(string steamLobbyIdString) {
        if (!SteamManager.IsInitialized) {
            ShowFeedback(Color.red, "Steam is not available.");
            return;
        }

        if (!ulong.TryParse(steamLobbyIdString, out var steamLobbyId)) {
            ShowFeedback(Color.red, "Invalid Steam lobby ID.");
            return;
        }

        ShowFeedback(Color.yellow, "Joining Steam lobby...");
        SteamManager.JoinLobby(new CSteamID(steamLobbyId));
    }

    /// <summary>
    /// Handles the Steam tab's "Join Friend" button press.
    /// Opens the Steam Friends overlay to allow joining via friend invite.
    /// </summary>
    private void OnJoinFriendButtonPressed() {
        if (!SteamManager.IsInitialized) {
            ShowFeedback(Color.red, "Steam is not available.");
            return;
        }

        SteamFriends.ActivateGameOverlay("Friends");
        ShowFeedback(Color.yellow, "Opened Steam Friends. Right-click friend to Join Game.");
    }

    #endregion

    #region Button Callbacks - Direct IP Tab

    /// <summary>
    /// Handles the Direct IP tab's "Connect" button press.
    /// Validates inputs and initiates a direct IP connection to a server.
    /// </summary>
    private void OnDirectConnectButtonPressed() {
        var address = _addressInput.GetInput();
        if (string.IsNullOrEmpty(address)) {
            ShowFeedback(Color.red, ErrorEnterAddress);
            return;
        }

        if (!TryParsePort(_portInput.GetInput(), out var port)) {
            ShowFeedback(Color.red, ErrorEnterValidPort);
            return;
        }

        if (!ValidateUsername(out var username)) {
            return;
        }

        SaveConnectionSettings(address, port, username);

        _directConnectButton.SetText(ConnectingText);
        _directConnectButton.SetInteractable(false);

        Logger.Debug($"Connecting to {address}:{port} as {username}");
        ConnectButtonPressed?.Invoke(address, port, username, TransportType.Udp);
    }

    /// <summary>
    /// Handles the Direct IP tab's "Host" button press.
    /// Validates inputs and starts hosting a server on the specified port.
    /// </summary>
    private void OnStartButtonPressed() {
        if (!TryParsePort(_portInput.GetInput(), out var port)) {
            ShowFeedback(Color.red, ErrorEnterValidPortHost);
            return;
        }

        if (!ValidateUsername(out var username)) {
            return;
        }

        StartHostButtonPressed?.Invoke("", port, username, TransportType.Udp);
    }

    #endregion

    #region Steam Event Callbacks

    /// <summary>
    /// Called when a Steam lobby is successfully created.
    /// Displays success message and triggers server hosting.
    /// </summary>
    /// <param name="lobbyId">The unique Steam ID of the created lobby.</param>
    /// <param name="username">The username of the lobby host.</param>
    private void OnSteamLobbyCreated(CSteamID lobbyId, string username) {
        Logger.Info($"Lobby created: {lobbyId}");
        ShowFeedback(Color.green, "Lobby created! Friends can join via Steam overlay.");

        // Start hosting with Steam transport (port 0 as it's not used for Steam P2P)
        StartHostButtonPressed?.Invoke("", 0, username, TransportType.Steam);
    }

    /// <summary>
    /// Called when the list of available Steam lobbies is received.
    /// Auto-joins the first lobby if any are found.
    /// </summary>
    /// <param name="lobbyIds">Array of Steam lobby IDs found in the search.</param>
    private void OnLobbyListReceived(CSteamID[] lobbyIds) {
        if (lobbyIds.Length == 0) {
            ShowFeedback(Color.yellow, "No lobbies found.");
            return;
        }

        Logger.Info($"Found {lobbyIds.Length} lobbies. Auto-joining first one.");
        ShowFeedback(Color.yellow, $"Found {lobbyIds.Length} lobbies. Joining first...");

        SteamManager.JoinLobby(lobbyIds[0]);
    }

    /// <summary>
    /// Called when successfully joined a Steam lobby.
    /// Initiates connection to the lobby host via Steam P2P.
    /// </summary>
    /// <param name="lobbyId">The Steam ID of the joined lobby.</param>
    private void OnLobbyJoined(CSteamID lobbyId) {
        Logger.Info($"Joined lobby: {lobbyId}");
        ShowFeedback(Color.green, "Joined lobby! Connecting to host...");

        var hostId = SteamManager.GetLobbyOwner(lobbyId);

        if (!ValidateUsername(out var username)) {
            return;
        }

        // Connect using Steam ID as address with Steam transport
        ConnectButtonPressed?.Invoke(hostId.ToString(), 0, username, TransportType.Steam);
    }

    #endregion

    #region Connection Event Callbacks

    /// <summary>
    /// Called when the client successfully establishes a connection to the server.
    /// Resets UI state and displays success message.
    /// </summary>
    public void OnSuccessfulConnect() {
        ShowFeedback(Color.green, MsgConnected);
        ResetConnectionButtons();
    }

    /// <summary>
    /// Called when the client disconnects from the server.
    /// Resets the connection UI to allow reconnection.
    /// </summary>
    public void OnClientDisconnect() {
        ResetConnectionButtons();
    }

    /// <summary>
    /// Called when a connection attempt fails.
    /// Displays an appropriate error message based on the failure reason.
    /// </summary>
    /// <param name="result">Details about why the connection failed.</param>
    public void OnFailedConnect(ConnectionFailedResult result) {
        var message = GetFailureMessage(result);
        ShowFeedback(Color.red, message);
        ResetConnectionButtons();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Validates the username input field.
    /// </summary>
    /// <param name="username">Output parameter containing the validated username.</param>
    /// <returns>True if username is valid, false otherwise.</returns>
    private bool ValidateUsername(out string username) {
        if (!ConnectInterfaceHelpers.ValidateUsername(
                _usernameInput,
                _feedbackText,
                out username,
                _feedbackHideCoroutine,
                out var newCoroutine
            )) {
            _feedbackHideCoroutine = newCoroutine;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Attempts to parse a port number string into a valid integer port.
    /// </summary>
    /// <param name="portString">The string to parse.</param>
    /// <param name="port">Output parameter containing the parsed port number.</param>
    /// <returns>True if parsing succeeded and port is valid (non-zero), false otherwise.</returns>
    private bool TryParsePort(string portString, out int port) {
        return int.TryParse(portString, out port) && port != 0;
    }

    /// <summary>
    /// Saves connection settings (address, port, username) to persistent storage.
    /// </summary>
    private void SaveConnectionSettings(string address, int port, string username) {
        _modSettings.ConnectAddress = address;
        _modSettings.ConnectPort = port;
        _modSettings.Username = username;
        _modSettings.Save();
    }

    /// <summary>
    /// Displays a feedback message to the user with the specified color.
    /// Automatically hides the message after a delay.
    /// </summary>
    /// <param name="color">The color of the feedback text.</param>
    /// <param name="message">The message to display.</param>
    private void ShowFeedback(Color color, string message) {
        _feedbackHideCoroutine = ConnectInterfaceHelpers.SetFeedbackText(
            _feedbackText,
            color,
            message,
            _feedbackHideCoroutine
        );
    }

    /// <summary>
    /// Resets the connection buttons to their default state after a connection attempt.
    /// </summary>
    private void ResetConnectionButtons() {
        ConnectInterfaceHelpers.ResetConnectButtons(_directConnectButton, _lobbyConnectButton);
    }

    /// <summary>
    /// Converts a connection failure result into a user-friendly error message.
    /// </summary>
    /// <param name="result">The connection failure details.</param>
    /// <returns>A formatted error message string.</returns>
    private string GetFailureMessage(ConnectionFailedResult result) {
        return result.Reason switch {
            ConnectionFailedReason.InvalidAddons => ErrorInvalidAddons,
            ConnectionFailedReason.SocketException or
                ConnectionFailedReason.IOException => ErrorInternal,
            ConnectionFailedReason.TimedOut => ErrorTimeout,
            ConnectionFailedReason.Other =>
                $"Failed to connect:\n{((ConnectionFailedMessageResult) result).Message}",
            _ => ErrorUnknown
        };
    }

    #endregion
}

using System;
using System.Collections;
using SSMP.Game;
using SSMP.Game.Settings;
using SSMP.Networking.Client;
using SSMP.Ui.Component;
using SSMP.Util;
using Steamworks;
using SSMP.Networking.Transport.Common;
using SSMP.Networking;
using SSMP.Ui.Util;
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
    /// The time in seconds to hide the feedback text after it appeared.
    /// </summary>
    private const float FeedbackTextHideTime = 10f;

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

    /// <summary>
    /// The width of the background panel.
    /// </summary>
    private const float PanelWidth = 450f;

    /// <summary>
    /// The height of the background panel.
    /// </summary>
    private const float PanelHeight = 600f;

    /// <summary>
    /// The corner radius for the background panel.
    /// </summary>
    private const int PanelCornerRadius = 20;

    /// <summary>
    /// The width of the glowing notch.
    /// </summary>
    private const float NotchWidth = 450f;

    /// <summary>
    /// The height of the glowing notch.
    /// </summary>
    private const float NotchHeight = 4f;

    #endregion

    #region Text Constants

    /// <summary>
    /// The text of the connection button if not connected.
    /// </summary>
    private const string ConnectText = "CONNECT";

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
    /// Error message when lobby ID is missing.
    /// </summary>
    private const string ErrorEnterLobbyId = "Failed to connect:\nYou must enter a Lobby ID";

    /// <summary>
    /// Error message for unimplemented lobby connection.
    /// </summary>
    private const string ErrorLobbyNotImplemented = "Lobby connection not yet implemented";

    /// <summary>
    /// Error message for unimplemented Steam integration.
    /// </summary>
    private const string ErrorSteamNotImplemented = "Steam integration not yet implemented";

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
    /// Error message when username is missing.
    /// </summary>
    private const string ErrorEnterUsername = "Failed to connect:\nYou must enter a username";

    /// <summary>
    /// Error message when username is too long.
    /// </summary>
    private const string ErrorUsernameTooLong = "Failed to connect:\nUsername is too long";

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
    private readonly TabButtonComponent _steamTab;

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
    private readonly ComponentGroup _steamGroup;

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
    /// Cached reflection field for accessing ButtonComponent GameObject.
    /// </summary>
    private static readonly System.Reflection.FieldInfo? GameObjectField = typeof(ButtonComponent).GetField("GameObject", 
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    /// <summary>
    /// Cached reflection field for accessing ComponentGroup components.
    /// </summary>
    private static readonly System.Reflection.FieldInfo? ComponentsField = typeof(ComponentGroup).GetField("_components", 
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    /// <summary>
    /// Cached reflection field for accessing ComponentGroup children.
    /// </summary>
    private static readonly System.Reflection.FieldInfo? ChildrenField = typeof(ComponentGroup).GetField("_children",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    /// <summary>
    /// Event that is executed when the connect button is pressed.
    /// </summary>
    public event Action<ConnectionDetails>? ConnectButtonPressed;

    /// <summary>
    /// Event that is executed when the start hosting button is pressed.
    /// </summary>
    public event Action<ConnectionDetails>? StartHostButtonPressed;

    #endregion

    /// <summary>
    /// Initializes a new instance of the ConnectInterface class.
    /// </summary>
    /// <param name="modSettings">The mod settings.</param>
    /// <param name="connectGroup">The component group for the connect interface.</param>
    public ConnectInterface(ModSettings modSettings, ComponentGroup connectGroup) {
        _modSettings = modSettings;
        
        // Subscribe to Steam lobby events
        SteamManager.LobbyCreatedEvent += OnSteamLobbyCreated;
        SteamManager.LobbyListReceivedEvent += OnLobbyListReceived;
        SteamManager.LobbyJoinedEvent += OnLobbyJoined;

        var x = InitialX;
        var y = InitialY;

        // Header
        new TextComponent(connectGroup, new Vector2(x, y), new Vector2(HeaderWidth, HeaderHeight), 
            HeaderText, 32, alignment: TextAnchor.MiddleCenter);
        y -= HeaderToNotchSpacing;
        
        // Create white notch with glow effect under header
        _glowingNotch = CreateGlowingNotch(x, y);
        y -= NotchToPanelSpacing;

        // Background panel
        _backgroundPanel = CreateBackgroundPanel(x, y);
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
        _matchmakingTab = CreateTabButton(_backgroundGroup, x - tabWidth, tabY, tabWidth, MatchmakingTabText, () => SwitchTab(Tab.Matchmaking));
        _steamTab = CreateTabButton(_backgroundGroup, x, tabY, tabWidth, SteamTabText, () => SwitchTab(Tab.Steam));
        _directIpTab = CreateTabButton(_backgroundGroup, x + tabWidth, tabY, tabWidth, DirectIpTabText, () => SwitchTab(Tab.DirectIp));
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

        ReparentComponentGroup(_backgroundGroup);
        PositionTabButtonsFixed();
        SwitchTab(Tab.Matchmaking);
    }

    /// <summary>
    /// Creates a glowing horizontal notch under the multiplayer header.
    /// </summary>
    /// <param name="x">The x position.</param>
    /// <param name="y">The y position.</param>
    /// <returns>The created notch GameObject.</returns>
    private GameObject CreateGlowingNotch(float x, float y) {
        var notchObject = new GameObject("GlowingNotch");
        var rect = notchObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(x / 1920f, y / 1080f);
        rect.sizeDelta = new Vector2(NotchWidth, NotchHeight);
        rect.pivot = new Vector2(0.5f, 0.5f);
        
        var image = notchObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = Sprite.Create(
            UiUtils.CreateHorizontalGradientTexture(256, 1),
            new Rect(0, 0, 256, 1),
            new Vector2(0.5f, 0.5f)
        );
        image.color = Color.white;
        
        notchObject.transform.SetParent(UiManager.UiGameObject!.transform, false);
        UnityEngine.Object.DontDestroyOnLoad(notchObject);
        notchObject.SetActive(false);
        
        return notchObject;
    }

    /// <summary>
    /// Creates the background panel GameObject with rounded corners.
    /// </summary>
    /// <param name="x">The x position.</param>
    /// <param name="y">The y position.</param>
    /// <returns>The background panel GameObject.</returns>
    private GameObject CreateBackgroundPanel(float x, float y) {
        var panel = new GameObject("MenuBackground");
        var rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(x / 1920f, y / 1080f);
        rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        rect.pivot = new Vector2(0.5f, 1f);
        
        var image = panel.AddComponent<UnityEngine.UI.Image>();
        image.color = Color.white;
        
        var roundedTexture = UiUtils.CreateRoundedRectTexture((int)PanelWidth, (int)PanelHeight, PanelCornerRadius);
        image.sprite = Sprite.Create(
            roundedTexture,
            new Rect(0, 0, roundedTexture.width, roundedTexture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(20, 20, 20, 20)
        );
        image.type = UnityEngine.UI.Image.Type.Sliced;
        
        panel.transform.SetParent(UiManager.UiGameObject!.transform, false);
        panel.transform.SetAsFirstSibling();
        UnityEngine.Object.DontDestroyOnLoad(panel);
        panel.SetActive(false);
        return panel;
    }

    /// <summary>
    /// Creates a tab button component using TabButtonComponent.
    /// </summary>
    /// <param name="group">The component group.</param>
    /// <param name="x">The x position.</param>
    /// <param name="y">The y position.</param>
    /// <param name="width">The width of the button.</param>
    /// <param name="text">The button text.</param>
    /// <param name="onPress">The press callback.</param>
    /// <returns>The created button component.</returns>
    private TabButtonComponent CreateTabButton(ComponentGroup group, float x, float y, float width, string text, Action onPress) {
        var button = new TabButtonComponent(group, new Vector2(x, y), new Vector2(width, 50f), 
            text, Resources.TextureManager.ButtonBg, Resources.FontManager.UIFontRegular, 18);
        button.SetOnPress(onPress);
        return button;
    }

    /// <summary>
    /// Positions the tab buttons to fit within the background panel bounds.
    /// </summary>
    private void PositionTabButtonsFixed() {
        if (GameObjectField == null) return;
        
        var bgWidth = _backgroundPanel.GetComponent<RectTransform>().sizeDelta.x;
        var buttonWidth = bgWidth / 3f;
        
        AdjustButtonFixed(GameObjectField.GetValue(_matchmakingTab) as GameObject, -buttonWidth, buttonWidth);
        AdjustButtonFixed(GameObjectField.GetValue(_steamTab) as GameObject, 0f, buttonWidth);
        AdjustButtonFixed(GameObjectField.GetValue(_directIpTab) as GameObject, buttonWidth, buttonWidth);
    }

    /// <summary>
    /// Adjusts a button's position and width.
    /// </summary>
    /// <param name="buttonGameObject">The button GameObject.</param>
    /// <param name="xPosition">The x position.</param>
    /// <param name="width">The width.</param>
    private void AdjustButtonFixed(GameObject? buttonGameObject, float xPosition, float width) {
        if (buttonGameObject == null) return;
        var rectTransform = buttonGameObject.GetComponent<RectTransform>();
        if (rectTransform == null) return;
        rectTransform.sizeDelta = new Vector2(width, 50f);
        var position = rectTransform.localPosition;
        position.x = xPosition;
        rectTransform.localPosition = position;
    }

    /// <summary>
    /// Reparents a ComponentGroup to be a child of the background panel using reflection.
    /// </summary>
    /// <param name="group">The component group to reparent.</param>
    private void ReparentComponentGroup(ComponentGroup group) {
        // Process components
        if (ComponentsField != null && ComponentsField.GetValue(group) is IEnumerable components) {
            foreach (var component in components) {
                if (component == null) continue;
                
                var gameObjectField = component.GetType().GetField("GameObject",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (gameObjectField?.GetValue(component) is GameObject gameObject) {
                    gameObject.transform.SetParent(_backgroundPanel.transform, true);
                }
            }
        }
        
        // Process children recursively
        if (ChildrenField != null && ChildrenField.GetValue(group) is IEnumerable children) {
            foreach (var child in children) {
                if (child is ComponentGroup childGroup) {
                    ReparentComponentGroup(childGroup);
                }
            }
        }
    }

    /// <summary>
    /// Switches to a different tab.
    /// </summary>
    /// <param name="tab">The tab to switch to.</param>
    private void SwitchTab(Tab tab) {
        _activeTab = tab;
        
        // Update tab button states
        _matchmakingTab.SetTabActive(tab == Tab.Matchmaking);
        _steamTab.SetTabActive(tab == Tab.Steam);
        _directIpTab.SetTabActive(tab == Tab.DirectIp);
        
        // Show/hide content groups
        _matchmakingGroup.SetActive(tab == Tab.Matchmaking);
        _steamGroup.SetActive(tab == Tab.Steam);
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
        if (!SteamManager.IsInitialized) {
            SetFeedbackText(Color.red, "Steam is not available.");
            return;
        }
        
        SetFeedbackText(Color.yellow, "Searching for lobbies...");
        SteamManager.RequestLobbyList();
    }

    /// <summary>
    /// Callback method for when the create lobby button is pressed.
    /// </summary>
    private void OnCreateLobbyButtonPressed() {
        if (!SteamManager.IsInitialized) {
            SetFeedbackText(Color.red, "Steam is not available. Please ensure Steam is running.");
            Logger.Warn("Cannot create Steam lobby: Steam is not initialized");
            return;
        }

        if (!ValidateUsername(out var username)) return;
        
        SetFeedbackText(Color.yellow, "Creating Steam lobby...");
        Logger.Info($"Create lobby requested for user: {username}");

        // Create lobby via SteamManager
        SteamManager.CreateLobby(username);
    }

    /// <summary>
    /// Callback method for when the browse lobby button is pressed.
    /// </summary>
    private void OnBrowseLobbyButtonPressed() {
        if (!SteamManager.IsInitialized) {
            SetFeedbackText(Color.red, "Steam is not available.");
            return;
        }
        
        SetFeedbackText(Color.yellow, "Refreshing lobby list...");
        SteamManager.RequestLobbyList();
    }

    /// <summary>
    /// Callback for when a Steam lobby is created.
    /// </summary>
    private void OnSteamLobbyCreated(CSteamID lobbyId, string username) {
        Logger.Info($"Lobby created: {lobbyId}");
        SetFeedbackText(Color.green, $"Lobby created! Friends can join via Steam overlay.");

        // Fire event to start server hosting
        // Port is ignored for Steam P2P, but we pass 0 for consistency
        StartHostButtonPressed?.Invoke(new ConnectionDetails("", 0, username, TransportType.Steam)); 
    }

    /// <summary>
    /// Callback for Join Friend button (Steam tab).
    /// </summary>
    private void OnJoinFriendButtonPressed() {
        if (!SteamManager.IsInitialized) {
            SetFeedbackText(Color.red, "Steam is not available.");
            return;
        }

        // Open Steam Friends overlay
        SteamFriends.ActivateGameOverlay("Friends");
        SetFeedbackText(Color.yellow, "Opened Steam Friends. Right-click friend to Join Game.");
    }

    /// <summary>
    /// Callback for when a list of lobbies is received.
    /// </summary>
    private void OnLobbyListReceived(CSteamID[] lobbyIds) {
        if (lobbyIds.Length == 0) {
            SetFeedbackText(Color.yellow, "No lobbies found.");
            return;
        }

        Logger.Info($"Found {lobbyIds.Length} lobbies. Auto-joining first one for now.");
        SetFeedbackText(Color.yellow, $"Found {lobbyIds.Length} lobbies. Joining first...");
        
        // For now, just join the first one
        SteamManager.JoinLobby(lobbyIds[0]);
    }

    /// <summary>
    /// Callback for when a lobby is joined.
    /// </summary>
    private void OnLobbyJoined(CSteamID lobbyId) {
        Logger.Info($"Joined lobby: {lobbyId}");
        SetFeedbackText(Color.green, "Joined lobby! Connecting to host...");
        
        var hostId = SteamManager.GetLobbyOwner(lobbyId);
        
        if (!ValidateUsername(out var username)) username = "Player"; // Fallback

        // Connect using Steam ID as address
        ConnectButtonPressed?.Invoke(new ConnectionDetails(hostId.ToString(), 0, username, TransportType.Steam));
    }

    /// <summary>
    /// Callback method for when the direct connect button is pressed.
    /// </summary>
    private void OnDirectConnectButtonPressed() {
        var address = _addressInput.GetInput();
        if (address.Length == 0) {
            SetFeedbackText(Color.red, ErrorEnterAddress);
            return;
        }
        
        var portString = _portInput.GetInput();
        if (!int.TryParse(portString, out var port) || port == 0) {
            SetFeedbackText(Color.red, ErrorEnterValidPort);
            return;
        }
        
        if (!ValidateUsername(out var username)) return;
        
        // Save settings
        _modSettings.ConnectAddress = address;
        _modSettings.ConnectPort = port;
        _modSettings.Username = username;
        _modSettings.Save();
        
        _directConnectButton.SetText(ConnectingText);
        _directConnectButton.SetInteractable(false);
        
        Logger.Debug($"Connecting to {address}:{port} as {username}");
        ConnectButtonPressed?.Invoke(new ConnectionDetails(address, port, username, TransportType.Udp));
    }

    /// <summary>
    /// Callback method for when the start hosting button is pressed.
    /// </summary>
    private void OnStartButtonPressed() {
        var portString = _portInput.GetInput();
        if (!int.TryParse(portString, out var port) || port == 0) {
            SetFeedbackText(Color.red, ErrorEnterValidPortHost);
            return;
        }
        if (!ValidateUsername(out var username)) return;
        StartHostButtonPressed?.Invoke(new ConnectionDetails("", port, username, TransportType.Udp));
    }

    /// <summary>
    /// Callback method for when the client disconnects.
    /// </summary>
    public void OnClientDisconnect() {
        ResetConnectButtons();
    }

    /// <summary>
    /// Callback method for when the client successfully connects.
    /// </summary>
    public void OnSuccessfulConnect() {
        SetFeedbackText(Color.green, MsgConnected);
        ResetConnectButtons();
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
        
        SetFeedbackText(Color.red, message);
        ResetConnectButtons();
    }

    /// <summary>
    /// Resets connect buttons to their default state.
    /// </summary>
    private void ResetConnectButtons() {
        _directConnectButton.SetText(ConnectText);
        _directConnectButton.SetInteractable(true);
        _lobbyConnectButton.SetText(ConnectText);
        _lobbyConnectButton.SetInteractable(true);
    }

    /// <summary>
    /// Sets the feedback text with the given color and content.
    /// </summary>
    /// <param name="color">The color of the text.</param>
    /// <param name="text">The content of the text.</param>
    private void SetFeedbackText(Color color, string text) {
        _feedbackText.SetColor(color);
        _feedbackText.SetText(text);
        _feedbackText.SetActive(true);
        
        if (_feedbackHideCoroutine != null) {
            MonoBehaviourUtil.Instance.StopCoroutine(_feedbackHideCoroutine);
        }
        _feedbackHideCoroutine = MonoBehaviourUtil.Instance.StartCoroutine(WaitHideFeedbackText());
    }

    /// <summary>
    /// Coroutine for hiding the feedback text after a delay.
    /// </summary>
    /// <returns>An enumerator for the coroutine.</returns>
    private IEnumerator WaitHideFeedbackText() {
        yield return new WaitForSeconds(FeedbackTextHideTime);
        _feedbackText.SetActive(false);
    }

    /// <summary>
    /// Validates the username input.
    /// </summary>
    /// <param name="username">The validated username.</param>
    /// <returns>True if the username is valid, false otherwise.</returns>
    private bool ValidateUsername(out string username) {
        username = _usernameInput.GetInput();
        
        if (username.Length == 0) {
            SetFeedbackText(Color.red, ErrorEnterUsername);
            return false;
        }
        
        if (username.Length > 32) {
            SetFeedbackText(Color.red, ErrorUsernameTooLong);
            return false;
        }
        
        return true;
    }
}

using System;
using SSMP.Game;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SSMP.Ui.Component;

/// <summary>
/// A modal panel for configuring lobby settings before creation.
/// Shows lobby name, max players, visibility toggle, and create/cancel buttons.
/// </summary>
internal class LobbyConfigPanel : IComponent {
    private GameObject GameObject { get; }
    private readonly InputField _nameInput;
    private readonly Text _visibilityText = null!;
    private LobbyVisibility _visibility = LobbyVisibility.Public;
    private Action<string, LobbyVisibility>? _onCreate;
    private Action? _onCancel;
    private bool _activeSelf;
    private readonly ComponentGroup _componentGroup;
    private readonly string _lobbyType;

    private const float HeaderHeight = 35f;
    private const float RowHeight = 40f;
    private const float RowSpacing = 12f;
    private const float ButtonHeight = 45f;
    private const float Padding = 15f;

    /// <summary>
    /// Creates a new lobby configuration panel.
    /// </summary>
    /// <param name="parent">Parent component group</param>
    /// <param name="position">Center position</param>
    /// <param name="size">Panel size</param>
    /// <param name="lobbyType">Type of lobby: "steam" or "matchmaking"</param>
    public LobbyConfigPanel(ComponentGroup parent, Vector2 position, Vector2 size, string lobbyType = "matchmaking") {
        _lobbyType = lobbyType;

        // Create main container
        GameObject = new GameObject("LobbyConfigPanel");
        var rect = GameObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(position.x / 1920f, position.y / 1080f);
        rect.sizeDelta = size;
        rect.pivot = new Vector2(0.5f, 1f);

        var currentY = 0f;

        // Header
        var header = CreateText(
            "HOST LOBBY",
            new Vector2(0f, currentY),
            size.x,
            HeaderHeight,
            18,
            new Color(1f, 0.85f, 0.6f, 1f)
        );
        header.transform.SetParent(GameObject.transform, false);
        currentY -= HeaderHeight + RowSpacing;

        // Lobby Name row
        var nameLabel = CreateText(
            "Lobby Name:",
            new Vector2(-size.x / 4f - 10f, currentY),
            size.x / 2f - 20f,
            RowHeight,
            14,
            Color.white,
            TextAnchor.MiddleLeft
        );
        nameLabel.transform.SetParent(GameObject.transform, false);

        var nameInputGo = CreateInputField(
            new Vector2(size.x / 4f - 10f, currentY),
            size.x / 2f,
            RowHeight,
            ""
        );
        nameInputGo.transform.SetParent(GameObject.transform, false);
        _nameInput = nameInputGo.GetComponent<InputField>();
        currentY -= RowHeight + RowSpacing;

        currentY -= RowSpacing;

        // Visibility row (Steam only - matchmaking is always public)
        if (_lobbyType == "steam") {
            var visLabel = CreateText(
                "Visibility:",
                new Vector2(-size.x / 4f - 10f, currentY),
                size.x / 2f - 20f,
                RowHeight,
                14,
                Color.white,
                TextAnchor.MiddleLeft
            );
            visLabel.transform.SetParent(GameObject.transform, false);

            var visSelector = new GameObject("VisibilitySelector");
            var vRect = visSelector.AddComponent<RectTransform>();
            vRect.anchorMin = vRect.anchorMax = new Vector2(0.5f, 1f);
            vRect.pivot = new Vector2(0.5f, 0.5f);
            vRect.anchoredPosition = new Vector2(size.x / 4f - 10f, currentY - RowHeight / 2f);
            vRect.sizeDelta = new Vector2(size.x / 2f, RowHeight);

            // < button
            var prevVisBtn = CreateButton("<", new Vector2(-70f, 0f), new Vector2(30f, 30f), OnPrevVisibility);
            prevVisBtn.transform.SetParent(visSelector.transform, false);

            // Visibility text
            var visTextGo = CreateText("Public", Vector2.zero, 100f, RowHeight, 14, new Color(0.5f, 1f, 0.5f, 1f));
            visTextGo.transform.SetParent(visSelector.transform, false);
            _visibilityText = visTextGo.GetComponent<Text>();

            // > button
            var nextVisBtn = CreateButton(">", new Vector2(70f, 0f), new Vector2(30f, 30f), OnNextVisibility);
            nextVisBtn.transform.SetParent(visSelector.transform, false);

            visSelector.transform.SetParent(GameObject.transform, false);
            currentY -= RowHeight + RowSpacing * 2;
        } else {
            // Matchmaking is always public
            _visibility = LobbyVisibility.Public;
            currentY -= RowSpacing;
        }

        // Buttons row
        var buttonWidth = (size.x - Padding * 3) / 2f;

        var cancelBtn = CreateButton(
            "CANCEL",
            new Vector2(-buttonWidth / 2f - Padding / 2f, currentY - ButtonHeight / 2f),
            new Vector2(buttonWidth, ButtonHeight),
            () => _onCancel?.Invoke(),
            new Color(0.4f, 0.4f, 0.4f, 1f)
        );
        cancelBtn.transform.SetParent(GameObject.transform, false);

        var createBtn = CreateButton(
            "CREATE",
            new Vector2(buttonWidth / 2f + Padding / 2f, currentY - ButtonHeight / 2f),
            new Vector2(buttonWidth, ButtonHeight),
            OnCreatePressed,
            new Color(0.2f, 0.6f, 0.3f, 1f)
        );
        createBtn.transform.SetParent(GameObject.transform, false);

        // Component group setup - same pattern as LobbyBrowserPanel
        _componentGroup = parent;
        _activeSelf = false;
        parent.AddComponent(this);
        GameObject.transform.SetParent(UiManager.UiGameObject!.transform, false);
        Object.DontDestroyOnLoad(GameObject);
        GameObject.SetActive(false);
    }

    #region Public API

    public void SetDefaultName(string name) => _nameInput.text = name;
    public void SetOnCreate(Action<string, LobbyVisibility> callback) => _onCreate = callback;
    public void SetOnCancel(Action callback) => _onCancel = callback;
    public string LobbyType => _lobbyType;
    public bool IsVisible => _activeSelf;

    public void Show() {
        GameObject.SetActive(true);
        _activeSelf = true;
    }

    public void Hide() {
        GameObject.SetActive(false);
        _activeSelf = false;
    }

    #endregion

    #region IComponent

    public void SetGroupActive(bool groupActive) {
        if (GameObject == null) return;
        GameObject.SetActive(_activeSelf && groupActive);
    }

    public void SetActive(bool active) {
        _activeSelf = active;
        GameObject.SetActive(_activeSelf && _componentGroup.IsActive());
    }

    public Vector2 GetPosition() {
        var rectTransform = GameObject.GetComponent<RectTransform>();
        var position = rectTransform.anchorMin;
        return new Vector2(position.x * 1920f, position.y * 1080f);
    }

    public void SetPosition(Vector2 position) {
        var rect = GameObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(position.x / 1920f, position.y / 1080f);
    }

    public Vector2 GetSize() {
        var rect = GameObject.GetComponent<RectTransform>();
        return rect.sizeDelta;
    }

    #endregion

    #region Private Helpers

    private void OnCreatePressed() {
        var name = _nameInput.text;
        if (string.IsNullOrWhiteSpace(name)) {
            name = "Unnamed Lobby";
        }

        _onCreate?.Invoke(name, _visibility);
    }

    private void OnPrevVisibility() {
        _visibility = _visibility switch {
            LobbyVisibility.Public => LobbyVisibility.Private,
            LobbyVisibility.FriendsOnly => LobbyVisibility.Public,
            LobbyVisibility.Private => LobbyVisibility.FriendsOnly,
            _ => LobbyVisibility.Public
        };
        UpdateVisibilityText();
    }

    private void OnNextVisibility() {
        _visibility = _visibility switch {
            LobbyVisibility.Public => LobbyVisibility.FriendsOnly,
            LobbyVisibility.FriendsOnly => LobbyVisibility.Private,
            _ => LobbyVisibility.Public
        };
        UpdateVisibilityText();
    }

    private void UpdateVisibilityText() {
        _visibilityText.text = _visibility switch {
            LobbyVisibility.Public => "Public",
            LobbyVisibility.FriendsOnly => "Friends",
            LobbyVisibility.Private => "Private",
            _ => "Public"
        };
        _visibilityText.color = _visibility switch {
            LobbyVisibility.Public => new Color(0.5f, 1f, 0.5f, 1f),
            LobbyVisibility.FriendsOnly => new Color(0.5f, 0.7f, 1f, 1f),
            LobbyVisibility.Private => new Color(1f, 0.7f, 0.5f, 1f),
            _ => Color.white
        };
    }

    private static GameObject CreateText(
        string text,
        Vector2 pos,
        float width,
        float height,
        int fontSize,
        Color color,
        TextAnchor alignment = TextAnchor.MiddleCenter
    ) {
        var go = new GameObject("Text");
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(pos.x, pos.y - height / 2f);
        rect.sizeDelta = new Vector2(width, height);

        var txt = go.AddComponent<Text>();
        txt.text = text;
        txt.font = Resources.FontManager.UIFontRegular;
        txt.fontSize = fontSize;
        txt.alignment = alignment;
        txt.color = color;

        return go;
    }

    private static GameObject CreateInputField(Vector2 pos, float width, float height, string placeholder) {
        var go = new GameObject("InputField");
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(pos.x, pos.y - height / 2f);
        rect.sizeDelta = new Vector2(width, height);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.15f, 0.18f, 1f);

        var textGo = new GameObject("Text");
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 5f);
        textRect.offsetMax = new Vector2(-10f, -5f);
        var text = textGo.AddComponent<Text>();
        text.font = Resources.FontManager.UIFontRegular;
        text.fontSize = 14;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        textGo.transform.SetParent(go.transform, false);

        var placeholderGo = new GameObject("Placeholder");
        var phRect = placeholderGo.AddComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = new Vector2(10f, 5f);
        phRect.offsetMax = new Vector2(-10f, -5f);
        var phText = placeholderGo.AddComponent<Text>();
        phText.font = Resources.FontManager.UIFontRegular;
        phText.fontSize = 14;
        phText.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        phText.alignment = TextAnchor.MiddleLeft;
        phText.text = placeholder;
        placeholderGo.transform.SetParent(go.transform, false);

        var input = go.AddComponent<InputField>();
        input.textComponent = text;
        input.placeholder = phText;

        return go;
    }

    private static GameObject CreateButton(
        string text,
        Vector2 pos,
        Vector2 size,
        Action onClick,
        Color? bgColor = null
    ) {
        var go = new GameObject("Button");
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        var bg = go.AddComponent<Image>();
        bg.color = bgColor ?? new Color(0.25f, 0.25f, 0.28f, 1f);

        var textGo = new GameObject("Text");
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var txt = textGo.AddComponent<Text>();
        txt.text = text;
        txt.font = Resources.FontManager.UIFontRegular;
        txt.fontSize = 14;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;
        textGo.transform.SetParent(go.transform, false);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(onClick.Invoke);

        return go;
    }

    #endregion
}

/// <summary>
/// Visibility options for lobbies.
/// </summary>
public enum LobbyVisibility {
    /// <summary>
    /// Anyone can find and join via browser.
    /// </summary>
    Public,

    /// <summary>
    /// Only friends can see and join.
    /// </summary>
    FriendsOnly,

    /// <summary>
    /// Invite-only, not discoverable.
    /// </summary>
    Private
}

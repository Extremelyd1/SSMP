using System;
using System.Collections.Generic;
using SSMP.Networking.Matchmaking;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SSMP.Ui.Component;

/// <summary>
/// A full-screen overlay panel that displays public lobbies from MMS.
/// Includes Back and Refresh buttons at the bottom.
/// </summary>
internal class LobbyBrowserPanel : IComponent {
    private GameObject GameObject { get; }
    private readonly RectTransform _content;
    private readonly Text _emptyText;
    private readonly List<GameObject> _lobbyEntries = [];
    private Action<PublicLobbyInfo>? _onLobbySelected;
    private Action? _onBack;
    private Action? _onRefresh;
    private bool _activeSelf;
    private readonly ComponentGroup _componentGroup;

    private const float EntryHeight = 50f;
    private const float EntrySpacing = 8f;
    private const float Padding = 15f;
    private const float HeaderHeight = 35f;
    private const float ButtonAreaHeight = 60f;

    public LobbyBrowserPanel(ComponentGroup parent, Vector2 position, Vector2 size) {
        // Create main container - no background, sits inside existing panel
        GameObject = new GameObject("LobbyBrowserPanel");
        var rect = GameObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(position.x / 1920f, position.y / 1080f);
        rect.sizeDelta = size;
        rect.pivot = new Vector2(0.5f, 1f); // Top-center pivot to align with content area

        // Header: "PUBLIC LOBBIES"
        var header = new GameObject("Header");
        var headerRect = header.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = Vector2.zero;
        headerRect.sizeDelta = new Vector2(0f, HeaderHeight);
        var headerText = header.AddComponent<Text>();
        headerText.text = "PUBLIC LOBBIES";
        headerText.font = Resources.FontManager.UIFontRegular;
        headerText.fontSize = 18;
        headerText.alignment = TextAnchor.MiddleCenter;
        headerText.color = new Color(1f, 0.85f, 0.6f, 1f); // Gold/orange accent
        header.transform.SetParent(GameObject.transform, false);

        // Scroll view for lobby list (between header and buttons)
        var scrollView = new GameObject("ScrollView");
        var scrollRect = scrollView.AddComponent<ScrollRect>();
        var scrollRectTransform = scrollView.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = new Vector2(0f, 0f);
        scrollRectTransform.anchorMax = new Vector2(1f, 1f);
        scrollRectTransform.offsetMin = new Vector2(Padding, ButtonAreaHeight);
        scrollRectTransform.offsetMax = new Vector2(-Padding, -HeaderHeight - Padding);
        scrollView.transform.SetParent(GameObject.transform, false);

        // Viewport
        var viewport = new GameObject("Viewport");
        var viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewport.AddComponent<RectMask2D>();
        viewport.transform.SetParent(scrollView.transform, false);

        // Content container
        var content = new GameObject("Content");
        _content = content.AddComponent<RectTransform>();
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;
        _content.sizeDelta = new Vector2(0f, 0f);
        content.transform.SetParent(viewport.transform, false);

        scrollRect.viewport = viewportRect;
        scrollRect.content = _content;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 25f;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // Empty message
        var emptyObj = new GameObject("EmptyText");
        var emptyRect = emptyObj.AddComponent<RectTransform>();
        emptyRect.anchorMin = new Vector2(0.5f, 0.5f);
        emptyRect.anchorMax = new Vector2(0.5f, 0.5f);
        emptyRect.pivot = new Vector2(0.5f, 0.5f);
        emptyRect.sizeDelta = new Vector2(size.x - 60f, 80f);
        _emptyText = emptyObj.AddComponent<Text>();
        _emptyText.text = "No public lobbies found.\nClick Refresh to check again.";
        _emptyText.font = Resources.FontManager.UIFontRegular;
        _emptyText.fontSize = 16;
        _emptyText.alignment = TextAnchor.MiddleCenter;
        _emptyText.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        emptyObj.transform.SetParent(content.transform, false);

        // Bottom button area
        var buttonArea = new GameObject("ButtonArea");
        var buttonAreaRect = buttonArea.AddComponent<RectTransform>();
        buttonAreaRect.anchorMin = new Vector2(0f, 0f);
        buttonAreaRect.anchorMax = new Vector2(1f, 0f);
        buttonAreaRect.pivot = new Vector2(0.5f, 0f);
        buttonAreaRect.anchoredPosition = Vector2.zero;
        buttonAreaRect.sizeDelta = new Vector2(0f, ButtonAreaHeight);
        buttonArea.transform.SetParent(GameObject.transform, false);

        // Back button (left)
        CreateButton(buttonArea.transform, "BackButton", "← BACK", 
            new Vector2(0.02f, 0.12f), new Vector2(0.48f, 0.88f),
            new Color(0.15f, 0.15f, 0.18f, 1f), () => _onBack?.Invoke());

        // Refresh button (right)
        CreateButton(buttonArea.transform, "RefreshButton", "↻ REFRESH",
            new Vector2(0.52f, 0.12f), new Vector2(0.98f, 0.88f),
            new Color(0.15f, 0.4f, 0.25f, 1f), () => _onRefresh?.Invoke());

        _componentGroup = parent;
        _activeSelf = false;
        parent.AddComponent(this);
        GameObject.transform.SetParent(UiManager.UiGameObject!.transform, false);
        Object.DontDestroyOnLoad(GameObject);
        GameObject.SetActive(false);
    }

    private void CreateButton(Transform parent, string name, string text, 
        Vector2 anchorMin, Vector2 anchorMax, Color bgColor, Action onClick) {
        var btnObj = new GameObject(name);
        var btnRect = btnObj.AddComponent<RectTransform>();
        btnRect.anchorMin = anchorMin;
        btnRect.anchorMax = anchorMax;
        btnRect.offsetMin = Vector2.zero;
        btnRect.offsetMax = Vector2.zero;

        var btnImage = btnObj.AddComponent<Image>();
        btnImage.color = bgColor;

        var btnText = new GameObject("Text");
        var btnTextRect = btnText.AddComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = Vector2.zero;
        btnTextRect.offsetMax = Vector2.zero;
        var textComp = btnText.AddComponent<Text>();
        textComp.text = text;
        textComp.font = Resources.FontManager.UIFontRegular;
        textComp.fontSize = 16;
        textComp.alignment = TextAnchor.MiddleCenter;
        textComp.color = Color.white;
        btnText.transform.SetParent(btnObj.transform, false);

        var button = btnObj.AddComponent<Button>();
        button.onClick.AddListener(() => onClick());

        btnObj.transform.SetParent(parent, false);
        Object.DontDestroyOnLoad(btnObj);
    }

    public void SetOnLobbySelected(Action<PublicLobbyInfo> callback) => _onLobbySelected = callback;
    public void SetOnBack(Action callback) => _onBack = callback;
    public void SetOnRefresh(Action callback) => _onRefresh = callback;

    public void SetLobbies(List<PublicLobbyInfo>? lobbies) {
        foreach (var entry in _lobbyEntries) {
            Object.Destroy(entry);
        }
        _lobbyEntries.Clear();

        if (lobbies == null || lobbies.Count == 0) {
            _emptyText.gameObject.SetActive(true);
            _content.sizeDelta = new Vector2(0f, 100f);
            return;
        }

        _emptyText.gameObject.SetActive(false);

        var yPos = -5f;
        foreach (var lobby in lobbies) {
            var entry = CreateLobbyEntry(lobby, yPos);
            _lobbyEntries.Add(entry);
            yPos -= EntryHeight + EntrySpacing;
        }

        _content.sizeDelta = new Vector2(0f, -yPos + 10f);
    }

    private GameObject CreateLobbyEntry(PublicLobbyInfo lobby, float yPos) {
        var entry = new GameObject($"Lobby_{lobby.ConnectionData}");
        var rect = entry.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, yPos);
        rect.sizeDelta = new Vector2(0f, EntryHeight);

        // Row background
        var bg = entry.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.12f, 0.15f, 1f);

        // Lobby name (left, 50%)
        var nameObj = new GameObject("Name");
        var nameRect = nameObj.AddComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(0.5f, 1f);
        nameRect.offsetMin = new Vector2(15f, 0f);
        nameRect.offsetMax = Vector2.zero;
        var nameText = nameObj.AddComponent<Text>();
        nameText.text = lobby.Name;
        nameText.font = Resources.FontManager.UIFontRegular;
        nameText.fontSize = 16;
        nameText.alignment = TextAnchor.MiddleLeft;
        nameText.color = Color.white;
        nameObj.transform.SetParent(entry.transform, false);

        // Lobby type indicator (center)
        var typeObj = new GameObject("Type");
        var typeRect = typeObj.AddComponent<RectTransform>();
        typeRect.anchorMin = new Vector2(0.5f, 0f);
        typeRect.anchorMax = new Vector2(0.7f, 1f);
        typeRect.offsetMin = Vector2.zero;
        typeRect.offsetMax = Vector2.zero;
        var typeText = typeObj.AddComponent<Text>();
        typeText.text = lobby.LobbyType.ToUpper();
        typeText.font = Resources.FontManager.UIFontRegular;
        typeText.fontSize = 14;
        typeText.alignment = TextAnchor.MiddleCenter;
        typeText.color = lobby.LobbyType == "steam" ? new Color(0.4f, 0.7f, 1f, 1f) : new Color(1f, 0.85f, 0.6f, 1f);
        typeObj.transform.SetParent(entry.transform, false);

        // Join button (right)
        var btnObj = new GameObject("JoinButton");
        var btnRect = btnObj.AddComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.72f, 0.15f);
        btnRect.anchorMax = new Vector2(0.98f, 0.85f);
        btnRect.offsetMin = Vector2.zero;
        btnRect.offsetMax = Vector2.zero;
        var btnImage = btnObj.AddComponent<Image>();
        btnImage.color = new Color(0.2f, 0.5f, 0.3f, 1f);

        var btnText = new GameObject("Text");
        var btnTextRect = btnText.AddComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = Vector2.zero;
        btnTextRect.offsetMax = Vector2.zero;
        var btnTextComp = btnText.AddComponent<Text>();
        btnTextComp.text = "JOIN";
        btnTextComp.font = Resources.FontManager.UIFontRegular;
        btnTextComp.fontSize = 14;
        btnTextComp.alignment = TextAnchor.MiddleCenter;
        btnTextComp.color = Color.white;
        btnText.transform.SetParent(btnObj.transform, false);
        btnObj.transform.SetParent(entry.transform, false);

        var button = btnObj.AddComponent<Button>();
        var capturedLobby = lobby;
        button.onClick.AddListener(() => _onLobbySelected?.Invoke(capturedLobby));

        entry.transform.SetParent(_content.transform, false);
        Object.DontDestroyOnLoad(entry);
        return entry;
    }

    public void Show() => GameObject.SetActive(true);
    public void Hide() => GameObject.SetActive(false);
    public bool IsVisible => GameObject.activeSelf;

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
        var rectTransform = GameObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(position.x / 1920f, position.y / 1080f);
    }

    public Vector2 GetSize() => GameObject.GetComponent<RectTransform>().sizeDelta;
}

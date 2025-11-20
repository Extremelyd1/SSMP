using System;
using System.Collections.Generic;
using System.Text;
using GlobalEnums;
using SSMP.Api.Client;
using SSMP.Game.Settings;
using SSMP.Ui.Component;
using SSMP.Util;
using UnityEngine;
using UnityEngine.UI;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Ui.Chat;

/// <summary>
/// The message box in the bottom left of the screen that shows information related to SSMP.
/// </summary>
internal class ChatBox : IChatBox {
    private const int MaxMessages = 100;
    private const int MaxShownMessages = 10;
    private const int MaxShownMessagesWhenOpen = 20;
    private const float ChatWidth = 500f;
    private const float MessageHeight = 25f;
    private const float BoxInputMargin = 30f;
    private const float InputHeight = 30f;
    private const float InputMarginBottom = 20f;
    private const float MarginLeft = 25f;
    private const float TextMargin = 10f;
    private const int MaxWrapPasses = 200;

    public static Vector2 MessageSize { get; private set; }

    private static TextGenerationSettings _textGenSettings;

    private readonly ComponentGroup _chatBoxGroup;
    private readonly TextGenerator _textGenerator;
    private readonly ChatMessage?[] _messages;
    private readonly ChatInputComponent _chatInput;
    private bool _isOpen;
    private int _scrollOffset;

    public event Action<string>? ChatInputEvent;

    public ChatBox(ComponentGroup chatBoxGroup, ModSettings modSettings) {
        _chatBoxGroup = chatBoxGroup;
        _textGenerator = new TextGenerator();
        _messages = new ChatMessage[MaxMessages];

        _chatInput = CreateChatInput(chatBoxGroup);
        InitializeTextSettings();

        MonoBehaviourUtil.Instance.OnUpdateEvent += () => CheckKeyBinds(modSettings);
    }

    private ChatInputComponent CreateChatInput(ComponentGroup chatBoxGroup) {
        var input = new ChatInputComponent(
            chatBoxGroup,
            new Vector2(ChatWidth / 2f + MarginLeft, InputMarginBottom + InputHeight / 2f),
            new Vector2(ChatWidth, InputHeight),
            UiManager.ChatFontSize
        );
        input.SetActive(false);
        input.OnSubmit += OnChatSubmit;
        return input;
    }

    private void OnChatSubmit(string chatInput) {
        if (chatInput.Length > 0) {
            ChatInputEvent?.Invoke(chatInput);
        }

        HideChatInput();
    }

    private static void InitializeTextSettings() {
        MessageSize = new Vector2(ChatWidth + TextMargin, MessageHeight);
        _textGenSettings = new TextGenerationSettings {
            font = Resources.FontManager.UIFontRegular,
            color = Color.white,
            fontSize = UiManager.ChatFontSize,
            lineSpacing = 1,
            richText = true,
            scaleFactor = 1,
            fontStyle = FontStyle.Normal,
            textAnchor = TextAnchor.LowerLeft,
            alignByGeometry = false,
            resizeTextForBestFit = false,
            resizeTextMinSize = 10,
            resizeTextMaxSize = 40,
            updateBounds = false,
            verticalOverflow = VerticalWrapMode.Overflow,
            horizontalOverflow = HorizontalWrapMode.Wrap,
            generationExtents = MessageSize,
            pivot = new Vector2(0.5f, 0.5f),
            generateOutOfBounds = false
        };
    }

    private void CheckKeyBinds(ModSettings modSettings) {
        if (!_chatBoxGroup.IsActive()) return;

        if (_isOpen) {
            HandleOpenChatInput();
        } else if (modSettings.Keybinds.OpenChat.IsPressed && CanOpenChat()) {
            ShowChatInput();
        }
    }

    private void HandleOpenChatInput() {
        if (InputHandler.Instance.inputActions.Pause.IsPressed) {
            HideChatInput();
            return;
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0) {
            HandleScroll(scroll);
        }
    }

    private void HandleScroll(float scrollDelta) {
        int messageCount = CountMessages();
        int maxScroll = Mathf.Max(0, messageCount - MaxShownMessagesWhenOpen);

        if (maxScroll >= 0) {
            int oldOffset = _scrollOffset;
            _scrollOffset = Mathf.Clamp(_scrollOffset + (scrollDelta > 0 ? 1 : -1), 0, maxScroll);

            if (_scrollOffset != oldOffset) {
                UpdateMessageVisibility();
            }
        }
    }

    private int CountMessages() {
        int count = 0;
        for (int i = 0; i < MaxMessages; i++) {
            if (_messages[i] != null) count++;
        }

        return count;
    }

    private bool CanOpenChat() {
        if (!IsGameStateValid()) return false;
        if (IsHeroCharging()) return false;
        if (IsInventoryOpen()) return false;
        if (IsGodHomeMenuOpen()) return false;
        if (IsAnyInputFieldFocused()) return false;
        return true;
    }

    private static bool IsGameStateValid() {
        var gameManager = GameManager.instance;
        if (gameManager == null) return false;

        var validGameStates = gameManager.GameState == GameState.PLAYING ||
                              gameManager.GameState == GameState.MAIN_MENU;
        if (!validGameStates) return false;

        var uiManager = UIManager.instance;
        if (uiManager == null) return false;

        return uiManager.uiState == UIState.PLAYING ||
               uiManager.uiState == UIState.MAIN_MENU_HOME;
    }

    private static bool IsHeroCharging() {
        var hero = HeroController.instance;
        return hero != null && hero.cState.nailCharging;
    }

    private static bool IsAnyInputFieldFocused() {
        foreach (var selectable in Selectable.allSelectablesArray) {
            var inputField = selectable.gameObject.GetComponent<InputField>();
            if (inputField && inputField.isFocused) return true;
        }

        return false;
    }

    private void ShowChatInput() {
        _isOpen = true;
        _scrollOffset = 0;

        UpdateMessageVisibility();

        _chatInput.SetActive(true);
        _chatInput.Focus();

        InputHandler.Instance.StopMouseInput();
        InputHandler.Instance.PreventPause();
        SetEnabledHeroActions(false);
    }

    private void HideChatInput() {
        _isOpen = false;
        _scrollOffset = 0;

        for (var i = 0; i < MaxMessages; i++) 
            _messages[i]?.Hide();

        _chatInput.SetActive(false);
        
        InputHandler.Instance.EnableMouseInput();
        InputHandler.Instance.inputActions.Pause.ClearInputState();
        InputHandler.Instance.AllowPause();
        SetEnabledHeroActions(true);
    }

    private void UpdateMessageVisibility() {
        int messageCount = CountMessages();
        int visibleCount = _isOpen ? MaxShownMessagesWhenOpen : MaxShownMessages;
        int maxScroll = Mathf.Max(0, messageCount - visibleCount);

        _scrollOffset = Mathf.Clamp(_scrollOffset, 0, maxScroll);

        int displayPosition = 0;
        for (int i = 0; i < MaxMessages; i++) {
            var message = _messages[i];
            if (message == null) continue;

            bool isVisible = displayPosition >= _scrollOffset &&
                             displayPosition < _scrollOffset + visibleCount;

            if (isVisible) {
                int visualSlot = displayPosition - _scrollOffset;
                float yPos = InputMarginBottom + InputHeight + BoxInputMargin +
                             (visualSlot * MessageHeight);

                message.SetPosition(new Vector2(MessageSize.x / 2f + MarginLeft, yPos));
                message.OnChatToggle(_isOpen);
            } else {
                message.Hide();
            }

            displayPosition++;
        }
    }

    public void AddMessage(string messageText) {
        var remaining = messageText;

        for (int pass = 0; pass < MaxWrapPasses && !string.IsNullOrEmpty(remaining); pass++) {
            var result = WrapTextLine(remaining);

            if (result.wrapped) {
                remaining = result.remainder;
            } else {
                var sanitized = RemoveEmptyColorSpans(remaining);
                if (HasVisibleContent(sanitized)) {
                    AddTrimmedMessage(sanitized);
                }

                break;
            }
        }
    }

    private (bool wrapped, string remainder) WrapTextLine(string text) {
        int lastSpaceIndex = -1;

        for (int i = 0; i < text.Length; i++) {
            i = SkipHtmlTag(text, i);

            if (text[i] == ' ') {
                lastSpaceIndex = i;
            }

            var currentText = text.Substring(0, i + 1);
            var width = _textGenerator.GetPreferredWidth(
                StripRichTextTags(currentText),
                _textGenSettings
            );

            if (width > ChatWidth) {
                return SplitAndWrapLine(text, lastSpaceIndex, i);
            }
        }

        return (false, text);
    }

    private static int SkipHtmlTag(string text, int index) {
        if (text[index] == '<') {
            int closing = text.IndexOf('>', index + 1);
            if (closing != -1) {
                var tagContent = text.Substring(index + 1, closing - index - 1).Trim().ToLowerInvariant();
                // Only skip recognized Unity rich-text tags; otherwise treat '<' as a literal character
                if (IsTrackableTag(tagContent) || IsClosingTagForTrackable(tagContent)) {
                    return closing;
                }
            }
        }

        return index;
    }

    private (bool wrapped, string remainder) SplitAndWrapLine(string text, int lastSpace, int currentIndex) {
        int splitIndex = lastSpace != -1 ? lastSpace : currentIndex + 1;

        var firstPart = text.Substring(0, splitIndex);
        var openTags = GetUnclosedRichTextTags(firstPart);
        var firstComplete = firstPart + BuildClosingTags(openTags);

        var sanitized = RemoveEmptyColorSpans(firstComplete);
        if (HasVisibleContent(sanitized)) {
            AddTrimmedMessage(sanitized);
        }

        bool removedSpace = splitIndex == lastSpace && lastSpace != -1;
        int remainderStart = splitIndex + (removedSpace ? 1 : 0);

        var remainderTail = text.Substring(remainderStart);
        remainderTail = CleanRemainderText(remainderTail);

        bool startsWithColor = StartsWithColorAfterSkippablePrefix(remainderTail);
        var reopenTags = startsWithColor ? FilterOutColorTags(openTags) : openTags;
        var remainder = BuildOpeningTags(reopenTags) + remainderTail;
        remainder = RemoveEmptyColorSpans(remainder);

        // Prevent infinite loops
        if (StripRichTextTags(remainder).Length >= StripRichTextTags(text).Length) {
            if (HasVisibleContent(remainder)) {
                AddTrimmedMessage(remainder);
            }

            return (false, string.Empty);
        }

        return (true, remainder);
    }

    private static string CleanRemainderText(string text) {
        text = TrimLeadingClosingTags(text);
        text = TrimLeadingDanglingAngles(text);
        text = NormalizeLeadingColorOpens(text);
        return text;
    }

    private void AddTrimmedMessage(string messageText) {
        messageText = EnsureLeadingCharForRichText(messageText);
        if (!HasVisibleContent(messageText)) return;

        _messages[MaxMessages - 1]?.Destroy();
        Logger.Debug($"[ChatLine] {messageText}");

        ShiftMessagesUp();

        var newMessage = new ChatMessage(
            _chatBoxGroup,
            new Vector2(MessageSize.x / 2f + MarginLeft,
                InputMarginBottom + InputHeight + BoxInputMargin),
            messageText
        );
        newMessage.Display(_isOpen);
        _messages[0] = newMessage;

        _scrollOffset = 0;
        UpdateMessageVisibility();
    }

    private void ShiftMessagesUp() {
        for (int i = MaxMessages - 2; i >= 0; i--) {
            _messages[i + 1] = _messages[i];
        }

        _messages[0] = null;
    }

    #region Rich Text Tag Utilities

    private static List<string> GetUnclosedRichTextTags(string text) {
        var stack = new List<string>();

        for (int i = 0; i < text.Length; i++) {
            if (text[i] != '<') continue;

            int end = text.IndexOf('>', i + 1);
            if (end == -1) break;

            var tagContent = text.Substring(i + 1, end - i - 1).ToLowerInvariant();

            if (tagContent.StartsWith("/")) {
                CloseMatchingTag(stack, tagContent.Substring(1).Trim());
            } else if (IsTrackableTag(tagContent)) {
                stack.Add(text.Substring(i, end - i + 1));
            }

            i = end;
        }

        return stack;
    }

    private static bool IsTrackableTag(string tagContent) {
        return tagContent.StartsWith("color=") || tagContent == "b" || tagContent == "i";
    }

    private static bool IsClosingTagForTrackable(string tagContent) {
        if (!tagContent.StartsWith("/")) return false;
        var closeName = tagContent.Substring(1).Trim();
        return closeName.StartsWith("color") || closeName == "b" || closeName == "i";
    }

    private static void CloseMatchingTag(List<string> stack, string closeName) {
        for (int s = stack.Count - 1; s >= 0; s--) {
            if (IsMatching(stack[s], closeName)) {
                stack.RemoveAt(s);
                break;
            }
        }
    }

    private static bool IsMatching(string openTag, string closeName) {
        if (openTag.StartsWith("<color", StringComparison.OrdinalIgnoreCase))
            return closeName.StartsWith("color");
        if (openTag.Equals("<b>", StringComparison.OrdinalIgnoreCase))
            return closeName == "b";
        if (openTag.Equals("<i>", StringComparison.OrdinalIgnoreCase))
            return closeName == "i";
        return false;
    }

    private static string BuildClosingTags(List<string> openTags) {
        var sb = new StringBuilder();
        for (int i = openTags.Count - 1; i >= 0; i--) {
            if (openTags[i].StartsWith("<color", StringComparison.OrdinalIgnoreCase))
                sb.Append("</color>");
            else if (openTags[i].Equals("<b>", StringComparison.OrdinalIgnoreCase))
                sb.Append("</b>");
            else if (openTags[i].Equals("<i>", StringComparison.OrdinalIgnoreCase))
                sb.Append("</i>");
        }

        return sb.ToString();
    }

    private static string BuildOpeningTags(List<string> openTags) {
        var sb = new StringBuilder();
        foreach (var tag in openTags) {
            sb.Append(tag);
        }

        return sb.ToString();
    }

    private static List<string> FilterOutColorTags(List<string> openTags) {
        var filtered = new List<string>(openTags.Count);
        foreach (var tag in openTags) {
            if (!tag.StartsWith("<color", StringComparison.OrdinalIgnoreCase)) {
                filtered.Add(tag);
            }
        }

        return filtered;
    }

    private static string RemoveEmptyColorSpans(string text) {
        int searchFrom = 0;
        while (searchFrom < text.Length) {
            int open = text.IndexOf("<color", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (open == -1) break;

            int openEnd = text.IndexOf('>', open + 1);
            if (openEnd == -1) break;

            int close = text.IndexOf("</color>", openEnd + 1, StringComparison.OrdinalIgnoreCase);
            if (close == -1) break;

            var inner = text.Substring(openEnd + 1, close - openEnd - 1);

            if (string.IsNullOrWhiteSpace(inner)) {
                text = text.Remove(close, 8); // "</color>".Length
                text = text.Remove(open, openEnd - open + 1);
                searchFrom = open;
            } else {
                searchFrom = close + 8;
            }
        }

        return text;
    }

    private static string TrimLeadingClosingTags(string text) {
        int index = 0;
        while (index + 2 < text.Length && text[index] == '<' && text[index + 1] == '/') {
            int end = text.IndexOf('>', index + 2);
            if (end == -1) break;

            var name = text.Substring(index + 2, end - index - 2).Trim().ToLowerInvariant();
            if (name == "color" || name == "b" || name == "i") {
                index = end + 1;
            } else {
                break;
            }
        }

        return index > 0 ? text.Substring(index) : text;
    }

    private static string TrimLeadingDanglingAngles(string text) {
        int idx = 0;
        while (idx < text.Length && text[idx] == '>') idx++;
        return idx > 0 ? text.Substring(idx) : text;
    }

    private static string NormalizeLeadingColorOpens(string text) {
        int idx = 0;
        string lastOpen = null;

        while (idx < text.Length && text[idx] == '<') {
            int end = text.IndexOf('>', idx + 1);
            if (end == -1) break;

            var content = text.Substring(idx + 1, end - idx - 1).Trim().ToLowerInvariant();
            if (content.StartsWith("color=")) {
                lastOpen = text.Substring(idx, end - idx + 1);
                idx = end + 1;
            } else {
                break;
            }
        }

        return lastOpen != null ? lastOpen + text.Substring(idx) : text;
    }

    private static bool StartsWithColorAfterSkippablePrefix(string text) {
        int i = 0;
        while (i < text.Length) {
            char c = text[i];
            if (char.IsWhiteSpace(c) || c == '-' || c == ':' ||
                c == '•' || c == '–' || c == '—') {
                i++;
            } else {
                break;
            }
        }

        return i < text.Length &&
               text.IndexOf("<color", i, StringComparison.OrdinalIgnoreCase) == i;
    }

    private static string EnsureLeadingCharForRichText(string text) {
        if (string.IsNullOrEmpty(text) || text[0] != '<') return text;
        return "\u200B" + text; // Zero-width space
    }

    private static bool HasVisibleContent(string text) {
        return StripRichTextTags(text).Trim().Length > 0;
    }

    private static string StripRichTextTags(string text) {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (c == '<') {
                int end = text.IndexOf('>', i + 1);
                if (end != -1) {
                    var content = text.Substring(i + 1, end - i - 1).Trim().ToLowerInvariant();
                    // Skip only recognized rich-text tags; otherwise treat '<' as literal
                    if (IsTrackableTag(content) || IsClosingTagForTrackable(content)) {
                        i = end; // jump past closing '>'
                        continue;
                    }
                }

                sb.Append('<');
            } else {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    #endregion

    #region Game State Checks

    private static void SetEnabledHeroActions(bool enabled) {
        var inputHandler = InputHandler.Instance;
        if (inputHandler?.inputActions == null) return;

        var actions = inputHandler.inputActions;
        actions.Left.Enabled = enabled;
        actions.Right.Enabled = enabled;
        actions.Up.Enabled = enabled;
        actions.Down.Enabled = enabled;
        actions.MenuSubmit.Enabled = enabled;
        actions.MenuCancel.Enabled = enabled;
        actions.MenuExtra.enabled = enabled;
        actions.MenuSuper.enabled = enabled;
        actions.RsUp.Enabled = enabled;
        actions.RsDown.Enabled = enabled;
        actions.RsLeft.Enabled = enabled;
        actions.RsRight.Enabled = enabled;
        actions.Jump.Enabled = enabled;
        actions.Evade.Enabled = enabled;
        actions.Dash.Enabled = enabled;
        actions.SuperDash.Enabled = enabled;
        actions.DreamNail.Enabled = enabled;
        actions.Attack.Enabled = enabled;
        actions.Cast.Enabled = enabled;
        actions.QuickMap.Enabled = enabled;
        actions.QuickCast.Enabled = enabled;
        actions.Taunt.Enabled = enabled;
        actions.PaneRight.Enabled = enabled;
        actions.PaneLeft.Enabled = enabled;
        actions.OpenInventory.Enabled = enabled;
        actions.OpenInventoryMap.Enabled = enabled;
        actions.OpenInventoryJournal.Enabled = enabled;
        actions.OpenInventoryTools.Enabled = enabled;
        actions.OpenInventoryQuests.Enabled = enabled;
        actions.SwipeInventoryMap.Enabled = enabled;
        actions.SwipeInventoryJournal.Enabled = enabled;
        actions.SwipeInventoryTools.Enabled = enabled;
        actions.SwipeInventoryQuests.Enabled = enabled;
    }

    private static bool IsInventoryOpen() {
        var gameManager = GameManager.instance;
        if (gameManager == null) return false;

        var invFsm = gameManager.inventoryFSM;
        if (invFsm == null) return false;
        var stateName = invFsm.ActiveStateName;
        return stateName != "Closed" && stateName != "Can Open Inventory?";
    }

    private static bool IsGodHomeMenuOpen() {
        var bossChallengeUi = Object.FindObjectsByType<BossChallengeUI>(FindObjectsSortMode.None);
        var bossDoorChallengeUi = Object.FindObjectsByType<BossDoorChallengeUI>(FindObjectsSortMode.None);
        return bossChallengeUi.Length != 0 || bossDoorChallengeUi.Length != 0;
    }

    #endregion
}

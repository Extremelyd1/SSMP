using System;
using System.Collections;
using SSMP.Game.Server;
using SSMP.Ui.Component;
using SSMP.Util;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SSMP.Ui.Util;

/// <summary>
/// Helper methods for creating and managing UI components in the ConnectInterface.
/// </summary>
internal static class ConnectInterfaceHelpers {
    /// <summary>
    /// The time in seconds to hide the feedback text after it appeared.
    /// </summary>
    private const float FeedbackTextHideTime = 10f;

    /// <summary>
    /// The width of the glowing notch.
    /// </summary>
    private const float NotchWidth = 450f;

    /// <summary>
    /// The height of the glowing notch.
    /// </summary>
    private const float NotchHeight = 4f;

    /// <summary>
    /// The width of the background panel.
    /// </summary>
    private const float PanelWidth = 450f;

    /// <summary>
    /// The height of the background panel.
    /// </summary>
    private const float PanelHeight = 600f;

    /// <summary>
    /// The width of the border of the background panel.
    /// </summary>
    private const int PanelBorderWidth = 6;

    /// <summary>
    /// The corner radius for the background panel.
    /// </summary>
    private const int PanelCornerRadius = 20;

    /// <summary>
    /// Creates a glowing horizontal notch under the multiplayer header.
    /// </summary>
    /// <param name="x">The x position.</param>
    /// <param name="y">The y position.</param>
    /// <returns>The created notch GameObject.</returns>
    public static GameObject CreateGlowingNotch(float x, float y) {
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
        Object.DontDestroyOnLoad(notchObject);
        notchObject.SetActive(false);
        
        return notchObject;
    }

    /// <summary>
    /// Creates the background panel GameObject with rounded corners.
    /// </summary>
    /// <param name="x">The x position.</param>
    /// <param name="y">The y position.</param>
    /// <returns>The background panel GameObject.</returns>
    public static GameObject CreateBackgroundPanel(float x, float y) {
        var panel = new GameObject("MenuBackground");
        var rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(x / 1920f, y / 1080f);
        rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        rect.pivot = new Vector2(0.5f, 1f);
        
        var image = panel.AddComponent<UnityEngine.UI.Image>();
        image.color = Color.white;
        
        var roundedTexture = UiUtils.CreateRoundedRectTexture(
            (int) PanelWidth, 
            (int) PanelHeight,
            PanelBorderWidth,
            PanelCornerRadius
        );
        image.sprite = Sprite.Create(
            roundedTexture,
            new Rect(0, 0, roundedTexture.width, roundedTexture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(PanelBorderWidth, PanelBorderWidth, PanelBorderWidth, PanelBorderWidth)
        );
        image.type = UnityEngine.UI.Image.Type.Sliced;
        
        panel.transform.SetParent(UiManager.UiGameObject!.transform, false);
        panel.transform.SetAsFirstSibling();
        Object.DontDestroyOnLoad(panel);
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
    public static TabButtonComponent CreateTabButton(
        ComponentGroup group, 
        float x, 
        float y, 
        float width, 
        string text, 
        Action onPress
    ) {
        var button = new TabButtonComponent(group, new Vector2(x, y), new Vector2(width, 50f), 
            text, Resources.FontManager.UIFontRegular, 18);
        button.SetOnPress(onPress);
        return button;
    }

    /// <summary>
    /// Positions the tab buttons to fit within the background panel bounds.
    /// </summary>
    /// <param name="backgroundPanel">The background panel GameObject.</param>
    /// <param name="matchmakingTab">The matchmaking tab button.</param>
    /// <param name="steamTab">The steam tab button (optional).</param>
    /// <param name="directIpTab">The direct IP tab button.</param>
    public static void PositionTabButtonsFixed(
        GameObject backgroundPanel,
        TabButtonComponent matchmakingTab,
        TabButtonComponent? steamTab,
        TabButtonComponent directIpTab
    ) {
        var bgWidth = backgroundPanel.GetComponent<RectTransform>().sizeDelta.x;

        if (steamTab != null) {
            // 3 buttons
            var buttonWidth = (bgWidth - 2 * PanelBorderWidth) / 3f;
            AdjustButtonFixed(matchmakingTab.GameObject, -buttonWidth, buttonWidth);
            AdjustButtonFixed(steamTab.GameObject, 0f, buttonWidth);
            AdjustButtonFixed(directIpTab.GameObject, buttonWidth, buttonWidth);
        } else {
            // 2 buttons
            var buttonWidth = (bgWidth - 2 * PanelBorderWidth) / 2f;
            var offset = buttonWidth / 2f;
            AdjustButtonFixed(matchmakingTab.GameObject, -offset, buttonWidth);
            AdjustButtonFixed(directIpTab.GameObject, offset, buttonWidth);
        }
    }

    /// <summary>
    /// Adjusts a button's position and width.
    /// </summary>
    /// <param name="buttonGameObject">The button GameObject.</param>
    /// <param name="xPosition">The x position.</param>
    /// <param name="width">The width.</param>
    private static void AdjustButtonFixed(GameObject? buttonGameObject, float xPosition, float width) {
        if (buttonGameObject == null) return;
        var rectTransform = buttonGameObject.GetComponent<RectTransform>();
        if (rectTransform == null) return;
        rectTransform.sizeDelta = new Vector2(width, 50f);
        var position = rectTransform.localPosition;
        position.x = xPosition;
        rectTransform.localPosition = position;
    }

    /// <summary>
    /// Reparents a ComponentGroup to be a child of the background panel.
    /// </summary>
    /// <param name="group">The component group to reparent.</param>
    /// <param name="backgroundPanel">The background panel to parent to.</param>
    public static void ReparentComponentGroup(ComponentGroup group, GameObject backgroundPanel) {
        group.ReparentComponents(backgroundPanel);
    }

    /// <summary>
    /// Sets the feedback text with the given color and content, and starts the hide coroutine.
    /// </summary>
    /// <param name="feedbackText">The feedback text component.</param>
    /// <param name="color">The color of the text.</param>
    /// <param name="text">The content of the text.</param>
    /// <param name="currentCoroutine">The current hide coroutine (will be stopped if not null).</param>
    /// <returns>The new hide coroutine.</returns>
    public static Coroutine SetFeedbackText(
        ITextComponent feedbackText, 
        Color color, 
        string text, 
        Coroutine? currentCoroutine
    ) {
        feedbackText.SetColor(color);
        feedbackText.SetText(text);
        feedbackText.SetActive(true);
        
        if (currentCoroutine != null) {
            MonoBehaviourUtil.Instance.StopCoroutine(currentCoroutine);
        }
        return MonoBehaviourUtil.Instance.StartCoroutine(WaitHideFeedbackText(feedbackText));
    }

    /// <summary>
    /// Coroutine for hiding the feedback text after a delay.
    /// </summary>
    /// <param name="feedbackText">The feedback text component to hide.</param>
    /// <returns>An enumerator for the coroutine.</returns>
    private static IEnumerator WaitHideFeedbackText(ITextComponent feedbackText) {
        yield return new WaitForSeconds(FeedbackTextHideTime);
        feedbackText.SetActive(false);
    }

    /// <summary>
    /// Validates a username input.
    /// </summary>
    /// <param name="usernameInput">The username input component.</param>
    /// <param name="feedbackText">The feedback text component for displaying errors.</param>
    /// <param name="username">The validated username.</param>
    /// <param name="currentCoroutine">The current feedback hide coroutine.</param>
    /// <param name="newCoroutine">The new feedback hide coroutine (if validation fails).</param>
    /// <returns>True if the username is valid, false otherwise.</returns>
    public static bool ValidateUsername(IInputComponent usernameInput, ITextComponent feedbackText, 
        out string username, Coroutine? currentCoroutine, out Coroutine? newCoroutine) {
        newCoroutine = currentCoroutine;
        username = usernameInput.GetInput();
        
        if (username.Length == 0) {
            newCoroutine = SetFeedbackText(
                feedbackText, 
                Color.red, 
                "Failed to connect:\nYou must enter a username", 
                currentCoroutine
            );
            return false;
        }

        if (username.Length > ServerManager.MaxUsernameLength) {
            newCoroutine = SetFeedbackText(
                feedbackText, 
                Color.red, 
                "Failed to connect:\nUsername is too long", 
                currentCoroutine
            );
            return false;
        }
        
        return true;
    }

    /// <summary>
    /// Resets connect buttons to their default state.
    /// </summary>
    /// <param name="directConnectButton">The direct connect button.</param>
    /// <param name="lobbyConnectButton">The lobby connect button.</param>
    public static void ResetConnectButtons(IButtonComponent directConnectButton, IButtonComponent lobbyConnectButton) {
        directConnectButton.SetText(ConnectInterface.DirectConnectButtonText);
        directConnectButton.SetInteractable(true);
        lobbyConnectButton.SetText(ConnectInterface.DirectConnectButtonText);
        lobbyConnectButton.SetInteractable(true);
    }
}

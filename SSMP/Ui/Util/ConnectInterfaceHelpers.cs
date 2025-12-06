using System;
using System.Collections;
using SSMP.Game.Server;
using SSMP.Ui.Component;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
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
    private const float PanelHeight = 520f;

    /// <summary>
    /// The width of the border of the background panel.
    /// </summary>
    private const int PanelBorderWidth = 6;

    /// <summary>
    /// The corner radius for the background panel.
    /// </summary>
    private const int PanelCornerRadius = 20;

    /// <summary>
    /// Determines the current resolution tier based on aspect ratio and spacing multiplier.
    /// </summary>
    /// <param name="spacingMultiplier">The spacing multiplier (referenceHeight / currentHeight)</param>
    /// <returns>The detected resolution tier</returns>
    public static ResolutionTier GetResolutionTier(float spacingMultiplier) {
        float aspectRatio = (float) Screen.width / Screen.height;

        Logger.Debug($"[ResolutionDetection] {Screen.width}x{Screen.height}, " +
                     $"aspect={aspectRatio:F2}, spacingMult={spacingMultiplier:F3}");

        // Ultrawide: 21:9 = 2.33, 32:9 = 3.55
        // Range: 2.0+ catches all ultrawides including super ultrawides
        if (aspectRatio >= 2.0f) {
            return ResolutionTier.Ultrawide;
        }

        // Legacy aspect ratios: 4:3 = 1.33, 5:4 = 1.25, 16:10 = 1.6
        // Range: below 1.7 but above 1.0 catches these non-16:9 formats
        if (aspectRatio < 1.7f) {
            return ResolutionTier.Legacy;
        }

        // Standard 16:9 (aspect ratio ~1.777)
        // Now subdivide by height using spacing multiplier
        // Height-based tiers for 16:9:
        // 4K (2160p):  1080/2160 = 0.50
        // 1440p:       1080/1440 = 0.75
        // 1080p:       1080/1080 = 1.00

        if (spacingMultiplier < 0.6f) {
            // 4K and above (2160p+)
            return ResolutionTier.UHD4K;
        }

        if (spacingMultiplier < 0.85f) {
            // 1440p range
            return ResolutionTier.QHD1440p;
        }

        // 1080p and below (includes 900p, 768p, etc.)
        return ResolutionTier.Standard1080p;
    }

    /// <summary>
    /// Gets the button layout configuration for a specific resolution tier.
    /// </summary>
    /// <param name="tier">The resolution tier</param>
    /// <returns>The button layout configuration</returns>
    public static ButtonLayoutConfig GetLayoutConfig(ResolutionTier tier) {
        return tier switch {
            ResolutionTier.Ultrawide => new ButtonLayoutConfig(
                buttonGap: -25f,
                sideMargin: 23f
            ),

            ResolutionTier.UHD4K => new ButtonLayoutConfig(
                buttonGap: -80f,
                sideMargin: 47f,
                buttonHeightOverride: 65f
            ),

            ResolutionTier.QHD1440p => new ButtonLayoutConfig(
                buttonGap: -5f,
                sideMargin: 25f
            ),

            ResolutionTier.Legacy => new ButtonLayoutConfig(
                buttonGap: 120f,
                sideMargin: -45f
            ),

            ResolutionTier.Standard1080p => new ButtonLayoutConfig(
                buttonGap: 5f,
                sideMargin: 0f
            ),

            _ => new ButtonLayoutConfig(5f, 0f)
        };
    }

    /// <summary>
    /// Calculates button width and offset for split button layouts (e.g., Connect/Host buttons).
    /// </summary>
    /// <param name="contentWidth">The total content width available</param>
    /// <param name="spacingMultiplier">The spacing multiplier for resolution scaling</param>
    /// <returns>Tuple of (buttonWidth, buttonOffset)</returns>
    public static (float width, float offset) CalculateButtonLayout(float contentWidth, float spacingMultiplier) {
        var tier = GetResolutionTier(spacingMultiplier);
        var config = GetLayoutConfig(tier);

        Logger.Info($"[ButtonLayout] {tier} ({Screen.width}x{Screen.height}): " +
                    $"gap={config.ButtonGap:F1}px, margin={config.SideMargin:F1}px");

        // Calculate effective content width after margins
        var effectiveWidth = contentWidth - (config.SideMargin * 2f);

        // Calculate button dimensions
        // Formula: 2*buttonWidth + gap = effectiveWidth
        var buttonWidth = (effectiveWidth - config.ButtonGap) / 2f;

        // Calculate offset from center to button center
        var buttonOffset = effectiveWidth / 2f - buttonWidth / 2f;

        Logger.Info($"[ButtonLayout] effectiveWidth={effectiveWidth:F1}, " +
                    $"buttonWidth={buttonWidth:F1}, offset={buttonOffset:F1}");

        return (buttonWidth, buttonOffset);
    }

    /// <summary>
    /// Gets the button height for the current resolution, applying overrides when necessary.
    /// </summary>
    /// <param name="spacingMultiplier">The spacing multiplier for resolution scaling</param>
    /// <param name="defaultHeight">The default button height</param>
    /// <returns>The button height to use</returns>
    public static float GetButtonHeight(float spacingMultiplier, float defaultHeight) {
        var tier = GetResolutionTier(spacingMultiplier);
        var config = GetLayoutConfig(tier);
        return config.ButtonHeightOverride ?? defaultHeight;
    }

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
    public static GameObject CreateBackgroundPanel(float x, float y, float height = PanelHeight) {
        var panel = new GameObject("MenuBackground");
        var rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(x / 1920f, y / 1080f);
        rect.sizeDelta = new Vector2(PanelWidth, height);
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

/// <summary>
/// Resolution tier for UI layout calculations.
/// </summary>
public enum ResolutionTier {
    /// <summary>1920x1080 and below (16:9)</summary>
    Standard1080p,

    /// <summary>2560x1440 (16:9)</summary>
    QHD1440p,

    /// <summary>3840x2160 and above (16:9)</summary>
    UHD4K,

    /// <summary>21:9+ aspect ratio (2560x1080, 3440x1440, 5120x1440, etc.)</summary>
    Ultrawide,

    /// <summary>4:3, 5:4, or other non-standard aspect ratios</summary>
    Legacy
}

/// <summary>
/// Configuration for button layout at a specific resolution tier.
/// </summary>
public readonly struct ButtonLayoutConfig {
    /// <summary>Gap between buttons (negative = overlap, positive = separation)</summary>
    public float ButtonGap { get; }

    /// <summary>Margin from panel edges</summary>
    public float SideMargin { get; }

    /// <summary>Optional button height override</summary>
    public float? ButtonHeightOverride { get; }

    public ButtonLayoutConfig(float buttonGap, float sideMargin, float? buttonHeightOverride = null) {
        ButtonGap = buttonGap;
        SideMargin = sideMargin;
        ButtonHeightOverride = buttonHeightOverride;
    }
}

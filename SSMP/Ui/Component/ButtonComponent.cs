using System;
using SSMP.Ui.Resources;
using SSMP.Ui.Util;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SSMP.Ui.Component;

/// <inheritdoc cref="IButtonComponent" />
internal class ButtonComponent : Component, IButtonComponent {
    /// <summary>
    /// The default width of a button.
    /// </summary>
    private const float DefaultWidth = 240f;

    /// <summary>
    /// The default height of a button.
    /// </summary>
    private const float DefaultHeight = 38f;

    /// <summary>
    /// The background sprites.
    /// </summary>
    private readonly MultiStateSprite _bgSprite;

    /// <summary>
    /// The Unity Text component.
    /// </summary>
    private readonly Text _text;

    /// <summary>
    /// The Unity Image component.
    /// </summary>
    private readonly Image _image;

    /// <summary>
    /// The action that is executed when the button is pressed.
    /// </summary>
    private Action? _onPress;

    /// <summary>
    /// Whether the button is interactable (i.e. can be pressed).
    /// </summary>
    private bool _interactable;

    /// <summary>
    /// Whether the user is hovering over the button.
    /// </summary>
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private bool _isHover;

    /// <summary>
    /// Whether the user has their mouse down on the button.
    /// </summary>
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private bool _isMouseDown;

    /// <summary>
    /// The gradient shine overlay for hover/active states.
    /// </summary>
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly Image? _shineOverlay;

    public ButtonComponent(
        ComponentGroup componentGroup,
        Vector2 position,
        string text
    ) : this(
        componentGroup,
        position,
        new Vector2(DefaultWidth, DefaultHeight),
        text,
        TextureManager.ButtonBg,
        Resources.FontManager.UIFontRegular,
        UiManager.NormalFontSize) {
    }

    public ButtonComponent(
        ComponentGroup componentGroup,
        Vector2 position,
        Vector2 size,
        string text,
        MultiStateSprite bgSprite,
        Font font,
        int fontSize
    ) : this(
        componentGroup,
        position,
        size,
        text,
        bgSprite,
        font,
        Color.white,
        fontSize
    ) {
    }

    private ButtonComponent(
        ComponentGroup componentGroup,
        Vector2 position,
        Vector2 size,
        string text,
        MultiStateSprite bgSprite,
        Font font,
        Color textColor,
        int fontSize
    ) : base(componentGroup, position, size) {
        _bgSprite = bgSprite;
        _interactable = true;

        // Create background image with darker color
        _image = GameObject.AddComponent<Image>();
        _image.sprite = bgSprite.Neutral;
        _image.type = Image.Type.Sliced;
        _image.color = new Color(0.1f, 0.1f, 0.1f, 1f); // Very dark gray (#1a1a1a)
        
        // Add RectMask2D to contain all child elements within button bounds
        GameObject.AddComponent<RectMask2D>();

        // Create gradient shine overlay at the top - CONTAINED within button
        var shineObject = new GameObject("ShineOverlay");
        var shineRect = shineObject.AddComponent<RectTransform>();
        
        // Anchor to fill the entire button area
        shineRect.anchorMin = new Vector2(0f, 1f); // Top left
        shineRect.anchorMax = new Vector2(1f, 1f); // Top right
        shineRect.pivot = new Vector2(0.5f, 1f);
        shineRect.anchoredPosition = Vector2.zero;
        shineRect.sizeDelta = new Vector2(0f, 3f); // 3px tall stripe at top, 0 width offset means it stretches with anchors
        
        _shineOverlay = shineObject.AddComponent<Image>();
        
        // Shared horizontal gradient (bright center, fade to edges)
        _shineOverlay.sprite = UiUtils.HorizontalGradientSprite;
        _shineOverlay.color = new Color(1f, 0.7f, 0.3f, 0f); // Orange tint, initially invisible
        
        shineObject.transform.SetParent(GameObject.transform, false);

        // Create the text component in the button
        var textObject = CreateChildObject(size);
        _text = textObject.AddComponent<Text>();
        _text.text = text;
        _text.font = font;
        _text.fontSize = fontSize;
        _text.alignment = TextAnchor.MiddleCenter;
        _text.color = textColor;

        var eventTrigger = GameObject.AddComponent<EventTrigger>();
        _isMouseDown = false;
        _isHover = false;

        AddEventTrigger(eventTrigger, EventTriggerType.PointerEnter, _ => {
            _isHover = true;

            if (_interactable) {
                _image.sprite = bgSprite.Hover;
                // Add warm glow to button background
                _image.color = new Color(0.15f, 0.12f, 0.1f, 1f); // Slightly warmer/brighter
                // Show shine effect
                SetShineAlpha(0.5f);
            }
        });
        AddEventTrigger(eventTrigger, EventTriggerType.PointerExit, _ => {
            _isHover = false;
            if (_interactable && !_isMouseDown) {
                _image.sprite = bgSprite.Neutral;
                // Return to normal dark color
                _image.color = new Color(0.1f, 0.1f, 0.1f, 1f);
                // Hide shine effect
                SetShineAlpha(0f);
            }
        });
        AddEventTrigger(eventTrigger, EventTriggerType.PointerDown, _ => {
            _isMouseDown = true;

            if (_interactable) {
                _image.sprite = bgSprite.Active;
                // Even warmer/brighter on click
                _image.color = new Color(0.2f, 0.15f, 0.12f, 1f);
                // Brighter shine on click
                SetShineAlpha(0.7f);
            }
        });
        AddEventTrigger(eventTrigger, EventTriggerType.PointerUp, _ => {
            _isMouseDown = false;

            if (_interactable) {
                if (_isHover) {
                    _image.sprite = bgSprite.Hover;
                    _image.color = new Color(0.15f, 0.12f, 0.1f, 1f); // Back to hover glow
                    _onPress?.Invoke();
                    // Return to hover shine
                    SetShineAlpha(0.5f);
                } else {
                    _image.sprite = bgSprite.Neutral;
                    _image.color = new Color(0.1f, 0.1f, 0.1f, 1f); // Back to normal
                    // Hide shine
                    SetShineAlpha(0f);
                }
            }
        });
    }

    /// <inheritdoc />
    public void SetText(string text) {
        _text.text = text;
    }

    /// <inheritdoc />
    public void SetOnPress(Action action) {
        _onPress = action;
    }

    /// <inheritdoc />
    public void SetInteractable(bool interactable) {
        _interactable = interactable;

        var color = _text.color;
        color.a = ApplyInteractableSprite(_image, _bgSprite, interactable);
        _text.color = color;
    }

    /// <summary>
    /// Set the alpha of the shine overlay.
    /// </summary>
    /// <param name="alpha">The new alpha.</param>
    private void SetShineAlpha(float alpha) {
        if (_shineOverlay != null) {
            var color = _shineOverlay.color;
            color.a = alpha;
            _shineOverlay.color = color;
        }
    }
}

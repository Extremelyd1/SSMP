using System;
using SSMP.Ui.Util;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SSMP.Ui.Component;

/// <summary>
/// Tab button component with square corners and persistent glow when active.
/// </summary>
internal class TabButtonComponent : Component, IButtonComponent {
    /// <summary>
    /// The text component of the button.
    /// </summary>
    private readonly Text _text;

    /// <summary>
    /// The background image component.
    /// </summary>
    private readonly Image _image;

    /// <summary>
    /// The shine overlay image component.
    /// </summary>
    private readonly Image? _shineOverlay;

    /// <summary>
    /// The action to execute when the button is pressed.
    /// </summary>
    private Action? _onPress;

    /// <summary>
    /// Whether the button is interactable.
    /// </summary>
    private bool _interactable;

    /// <summary>
    /// Whether the pointer is currently hovering over the button.
    /// </summary>
    private bool _isHover;

    /// <summary>
    /// Whether the mouse button is currently pressed down on the button.
    /// </summary>
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private bool _isMouseDown;

    /// <summary>
    /// Whether the tab is currently active.
    /// </summary>
    private bool _isActive;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabButtonComponent"/> class.
    /// </summary>
    /// <param name="componentGroup">The component group this button belongs to.</param>
    /// <param name="position">The position of the button.</param>
    /// <param name="size">The size of the button.</param>
    /// <param name="text">The text displayed on the button.</param>
    /// <param name="font">The font of the text.</param>
    /// <param name="fontSize">The font size of the text.</param>
    public TabButtonComponent(
        ComponentGroup componentGroup,
        Vector2 position,
        Vector2 size,
        string text,
        Font font,
        int fontSize
    ) : base(componentGroup, position, size) {
        _interactable = true;
        _isActive = false;

        // Create background image - square with solid color (no sprite)
        _image = GameObject.AddComponent<Image>();
        _image.sprite = null; // No sprite, just solid color
        _image.type = Image.Type.Simple;
        _image.color = new Color(0.05f, 0.05f, 0.05f, 1f); // Darker when inactive

        // Create gradient shine overlay at the top - CONTAINED within button
        var shineObject = new GameObject("ShineOverlay");
        var shineRect = shineObject.AddComponent<RectTransform>();
        
        shineRect.anchorMin = new Vector2(0f, 1f);
        shineRect.anchorMax = new Vector2(1f, 1f);
        shineRect.pivot = new Vector2(0.5f, 1f);
        shineRect.anchoredPosition = Vector2.zero;
        shineRect.sizeDelta = new Vector2(0f, 3f);
        
        _shineOverlay = shineObject.AddComponent<Image>();
        
        var gradientTexture = UiUtils.CreateHorizontalGradientTexture(256, 1);
        var gradientSprite = Sprite.Create(
            gradientTexture,
            new Rect(0, 0, 256, 1),
            new Vector2(0.5f, 0.5f)
        );
        _shineOverlay.sprite = gradientSprite;
        _shineOverlay.color = new Color(1f, 0.7f, 0.3f, 0f); // Initially invisible
        
        shineObject.transform.SetParent(GameObject.transform, false);
        Object.DontDestroyOnLoad(shineObject);

        // Create the text component
        var textObject = new GameObject();
        textObject.AddComponent<RectTransform>().sizeDelta = size;
        _text = textObject.AddComponent<Text>();
        _text.text = text;
        _text.font = font;
        _text.fontSize = fontSize;
        _text.alignment = TextAnchor.MiddleCenter;
        _text.color = Color.white;

        textObject.transform.SetParent(GameObject.transform, false);
        Object.DontDestroyOnLoad(textObject);

        var eventTrigger = GameObject.AddComponent<EventTrigger>();
        _isMouseDown = false;
        _isHover = false;

        AddEventTrigger(eventTrigger, EventTriggerType.PointerEnter, _ => {
            _isHover = true;
            if (_interactable && !_isActive) {
                _image.color = new Color(0.12f, 0.1f, 0.08f, 1f);
                if (_shineOverlay != null) {
                    var color = _shineOverlay.color;
                    color.a = 0.4f;
                    _shineOverlay.color = color;
                }
            }
        });

        AddEventTrigger(eventTrigger, EventTriggerType.PointerExit, _ => {
            _isHover = false;
            if (_interactable && !_isMouseDown && !_isActive) {
                _image.color = new Color(0.05f, 0.05f, 0.05f, 1f);
                if (_shineOverlay != null) {
                    var color = _shineOverlay.color;
                    color.a = 0f;
                    _shineOverlay.color = color;
                }
            }
        });

        AddEventTrigger(eventTrigger, EventTriggerType.PointerDown, _ => {
            _isMouseDown = true;
            if (_interactable) {
                _image.color = new Color(0.15f, 0.12f, 0.1f, 1f);
                if (_shineOverlay != null) {
                    var color = _shineOverlay.color;
                    color.a = 0.6f;
                    _shineOverlay.color = color;
                }
            }
        });

        AddEventTrigger(eventTrigger, EventTriggerType.PointerUp, _ => {
            _isMouseDown = false;
            if (_interactable) {
                _onPress?.Invoke();
                // Don't change appearance here - let SetTabActive handle it
            }
        });
    }

    /// <summary>
    /// Sets whether this tab is active (selected).
    /// </summary>
    /// <param name="active">Whether the tab is active.</param>
    public void SetTabActive(bool active) {
        _isActive = active;
        
        if (active) {
            // Active tab - darker background with persistent glow
            if (_image != null) _image.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            if (_text != null) _text.color = new Color(1.0f, 0.7f, 0.3f, 1f); // Orange
            if (_shineOverlay != null) {
                var color = _shineOverlay.color;
                color.a = 0.6f; // Persistent glow
                _shineOverlay.color = color;
            }
        } else {
            // Inactive tab
            if (_image != null) _image.color = new Color(0.05f, 0.05f, 0.05f, 1f);
            if (_text != null) _text.color = Color.white;
            if (_shineOverlay != null && !_isHover) {
                var color = _shineOverlay.color;
                color.a = 0f;
                _shineOverlay.color = color;
            }
        }
    }

    /// <summary>
    /// Sets the text of the button.
    /// </summary>
    /// <param name="text">The new text.</param>
    public void SetText(string text) {
        _text.text = text;
    }

    /// <summary>
    /// Sets the action to be executed when the button is pressed.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    public void SetOnPress(Action action) {
        _onPress = action;
    }

    /// <summary>
    /// Sets whether the button is interactable.
    /// </summary>
    /// <param name="interactable">True if the button should be interactable, false otherwise.</param>
    public void SetInteractable(bool interactable) {
        _interactable = interactable;
        var color = _text.color;
        color.a = interactable ? 1f : NotInteractableOpacity;
        _text.color = color;
    }
}

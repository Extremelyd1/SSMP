using System.Collections;
using SSMP.Ui.Component;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Ui.Chat;

/// <summary>
/// Manages a single message in the chat display with fade animations.
/// </summary>
internal class ChatMessage {
    /// <summary>
    /// How long a message stays at full opacity before fading.
    /// </summary>
    private const float MessageStayTime = 7.5f;

    /// <summary>
    /// Duration of the fade-out animation.
    /// </summary>
    private const float MessageFadeTime = 1f;

    /// <summary>
    /// The text component belonging to this chat message.
    /// </summary>
    private readonly TextComponent _textComponent;

    /// <summary>
    /// The current coroutine responsible for fading out the message after a delay.
    /// </summary>
    private Coroutine? _fadeCoroutine;

    /// <summary>
    /// The current alpha of the message.
    /// </summary>
    private float _alpha = 1f;

    /// <summary>
    /// Whether this message is already completely faded out.
    /// </summary>
    private bool _isFadedOut;

    /// <summary>
    /// Whether the chat is currently open.
    /// </summary>
    private bool _chatOpen;

    /// <summary>
    /// Creates a new chat message at the specified position.
    /// </summary>
    /// <param name="componentGroup">The UI component group this message belongs to.</param>
    /// <param name="position">The screen position of the message.</param>
    /// <param name="text">The message text content (supports Unity rich text).</param>
    public ChatMessage(ComponentGroup componentGroup, Vector2 position, string text) {
        _textComponent = new TextComponent(
            componentGroup,
            position,
            ChatBox.MessageSize,
            text,
            UiManager.ChatFontSize,
            alignment: TextAnchor.LowerLeft
        );
        _textComponent.SetActive(false);
    }

    /// <summary>
    /// Displays the message and starts its fade timer.
    /// </summary>
    /// <param name="chatOpen">Whether the chat is currently open.</param>
    public void Display(bool chatOpen) {
        _chatOpen = chatOpen;
        _textComponent.SetActive(true);
        StartFadeRoutine();
    }

    /// <summary>
    /// Hides the message and stops its fade animation.
    /// </summary>
    public void Hide() {
        _isFadedOut = true;
        SetAlpha(1f);
        _textComponent.SetActive(false);
        StopFadeRoutine();
    }

    /// <summary>
    /// Called when the chat is opened or closed.
    /// </summary>
    /// <param name="chatOpen">Whether the chat is now open.</param>
    public void OnChatToggle(bool chatOpen) {
        _chatOpen = chatOpen;

        if (chatOpen) {
            // Show message at full opacity when chat opens and pause fade
            ShowAtFullOpacity();
        } else {
            if (_isFadedOut) {
                // Keep hidden if already faded out
                _textComponent.SetActive(false);
            } else {
                // Resume fading when chat closes
                StartFadeRoutine();
            }
        }
    }

    /// <summary>
    /// Moves the message by the specified offset.
    /// </summary>
    /// <param name="offset">The amount to move in each direction.</param>
    public void Move(Vector2 offset) {
        _textComponent.SetPosition(_textComponent.GetPosition() + offset);
    }

    /// <summary>
    /// Sets the absolute position of the message.
    /// </summary>
    /// <param name="position">The new screen position.</param>
    public void SetPosition(Vector2 position) {
        _textComponent.SetPosition(position);
    }

    /// <summary>
    /// Destroys the message and its underlying UI component.
    /// </summary>
    public void Destroy() {
        StopFadeRoutine();
        _textComponent.Destroy();
    }

    /// <summary>
    /// Resets the message to full opacity and stops any fade animation.
    /// </summary>
    private void ShowAtFullOpacity() {
        StopFadeRoutine();

        if (_isFadedOut) {
            _textComponent.SetActive(true);
            _isFadedOut = false;
        }

        SetAlpha(1f);
    }

    /// <summary>
    /// Starts the fade-out coroutine for this message.
    /// </summary>
    private void StartFadeRoutine() {
        // Ensure only one fade coroutine is running
        StopFadeRoutine();
        _fadeCoroutine = MonoBehaviourUtil.Instance.StartCoroutine(FadeRoutine());
    }

    /// <summary>
    /// Stops the active fade-out coroutine if running.
    /// </summary>
    private void StopFadeRoutine() {
        if (_fadeCoroutine != null) {
            MonoBehaviourUtil.Instance.StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }
    }

    /// <summary>
    /// Sets the alpha transparency of the message text.
    /// </summary>
    /// <param name="alpha">Alpha value from 0 (transparent) to 1 (opaque).</param>
    private void SetAlpha(float alpha) {
        _alpha = Mathf.Clamp01(alpha);
        var color = _textComponent.GetColor();
        _textComponent.SetColor(new Color(color.r, color.g, color.b, _alpha));
    }

    /// <summary>
    /// Coroutine that waits, then gradually fades out the message.
    /// Fade is paused when chat is open.
    /// </summary>
    private IEnumerator FadeRoutine() {
        // Wait at full opacity, pausing while chat is open
        var waitElapsed = 0f;
        while (waitElapsed < MessageStayTime) {
            if (!_chatOpen) {
                waitElapsed += Time.deltaTime;
            }

            yield return null;
        }

        // Gradually fade out, pausing while chat is open
        var elapsed = 0f;
        while (elapsed < MessageFadeTime) {
            if (!_chatOpen) {
                elapsed += Time.deltaTime;
                SetAlpha(1f - (elapsed / MessageFadeTime));
            }

            yield return null;
        }

        _fadeCoroutine = null;
        Hide();
    }
}

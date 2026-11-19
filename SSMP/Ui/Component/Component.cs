using System;
using SSMP.Ui.Resources;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SSMP.Ui.Component;

/// <inheritdoc />
internal abstract class Component : IComponent {
    /// <summary>
    /// The opacity of non-interactable components.
    /// </summary>
    protected const float NotInteractableOpacity = 0.5f;

    /// <summary>
    /// The underlying GameObject of the component. 
    /// </summary>
    internal GameObject GameObject { get; }

    /// <summary>
    /// The Unity RectTransform instance.
    /// </summary>
    private readonly RectTransform _transform;

    /// <summary>
    /// Whether this component is active.
    /// </summary>
    private bool _activeSelf;

    /// <summary>
    /// The component group this component belongs to.
    /// </summary>
    private readonly ComponentGroup _componentGroup;

    protected Component(ComponentGroup componentGroup, Vector2 position, Vector2 size) {
        // Create a gameobject with the CanvasRenderer component, so we can render as GUI
        GameObject = new GameObject();
        GameObject.AddComponent<CanvasRenderer>();
        // Make sure game object persists
        Object.DontDestroyOnLoad(GameObject);

        // Create a RectTransform with the desired size
        _transform = GameObject.AddComponent<RectTransform>();

        position = new Vector2(
            position.x / 1920f,
            position.y / 1080f
        );
        _transform.anchorMin = _transform.anchorMax = position;

        _transform.sizeDelta = size;

        GameObject.transform.SetParent(UiManager.UiGameObject!.transform, false);

        _activeSelf = true;

        _componentGroup = componentGroup;
        componentGroup.AddComponent(this);
    }

    /// <inheritdoc />
    public virtual void SetGroupActive(bool groupActive) {
        // TODO: figure out why this could be happening
        if (GameObject == null) {
            // Logger.Info(
            //     $"The GameObject belonging to this component (type: {GetType()}) is null, this shouldn't happen");
            return;
        }

        GameObject.SetActive(_activeSelf && groupActive);
    }

    /// <inheritdoc />
    public virtual void SetActive(bool active) {
        _activeSelf = active;

        GameObject.SetActive(_activeSelf && _componentGroup.IsActive());
    }

    /// <inheritdoc />
    public Vector2 GetPosition() {
        var position = _transform.anchorMin;
        return new Vector2(
            position.x * 1920f,
            position.y * 1080f
        );
    }

    /// <inheritdoc />
    public void SetPosition(Vector2 position) {
        _transform.anchorMin = _transform.anchorMax = new Vector2(
            position.x / 1920f,
            position.y / 1080f
        );
    }

    /// <inheritdoc />
    public Vector2 GetSize() {
        return _transform.sizeDelta;
    }

    /// <summary>
    /// Destroys the component.
    /// </summary>
    public void Destroy() {
        Object.Destroy(GameObject);
    }

    /// <summary>
    /// Create a child object with a RectTransform of the given size, parented to this component's GameObject.
    /// </summary>
    /// <param name="size">The size of the child's RectTransform.</param>
    /// <returns>The created child object.</returns>
    protected GameObject CreateChildObject(Vector2 size) {
        var child = new GameObject();
        child.AddComponent<RectTransform>().sizeDelta = size;
        child.transform.SetParent(GameObject.transform, false);
        return child;
    }

    /// <summary>
    /// Set the sprite of the given image to the neutral or disabled sprite based on interactability.
    /// </summary>
    /// <param name="image">The background image.</param>
    /// <param name="sprites">The background sprites.</param>
    /// <param name="interactable">Whether the component is interactable.</param>
    /// <returns>The alpha that foreground graphics should use for the given interactability.</returns>
    protected static float ApplyInteractableSprite(Image image, MultiStateSprite sprites, bool interactable) {
        image.sprite = interactable ? sprites.Neutral : sprites.Disabled;
        return interactable ? 1f : NotInteractableOpacity;
    }

    /// <summary>
    /// Add an event trigger to this component object.
    /// </summary>
    /// <param name="eventTrigger">The event trigger.</param>
    /// <param name="type">The type of the event trigger.</param>
    /// <param name="action">The action that is executed on the event.</param>
    protected void AddEventTrigger(
        EventTrigger eventTrigger,
        EventTriggerType type,
        Action<BaseEventData> action
    ) {
        var eventTriggerEntry = new EventTrigger.Entry {
            eventID = type
        };
        eventTriggerEntry.callback.AddListener(action.Invoke);

        eventTrigger.triggers.Add(eventTriggerEntry);
    }
}

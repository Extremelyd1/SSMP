using UnityEngine;

namespace SSMP.Ui.Component;

/// <summary>
/// Base class for overlay panels that own a root GameObject and register with a parent component group.
/// </summary>
internal abstract class PanelComponent : IComponent {
    /// <summary>The root GameObject for this panel.</summary>
    protected GameObject GameObject { get; }

    /// <summary>Tracks the panel's own active state.</summary>
    protected bool ActiveSelf;

    /// <summary>Parent component group for visibility management.</summary>
    private readonly ComponentGroup _componentGroup;

    /// <summary>
    /// Creates the root GameObject, anchored at the given position with a top-center pivot.
    /// </summary>
    /// <param name="name">The name of the root GameObject.</param>
    /// <param name="parent">Parent component group.</param>
    /// <param name="position">The position of the panel.</param>
    /// <param name="size">The size of the panel.</param>
    protected PanelComponent(string name, ComponentGroup parent, Vector2 position, Vector2 size) {
        _componentGroup = parent;

        GameObject = new GameObject(name);
        var rect = GameObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(position.x / 1920f, position.y / 1080f);
        rect.sizeDelta = size;
        rect.pivot = new Vector2(0.5f, 1f);
    }

    /// <summary>
    /// Registers the panel with its component group and parents it (inactive) to the UI root.
    /// Call once all children have been created.
    /// </summary>
    protected void Register() {
        _componentGroup.AddComponent(this);
        GameObject.transform.SetParent(UiManager.UiGameObject!.transform, false);
        GameObject.SetActive(false);
    }

    /// <summary>
    /// Shows the panel.
    /// </summary>
    public abstract void Show();

    /// <summary>
    /// Hides the panel.
    /// </summary>
    public abstract void Hide();

    /// <inheritdoc />
    public void SetGroupActive(bool groupActive) {
        if (GameObject == null) return;
        GameObject.SetActive(ActiveSelf && groupActive);
    }

    /// <inheritdoc />
    public void SetActive(bool active) {
        ActiveSelf = active;
        GameObject.SetActive(ActiveSelf && _componentGroup.IsActive());
    }

    /// <inheritdoc />
    public Vector2 GetPosition() {
        var position = GameObject.GetComponent<RectTransform>().anchorMin;
        return new Vector2(position.x * 1920f, position.y * 1080f);
    }

    /// <inheritdoc />
    public void SetPosition(Vector2 position) {
        var rect = GameObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(position.x / 1920f, position.y / 1080f);
    }

    /// <inheritdoc />
    public Vector2 GetSize() => GameObject.GetComponent<RectTransform>().sizeDelta;
}

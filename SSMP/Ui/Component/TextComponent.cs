using SSMP.Ui.Resources;
using UnityEngine;
using UnityEngine.UI;

namespace SSMP.Ui.Component;

/// <inheritdoc cref="ITextComponent" />
internal class TextComponent : Component, ITextComponent {
    /// <summary>
    /// The Unity Text component.
    /// </summary>
    private readonly Text _textObject;

    /// <summary>
    /// Initial text value; current text is stored on the underlying Text component.
    /// </summary>
    private readonly string _text;

    public TextComponent(
        ComponentGroup componentGroup,
        Vector2 position,
        Vector2 size,
        string text,
        int fontSize,
        FontStyle fontStyle = FontStyle.Normal,
        TextAnchor alignment = TextAnchor.MiddleCenter
    ) : this(componentGroup, position, size, new Vector2(0.5f, 0.5f), text, fontSize, fontStyle, alignment) {
    }

    public TextComponent(
        ComponentGroup componentGroup,
        Vector2 position,
        Vector2 size,
        Vector2 pivot,
        string text,
        int fontSize,
        FontStyle fontStyle = FontStyle.Normal,
        TextAnchor alignment = TextAnchor.MiddleCenter
    ) : base(componentGroup, position, size) {
        _text = text;
        _textObject = CreateTextObject(text, fontSize, fontStyle, alignment, pivot);
        AddSizeFitter();
        AddOutline();
    }

    public void SetText(string text) {
        _textObject.text = text;
    }

    public void SetColor(Color color) {
        _textObject.color = color;
    }

    public Color GetColor() {
        return _textObject.color;
    }
    
    /// <summary>
    /// Gets the preferred width required to render the component's text without wrapping.
    /// </summary>
    /// <returns>The preferred width in pixels.</returns>
    /// <remarks>
    /// <para>
    /// <strong>NOTE:</strong> This method is currently unused by the ChatBox implementation.
    /// ChatBox performs dynamic text measurement on arbitrary strings during its wrapping algorithm,
    /// which requires direct use of <see cref="TextGenerator"/> with custom settings rather than
    /// querying an existing component's preferred width.
    /// </para>
    /// <para>
    /// This method is retained for potential future use cases where measuring the preferred width
    /// of an already-instantiated text component may be needed, such as:
    /// <list type="bullet">
    /// <item><description>Dynamic UI layout systems</description></item>
    /// <item><description>Tooltip sizing</description></item>
    /// <item><description>Button auto-sizing based on text content</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public float GetPreferredWidth()
    {
        var textGen = new TextGenerator();
        var settings = _textObject.GetGenerationSettings(_textObject.rectTransform.rect.size);
        return textGen.GetPreferredWidth(_textObject.text, settings);
    }

    private Text CreateTextObject(string text, int fontSize, FontStyle fontStyle, TextAnchor alignment, Vector2 pivot) {
        var textObj = GameObject.AddComponent<Text>();
        
        textObj.supportRichText = true;
        textObj.text = text;
        textObj.font = Resources.FontManager.UIFontRegular;
        textObj.fontSize = fontSize;
        textObj.fontStyle = fontStyle;
        textObj.alignment = alignment;
        textObj.horizontalOverflow = HorizontalWrapMode.Overflow;
        textObj.verticalOverflow = VerticalWrapMode.Overflow;
        textObj.rectTransform.pivot = pivot;
        textObj.raycastTarget = false; // do not block input
        
        return textObj;
    }

    private void AddSizeFitter() {
        var sizeFitter = GameObject.AddComponent<ContentSizeFitter>();
        sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void AddOutline() {
        var outline = GameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace SSMP.Ui.Component;

/// <summary>
/// Input component specifically for the IP input.
/// </summary>
internal class IpInputComponent : HiddenInputComponent {
    /// <summary>
    /// List of characters that cannot be input in this field.
    /// </summary>
    private static readonly List<char> BlacklistedChars = [
        ' ',
        '\n',
        '\t',
        '\v',
        '\f',
        '\b',
        '\r'
    ];

    public IpInputComponent(
        ComponentGroup componentGroup,
        Vector2 position,
        string defaultValue,
        string placeholderText
    ) : this(
        componentGroup,
        position,
        new Vector2(DefaultWidth, DefaultHeight),
        defaultValue,
        placeholderText
    ) {
    }

    public IpInputComponent(
        ComponentGroup componentGroup,
        Vector2 position,
        Vector2 size,
        string defaultValue,
        string placeholderText
    ) : base(
        componentGroup,
        position,
        size,
        defaultValue,
        placeholderText,
        UiManager.NormalFontSize
    ) {
        InputField.onValidateInput += (_, _, addedChar) => {
            if (BlacklistedChars.Contains(addedChar)) {
                return '\0';
            }

            return addedChar;
        };
    }
}

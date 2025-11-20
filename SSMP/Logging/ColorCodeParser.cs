using System.Collections.Generic;
using System.Text;

namespace SSMP.Logging;

/// <summary>
/// Provides parsing utilities for Minecraft-style ampersand color codes and formatting.
/// </summary>
/// <remarks>
/// <para>Supported color codes: &amp;0 through &amp;9 (digits) and &amp;a through &amp;f (hex).</para>
/// <para>Supported style codes: &amp;l (bold), &amp;o (italic), &amp;m (strikethrough), &amp;n (underline).</para>
/// <para>Special codes: &amp;r (reset all formatting), &amp;&amp; (escape sequence for literal ampersand).</para>
/// <para>
/// This parser supports three output formats:
/// <list type="bullet">
/// <item><description>ANSI escape sequences for console output</description></item>
/// <item><description>Unity rich text tags for UI rendering</description></item>
/// <item><description>Plain text with all codes stripped</description></item>
/// </list>
/// </para>
/// </remarks>
public static class ColorCodeParser {
    /// <summary>
    /// Maps Minecraft color/style codes to their corresponding ANSI escape sequences.
    /// </summary>
    private static readonly Dictionary<char, string> AnsiCodes = new() {
        // Color codes (0-9, a-f)
        { '0', "\x1b[97m" }, // Black → Bright White (legacy mapping for visibility)
        { '1', "\x1b[34m" }, // Dark Blue
        { '2', "\x1b[32m" }, // Dark Green
        { '3', "\x1b[36m" }, // Dark Aqua
        { '4', "\x1b[31m" }, // Dark Red
        { '5', "\x1b[35m" }, // Dark Purple
        { '6', "\x1b[33m" }, // Gold
        { '7', "\x1b[37m" }, // Gray
        { '8', "\x1b[90m" }, // Dark Gray
        { '9', "\x1b[94m" }, // Blue
        { 'a', "\x1b[92m" }, // Green
        { 'b', "\x1b[96m" }, // Aqua
        { 'c', "\x1b[91m" }, // Red
        { 'd', "\x1b[95m" }, // Light Purple
        { 'e', "\x1b[93m" }, // Yellow
        { 'f', "\x1b[30m" }, // White → Black (legacy mapping)

        // Style codes
        { 'l', "\x1b[1m" }, // Bold
        { 'm', "\x1b[9m" }, // Strikethrough
        { 'n', "\x1b[4m" }, // Underline
        { 'o', "\x1b[3m" }, // Italic

        // Control codes
        { 'r', "\x1b[0m" } // Reset
    };

    /// <summary>
    /// Maps Minecraft color codes to their corresponding hex color values for Unity.
    /// </summary>
    private static readonly Dictionary<char, string> UnityHexColors = new() {
        { '0', "000000" }, { '1', "0000AA" }, { '2', "00AA00" }, { '3', "00AAAA" },
        { '4', "AA0000" }, { '5', "AA00AA" }, { '6', "FFAA00" }, { '7', "AAAAAA" },
        { '8', "555555" }, { '9', "5555FF" }, { 'a', "55FF55" }, { 'b', "55FFFF" },
        { 'c', "FF5555" }, { 'd', "FF55FF" }, { 'e', "FFFF55" }, { 'f', "FFFFFF" }
    };

    /// <summary>
    /// Converts Minecraft color codes to ANSI escape sequences for console output.
    /// </summary>
    /// <param name="message">The input string containing Minecraft color codes.</param>
    /// <returns>A string with color codes replaced by ANSI escape sequences.</returns>
    /// <remarks>
    /// Unrecognized codes are left as-is. Use &amp;&amp; to output a literal ampersand.
    /// </remarks>
    public static string ParseToAnsi(string message) {
        if (string.IsNullOrEmpty(message))
            return message;

        var sb = new StringBuilder(message.Length);

        for (int i = 0; i < message.Length; i++) {
            char ch = message[i];

            if (ch != '&') {
                sb.Append(ch);
                continue;
            }

            // Check if there's a character after '&'
            if (i + 1 >= message.Length) {
                sb.Append('&');
                continue;
            }

            char next = message[i + 1];

            // Handle escaped ampersand (&&)
            if (next == '&') {
                sb.Append('&');
                i++;
                continue;
            }

            // Try to parse as a color/style code
            char code = char.ToLower(next);
            if (AnsiCodes.TryGetValue(code, out var ansi)) {
                sb.Append(ansi);
                i++; // Skip the code character
            } else {
                // Unknown code, keep the ampersand
                sb.Append('&');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts Minecraft color codes to Unity rich text tags.
    /// </summary>
    /// <param name="message">The input string containing Minecraft color codes.</param>
    /// <returns>A string with color codes replaced by Unity rich text tags.</returns>
    /// <remarks>
    /// This overload does not support underline or strikethrough tags.
    /// Use the overload with explicit parameters if TextMeshPro support is needed.
    /// </remarks>
    public static string ParseToUnityRichText(string message) {
        return ParseToUnityRichText(message, supportUnderline: false, supportStrikethrough: false);
    }

    /// <summary>
    /// Converts Minecraft color codes to Unity rich text tags with optional extended formatting support.
    /// </summary>
    /// <param name="message">The input string containing Minecraft color codes.</param>
    /// <param name="supportUnderline">If true, enables &lt;u&gt; tags for underline (&amp;n).</param>
    /// <param name="supportStrikethrough">If true, enables &lt;s&gt; tags for strikethrough (&amp;m).</param>
    /// <returns>A string with color codes replaced by Unity rich text tags.</returns>
    /// <remarks>
    /// <para>Set <paramref name="supportUnderline"/> and <paramref name="supportStrikethrough"/> to true when using TextMeshPro.</para>
    /// <para>All open tags are automatically closed at the end of the string.</para>
    /// <para>The &amp;r code closes all active formatting and color tags.</para>
    /// </remarks>
    public static string ParseToUnityRichText(string message, bool supportUnderline, bool supportStrikethrough) {
        if (string.IsNullOrEmpty(message))
            return message;

        var sb = new StringBuilder(message.Length + 16);

        // Track active formatting state
        string? activeColor = null;
        bool bold = false;
        bool italic = false;
        bool underline = false;
        bool strike = false;

        for (int i = 0; i < message.Length; i++) {
            char ch = message[i];

            if (ch != '&') {
                // Prevent raw Unity rich-text tags from user input from being interpreted
                // by breaking the tag with a zero-width space after '<'.
                if (ch == '<') {
                    sb.Append("<\u200B");
                } else {
                    sb.Append(ch);
                }
                continue;
            }

            // Check if there's a character after '&'
            if (i + 1 >= message.Length) {
                sb.Append('&');
                continue;
            }

            char next = message[i + 1];

            // Handle escaped ampersand (&&)
            if (next == '&') {
                sb.Append('&');
                i++;
                continue;
            }

            char code = char.ToLower(next);

            // Handle color codes (0-9, a-f)
            if ((code >= '0' && code <= '9') || (code >= 'a' && code <= 'f')) {
                // Close previous color tag if one is active
                if (activeColor != null)
                    sb.Append("</color>");

                activeColor = UnityHexColors[code];
                sb.Append($"<color=#{activeColor}>");
                i++;
                continue;
            }

            // Handle style and control codes
            switch (code) {
                case 'l': // Bold
                    if (!bold) {
                        sb.Append("<b>");
                        bold = true;
                    }

                    i++;
                    break;

                case 'o': // Italic
                    if (!italic) {
                        sb.Append("<i>");
                        italic = true;
                    }

                    i++;
                    break;

                case 'm': // Strikethrough
                    if (supportStrikethrough && !strike) {
                        sb.Append("<s>");
                        strike = true;
                    }

                    i++;
                    break;

                case 'n': // Underline
                    if (supportUnderline && !underline) {
                        sb.Append("<u>");
                        underline = true;
                    }

                    i++;
                    break;

                case 'r': // Reset all formatting
                    if (italic) {
                        sb.Append("</i>");
                        italic = false;
                    }

                    if (bold) {
                        sb.Append("</b>");
                        bold = false;
                    }

                    if (supportUnderline && underline) {
                        sb.Append("</u>");
                        underline = false;
                    }

                    if (supportStrikethrough && strike) {
                        sb.Append("</s>");
                        strike = false;
                    }

                    if (activeColor != null) {
                        sb.Append("</color>");
                        activeColor = null;
                    }

                    i++;
                    break;

                default:
                    // Unknown code, keep the ampersand
                    sb.Append('&');
                    break;
            }
        }

        // Close any remaining open tags
        if (italic)
            sb.Append("</i>");
        if (bold)
            sb.Append("</b>");
        if (supportUnderline && underline)
            sb.Append("</u>");
        if (supportStrikethrough && strike)
            sb.Append("</s>");
        if (activeColor != null)
            sb.Append("</color>");

        return sb.ToString();
    }

    /// <summary>
    /// Removes all Minecraft color codes from a string, leaving only plain text.
    /// </summary>
    /// <param name="message">The input string containing Minecraft color codes.</param>
    /// <returns>A string with all recognized color codes removed.</returns>
    /// <remarks>
    /// Recognized codes are silently removed. Unrecognized codes are preserved.
    /// Use &amp;&amp; to output a literal ampersand.
    /// </remarks>
    public static string StripColorCodes(string message) {
        if (string.IsNullOrEmpty(message))
            return message;

        var sb = new StringBuilder(message.Length);

        for (int i = 0; i < message.Length; i++) {
            char ch = message[i];

            if (ch != '&') {
                sb.Append(ch);
                continue;
            }

            // Check if there's a character after '&'
            if (i + 1 >= message.Length) {
                sb.Append('&');
                continue;
            }

            char next = message[i + 1];

            // Handle escaped ampersand (&&)
            if (next == '&') {
                sb.Append('&');
                i++;
                continue;
            }

            // Check if this is a recognized code
            char code = char.ToLower(next);
            if (AnsiCodes.ContainsKey(code)) {
                // Recognized code: skip both '&' and the code character
                i++;
                continue;
            }

            // Unknown sequence: preserve the ampersand
            sb.Append('&');
        }

        return sb.ToString();
    }
}

using System.Collections.Generic;
using System.Text;

namespace SSMP.Logging;

/// <summary>
/// Utility class for parsing Minecraft-style color and formatting codes.
/// </summary>
public static class ColorCodeParser {
    /// <summary>
    /// Mapping of color/format codes to ANSI escape sequences.
    /// </summary>
    private static readonly Dictionary<char, string> AnsiCodes = new() {
        // Colors
        { '0', "\x1b[97m" }, // Black
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
        { 'f', "\x1b[30m" }, // White

        // Formatting
        { 'l', "\x1b[1m" }, // Bold
        { 'm', "\x1b[9m" }, // Strikethrough
        { 'n', "\x1b[4m" }, // Underline
        { 'o', "\x1b[3m" }, // Italic

        // Reset
        { 'r', "\x1b[0m" } // Reset
    };

    /// <summary>
    /// Parse a message with Minecraft-style color codes (&) and convert to ANSI escape sequences.
    /// </summary>
    /// <param name="message">The message with color codes.</param>
    /// <returns>The message with ANSI escape sequences.</returns>
    public static string ParseToAnsi(string message) {
        if (string.IsNullOrEmpty(message)) {
            return message;
        }

        var result = new StringBuilder(message.Length);

        for (int i = 0; i < message.Length; i++) {
            if (message[i] == '&' && i + 1 < message.Length) {
                char code = char.ToLower(message[i + 1]);

                if (AnsiCodes.TryGetValue(code, out var ansiCode)) {
                    result.Append(ansiCode);
                    i++; // Skip the next character (the code)
                    continue;
                }
            }

            result.Append(message[i]);
        }

        return result.ToString();
    }

    /// <summary>
    /// Strip all color codes from a message, leaving only the plain text.
    /// </summary>
    /// <param name="message">The message with color codes.</param>
    /// <returns>The message without color codes.</returns>
    public static string StripColorCodes(string message) {
        if (string.IsNullOrEmpty(message)) {
            return message;
        }

        var result = new StringBuilder(message.Length);

        for (int i = 0; i < message.Length; i++) {
            if (message[i] == '&' && i + 1 < message.Length) {
                char code = char.ToLower(message[i + 1]);

                if (AnsiCodes.ContainsKey(code)) {
                    i++; // Skip the next character (the code)
                    continue;
                }
            }

            result.Append(message[i]);
        }

        return result.ToString();
    }
}

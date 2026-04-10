using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using SSMP.Networking.Matchmaking.Protocol;
using SSMP.Networking.Matchmaking.Utilities;

namespace SSMP.Networking.Matchmaking.Parsing;

/// <summary>
/// Lightweight JSON helpers for reading and writing MMS API payloads.
/// These avoid a full JSON library dependency and are intentionally simple:
/// they assume well-formed server responses and handle only the small set
/// of value types (quoted strings, integers) that MMS actually returns.
/// </summary>
internal static class MmsJsonParser {
    /// <summary>Shared pool for minimizing character buffer allocations.</summary>
    private static readonly ArrayPool<char> CharPool = ArrayPool<char>.Shared;

    /// <summary>
    /// Finds <c>"key":value</c> in <paramref name="json"/> and returns the value as a string.
    /// Supports both quoted string values and unquoted numeric values.
    /// Returns <c>null</c> when the key is absent.
    /// </summary>
    /// <param name="json">The JSON span to search.</param>
    /// <param name="key">The key to find.</param>
    /// <returns>The extracted value or <c>null</c>.</returns>
    public static string? ExtractValue(ReadOnlySpan<char> json, string key) {
        Span<char> searchKey = stackalloc char[key.Length + 2];
        searchKey[0] = '"';
        key.AsSpan().CopyTo(searchKey[1..]);
        searchKey[key.Length + 1] = '"';

        var searchStart = 0;
        while (searchStart < json.Length) {
            var relative = json[searchStart..].IndexOf(searchKey, StringComparison.Ordinal);
            if (relative == -1) return null;

            var keyEnd = searchStart + relative + searchKey.Length;
            var valueStart = MmsUtilities.SkipWhitespace(json, keyEnd);
            if (valueStart >= json.Length || json[valueStart] != ':') {
                searchStart = searchStart + relative + 1;
                continue;
            }

            valueStart = MmsUtilities.SkipWhitespace(json, valueStart + 1);
            if (valueStart >= json.Length) return null;

            return json[valueStart] == '"'
                ? ExtractStringValue(json, valueStart)
                : ExtractNumericValue(json, valueStart);
        }

        return null;
    }

    /// <summary>
    /// Writes the CreateLobby JSON payload into a rented char buffer.
    /// Returns the number of characters written.
    /// The caller must return the buffer to <see cref="ArrayPool{T}.Shared"/> after use.
    /// </summary>
    /// <param name="port">The host port.</param>
    /// <param name="isPublic">Whether the lobby is public.</param>
    /// <param name="gameVersion">The game version string.</param>
    /// <param name="lobbyType">The type of the lobby.</param>
    /// <param name="hostLanIp">Optional local IP address.</param>
    /// <returns>A tuple containing the buffer and the number of characters written.</returns>
    public static (char[] buffer, int length) FormatCreateLobbyJson(
        int port,
        bool isPublic,
        string gameVersion,
        PublicLobbyType lobbyType,
        string? hostLanIp
    ) {
        var escapedGameVersion = MmsUtilities.EscapeJsonString(gameVersion);
        var escapedHostLanIp = hostLanIp == null ? null : MmsUtilities.EscapeJsonString(hostLanIp);
        var lobbyTypeValue = lobbyType == PublicLobbyType.Matchmaking ? "matchmaking" : "steam";
        var estimatedLength =
            96 +
            escapedGameVersion.Length +
            lobbyTypeValue.Length +
            (escapedHostLanIp?.Length ?? 0) +
            (hostLanIp != null ? 16 : 0) +
            (lobbyType == PublicLobbyType.Matchmaking ? 24 : 0);

        var buffer = CharPool.Rent(estimatedLength);
        var span = buffer.AsSpan();
        var written = 0;

        Write(span, ref written, "{\"");
        Write(span, ref written, MmsFields.HostPortRequest);
        Write(span, ref written, "\":");
        Write(span, ref written, port);
        Write(span, ref written, ",\"");
        Write(span, ref written, MmsFields.IsPublicRequest);
        Write(span, ref written, "\":");
        Write(span, ref written, MmsUtilities.BoolToJson(isPublic));
        Write(span, ref written, ",\"");
        Write(span, ref written, MmsFields.GameVersionRequest);
        Write(span, ref written, "\":\"");
        Write(span, ref written, escapedGameVersion);
        Write(span, ref written, "\",\"");
        Write(span, ref written, MmsFields.LobbyTypeRequest);
        Write(span, ref written, "\":\"");
        Write(span, ref written, lobbyTypeValue);
        Write(span, ref written, "\"");

        if (hostLanIp != null) {
            Write(span, ref written, ",\"");
            Write(span, ref written, MmsFields.HostLanIpRequest);
            Write(span, ref written, "\":\"");
            Write(span, ref written, escapedHostLanIp!);
            Write(span, ref written, ":");
            Write(span, ref written, port);
            Write(span, ref written, "\"");
        }

        if (lobbyType == PublicLobbyType.Matchmaking) {
            Write(span, ref written, ",\"");
            Write(span, ref written, MmsFields.MatchmakingVersionRequest);
            Write(span, ref written, "\":");
            Write(span, ref written, MmsProtocol.CurrentVersion);
        }

        Write(span, ref written, "}");
        return (buffer, written);
    }

    /// <summary>
    /// Returns a char buffer to the shared array pool.
    /// </summary>
    public static void ReturnBuffer(char[] buffer) => CharPool.Return(buffer);

    /// <summary>
    /// Extracts a quoted string value starting at the opening <c>"</c>.
    /// Returns <c>null</c> if the closing quote is missing.
    /// </summary>
    private static string? ExtractStringValue(ReadOnlySpan<char> json, int openQuoteIndex) {
        var segmentStart = openQuoteIndex + 1;
        StringBuilder? builder = null;

        for (var i = segmentStart; i < json.Length; i++) {
            if (json[i] == '\\') {
                if (i + 1 >= json.Length) return null;
                if (builder == null) builder = new StringBuilder(json.Slice(segmentStart, i - segmentStart).ToString());
                else builder.Append(json.Slice(segmentStart, i - segmentStart));
                var escape = json[++i];
                switch (escape) {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (i + 4 >= json.Length) return null;

                        var hex = json.Slice(i + 1, 4);
                        if (!ushort.TryParse(hex, NumberStyles.HexNumber, null, out var codePoint))
                            return null;

                        builder.Append((char) codePoint);
                        i += 4;
                        break;
                    default:
                        throw new FormatException($"Invalid JSON escape sequence \\{escape} at index {i}.");
                }

                segmentStart = i + 1;
                continue;
            }

            if (json[i] != '"') continue;

            if (builder == null)
                return json.Slice(openQuoteIndex + 1, i - openQuoteIndex - 1).ToString();

            builder.Append(json.Slice(segmentStart, i - segmentStart));
            return builder.ToString();
        }

        return null;
    }

    /// <summary>
    /// Extracts an unquoted numeric value (digits, <c>.</c>, <c>-</c>) starting at
    /// <paramref name="start"/>. Returns an empty string if no numeric characters are found.
    /// </summary>
    private static string ExtractNumericValue(ReadOnlySpan<char> json, int start) {
        var end = start;
        while (end < json.Length &&
               (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-')) {
            end++;
        }

        return json.Slice(start, end - start).ToString();
    }

    /// <summary>
    /// Copies a string value into the destination buffer at the current write position.
    /// </summary>
    /// <param name="destination">The character buffer to write into.</param>
    /// <param name="written">The current write position; incremented by the length of <paramref name="value"/>.</param>
    /// <param name="value">The string value to copy.</param>
    private static void Write(Span<char> destination, ref int written, ReadOnlySpan<char> value) {
        value.CopyTo(destination[written..]);
        written += value.Length;
    }

    /// <summary>
    /// Formats an integer value into the destination buffer at the current write position.
    /// </summary>
    /// <param name="destination">The character buffer to write into.</param>
    /// <param name="written">The current write position; incremented by the number of characters written.</param>
    /// <param name="value">The integer value to format.</param>
    /// <exception cref="InvalidOperationException">Thrown when the integer cannot be formatted into the remaining buffer space.</exception>
    private static void Write(Span<char> destination, ref int written, int value) {
        if (!value.TryFormat(destination[written..], out var charsWritten))
            throw new InvalidOperationException("Could not format MMS JSON integer.");

        written += charsWritten;
    }
}

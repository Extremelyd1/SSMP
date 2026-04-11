using System;
using System.Buffers;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SSMP.Networking.Matchmaking.Protocol;

namespace SSMP.Networking.Matchmaking.Parsing;

/// <summary>Reads and writes the small JSON payloads used by MMS.</summary>
internal static class MmsJsonParser {
    /// <summary>
    /// A scoped rental of a pooled char buffer. Disposing returns the buffer to the shared
    /// pool, making double-return safe and ensuring the caller cannot forget to release.
    /// </summary>
    internal sealed class CharLease : IDisposable {
        private static readonly ArrayPool<char> Pool = ArrayPool<char>.Shared;
        private char[]? _buffer;

        /// <summary>The number of valid characters in <see cref="Span"/>.</summary>
        private int Length { get; }

        /// <summary>The serialized JSON content, valid only while this lease is undisposed.</summary>
        public ReadOnlySpan<char> Span => _buffer != null
            ? _buffer.AsSpan(0, Length)
            : ReadOnlySpan<char>.Empty;

        internal CharLease(char[] buffer, int length) {
            _buffer = buffer;
            Length  = length;
        }

        /// <summary>Returns the rented buffer to the pool. Safe to call more than once.</summary>
        public void Dispose() {
            var buf = _buffer;
            if (buf == null) {
                return;
            }

            _buffer = null;
            Pool.Return(buf);
        }
    }

    /// <summary>
    /// Parses a JSON string and returns the first property with the requested key.
    /// Returns null when the payload is invalid or the key is missing.
    /// </summary>
    private static string? ExtractValue(string json, string key) {
        try {
            var token = JToken.Parse(json);
            var property = FindPropertyRecursive(token, key);
            return property == null ? null : ConvertTokenToString(property.Value);
        } catch (JsonReaderException) {
            return null;
        }
    }

    /// <summary>
    /// Span-based wrapper for callers that already have a message buffer.
    /// Converts once, then reuses the string overload.
    /// </summary>
    public static string? ExtractValue(ReadOnlySpan<char> json, string key) => ExtractValue(json.ToString(), key);

    /// <summary>
    /// Serializes the create-lobby payload into a scoped <see cref="CharLease"/>.
    /// Dispose the returned lease (e.g. with <c>using</c>) to return the buffer to the pool.
    /// </summary>
    public static CharLease FormatCreateLobbyJson(
        int port,
        bool isPublic,
        string gameVersion,
        PublicLobbyType lobbyType,
        string? hostLanIp
    ) {
        var payload = new JObject {
            [MmsFields.HostPortRequest]    = port,
            [MmsFields.IsPublicRequest]    = isPublic,
            [MmsFields.GameVersionRequest] = gameVersion,
            [MmsFields.LobbyTypeRequest]   = SerializeLobbyType(lobbyType)
        };

        if (hostLanIp != null) {
            payload[MmsFields.HostLanIpRequest] = $"{hostLanIp}:{port}";
        }

        // Matchmaking lobbies carry a protocol version so MMS can reject stale clients.
        if (lobbyType == PublicLobbyType.Matchmaking) {
            payload[MmsFields.MatchmakingVersionRequest] = MmsProtocol.CurrentVersion;
        }

        var json   = payload.ToString(Formatting.None);
        var buffer = ArrayPool<char>.Shared.Rent(json.Length);
        json.AsSpan().CopyTo(buffer);
        return new CharLease(buffer, json.Length);
    }

    /// <summary>Walks nested objects and arrays until it finds a matching property name.</summary>
    private static JProperty? FindPropertyRecursive(JToken token, string key) {
        switch (token.Type) {
            case JTokenType.Object: {
                foreach (var property in token.Children<JProperty>()) {
                    if (string.Equals(property.Name, key, StringComparison.Ordinal)) {
                        return property;
                    }

                    var nestedMatch = FindPropertyRecursive(property.Value, key);
                    if (nestedMatch != null) {
                        return nestedMatch;
                    }
                }

                return null;
            }

            case JTokenType.Array:
                return token.Children()
                            .Select(child => FindPropertyRecursive(child, key))
                            .OfType<JProperty>()
                            .FirstOrDefault();

            // Scalar and leaf token types contain no child properties to search.
            case JTokenType.None:
            case JTokenType.Constructor:
            case JTokenType.Property:
            case JTokenType.Comment:
            case JTokenType.Integer:
            case JTokenType.Float:
            case JTokenType.String:
            case JTokenType.Boolean:
            case JTokenType.Null:
            case JTokenType.Undefined:
            case JTokenType.Date:
            case JTokenType.Raw:
            case JTokenType.Bytes:
            case JTokenType.Guid:
            case JTokenType.Uri:
            case JTokenType.TimeSpan:
            default:
                return null;
        }
    }

    /// <summary>
    /// Maps the local enum to the lowercase lobby type string MMS expects.
    /// Throws <see cref="ArgumentOutOfRangeException"/> for unrecognized values so that
    /// new enum members are caught at development time rather than silently misrouted.
    /// </summary>
    private static string SerializeLobbyType(PublicLobbyType lobbyType) => lobbyType switch {
        PublicLobbyType.Matchmaking => "matchmaking",
        PublicLobbyType.Steam       => "steam",
        _ => throw new ArgumentOutOfRangeException(nameof(lobbyType), lobbyType,
                 $"No MMS name defined for lobby type '{lobbyType}'.")
    };

    /// <summary>Converts a JSON token into the string shape expected by existing callers.</summary>
    private static string? ConvertTokenToString(JToken token) => token.Type switch {
        JTokenType.Null    => null,
        JTokenType.String  => token.Value<string>(),
        JTokenType.Integer => token.Value<long>().ToString(CultureInfo.InvariantCulture),
        JTokenType.Float   => token.Value<double>().ToString(CultureInfo.InvariantCulture),
        JTokenType.Boolean => token.Value<bool>() ? "true" : "false",
        _                  => token.ToString(Formatting.None)
    };
}

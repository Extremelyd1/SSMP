using System;
using System.Collections.Generic;
using SSMP.Logging;
using SSMP.Networking.Matchmaking.Protocol;

namespace SSMP.Networking.Matchmaking.Parsing;

/// <summary>
/// Parses raw MMS JSON response bodies into typed matchmaking models.
/// All methods are allocation-minimal where possible, operating on
/// <see cref="ReadOnlySpan{T}"/> slices via <see cref="MmsJsonParser"/>.
/// </summary>
internal static class MmsResponseParser {
    /// <summary>
    /// Lookup key for scanning lobby object boundaries.
    /// </summary>
    private const string ConnectionDataKey = $"\"{MmsFields.ConnectionData}\":";

    /// <summary>
    /// Parses a lobby creation or Steam-lobby registration response into the
    /// fields required to activate a host session.
    /// </summary>
    /// <param name="response">Raw JSON response body from the MMS <c>/lobby</c> endpoint.</param>
    /// <param name="lobbyId">
    /// Receives the lobby's connection data string (used internally as the lobby ID),
    /// or <c>null</c> if absent.
    /// </param>
    /// <param name="hostToken">
    /// Receives the bearer token used to authenticate subsequent heartbeat and delete
    /// requests, or <c>null</c> if absent.
    /// </param>
    /// <param name="lobbyName">
    /// Receives the human-readable lobby display name, or <c>null</c> if absent.
    /// </param>
    /// <param name="lobbyCode">
    /// Receives the short alphanumeric join code shown to players, or <c>null</c> if absent.
    /// </param>
    /// <param name="hostDiscoveryToken">
    /// Receives the UDP discovery token sent during NAT hole-punch mapping,
    /// or <c>null</c> if the server did not include one (e.g. for Steam lobbies).
    /// </param>
    /// <returns>
    /// <c>true</c> if all required fields (<paramref name="lobbyId"/>,
    /// <paramref name="hostToken"/>, <paramref name="lobbyName"/>, and
    /// <paramref name="lobbyCode"/>) were present; <c>false</c> otherwise.
    /// Note that <paramref name="hostDiscoveryToken"/> being <c>null</c> does not
    /// cause a failure.
    /// </returns>
    public static bool TryParseLobbyActivation(
        string response,
        out string? lobbyId,
        out string? hostToken,
        out string? lobbyName,
        out string? lobbyCode,
        out string? hostDiscoveryToken
    ) {
        var span = response.AsSpan();
        lobbyId = MmsJsonParser.ExtractValue(span, MmsFields.ConnectionData);
        hostToken = MmsJsonParser.ExtractValue(span, MmsFields.HostToken);
        lobbyName = MmsJsonParser.ExtractValue(span, MmsFields.LobbyName);
        lobbyCode = MmsJsonParser.ExtractValue(span, MmsFields.LobbyCode);
        hostDiscoveryToken = MmsJsonParser.ExtractValue(span, MmsFields.HostDiscoveryToken);
        return lobbyId != null && hostToken != null && lobbyName != null && lobbyCode != null;
    }

    /// <summary>
    /// Parses a <c>/lobby/{id}/join</c> response into a <see cref="JoinLobbyResult"/>.
    /// </summary>
    /// <param name="response">Raw JSON response body from the MMS join endpoint.</param>
    /// <returns>
    /// A populated <see cref="JoinLobbyResult"/> on success, or <c>null</c> if
    /// <c>connectionData</c> or <c>lobbyType</c> are missing, or if <c>lobbyType</c>
    /// cannot be mapped to a known <see cref="PublicLobbyType"/> value.
    /// </returns>
    public static JoinLobbyResult? ParseJoinLobbyResult(string response) {
        var span = response.AsSpan();

        return !TryExtractJoinRequiredFields(span, out var connectionData, out var lobbyType)
            ? null
            : BuildJoinLobbyResult(span, connectionData!, lobbyType);
    }

    /// <summary>
    /// Parses the public lobby list response from the MMS <c>/lobbies</c> endpoint.
    /// Entries are extracted by scanning for successive <c>"connectionData"</c> keys,
    /// treating each occurrence as the start of a new lobby object.
    /// Entries missing <c>connectionData</c> or <c>name</c> are silently skipped.
    /// An unrecognised <c>lobbyType</c> defaults to <see cref="PublicLobbyType.Matchmaking"/>.
    /// </summary>
    /// <param name="response">Raw JSON response body containing a JSON array of lobby objects.</param>
    /// <returns>
    /// A list of <see cref="PublicLobbyInfo"/> entries. Returns an empty list if the
    /// response contains no parseable lobbies.
    /// </returns>
    /// <remarks>
    /// Lobby objects are limited by scanning for successive <c>"connectionData":</c> keys.
    /// This assumes <c>connectionData</c> is the first field in each lobby object and that
    /// no other field values contain the literal string <c>"connectionData":</c>.
    /// </remarks>
    public static List<PublicLobbyInfo> ParsePublicLobbies(string response) {
        var result = new List<PublicLobbyInfo>();
        var span = response.AsSpan();
        var idx = 0;

        while (TryFindNextLobbySlice(span, ref idx, out var slice)) {
            var entry = TryParsePublicLobbyEntry(slice);
            if (entry != null) {
                result.Add(entry);
            } else {
                Logger.Debug($"MmsResponseParser: Skipped unparseable lobby entry at index {idx}.");
            }
        }

        return result;
    }

    /// <summary>
    /// Parses the <c>start_punch</c> WebSocket message received during the join
    /// rendezvous phase, extracting the host endpoint and the scheduled punch start time.
    /// </summary>
    /// <param name="span">
    /// A <see cref="ReadOnlySpan{T}"/> over the raw UTF-8 decoded message text.
    /// </param>
    /// <returns>
    /// A <see cref="MatchmakingJoinStartResult"/> containing the host IP, port, and
    /// Unix-millisecond start timestamp, or <c>null</c> if any required field
    /// (<c>hostIp</c>, <c>hostPort</c>, <c>startTimeMs</c>) is missing or unparseable.
    /// </returns>
    public static MatchmakingJoinStartResult? ParseStartPunch(ReadOnlySpan<char> span) {
        return !TryExtractPunchFields(span, out var hostIp, out var hostPort, out var startTimeMs)
            ? null
            : new MatchmakingJoinStartResult { HostIp = hostIp!, HostPort = hostPort, StartTimeMs = startTimeMs };
    }

    /// <summary>
    /// Extracts and validates the two required fields for a join response:
    /// <c>connectionData</c> and a parseable <c>lobbyType</c>.
    /// </summary>
    /// <param name="span">Span over the raw response text.</param>
    /// <param name="connectionData">Receives the connection string, or <c>null</c> on failure.</param>
    /// <param name="lobbyType">Receives the parsed <see cref="PublicLobbyType"/> on success.</param>
    /// <returns><c>true</c> if both fields were present and valid; <c>false</c> otherwise.</returns>
    private static bool TryExtractJoinRequiredFields(
        ReadOnlySpan<char> span,
        out string? connectionData,
        out PublicLobbyType lobbyType
    ) {
        connectionData = MmsJsonParser.ExtractValue(span, MmsFields.ConnectionData);
        var lobbyTypeString = MmsJsonParser.ExtractValue(span, MmsFields.LobbyType);

        if (connectionData == null || lobbyTypeString == null) {
            lobbyType = default;
            return false;
        }

        // Default to Matchmaking for unknown lobby types, consistent with TryParsePublicLobbyEntry
        if (!Enum.TryParse(lobbyTypeString, true, out lobbyType))
            lobbyType = PublicLobbyType.Matchmaking;

        return true;
    }

    /// <summary>
    /// Constructs a <see cref="JoinLobbyResult"/> from a validated span, populating
    /// all optional fields alongside the required ones.
    /// </summary>
    /// <param name="span">Span over the raw response text.</param>
    /// <param name="connectionData">Pre-validated connection string.</param>
    /// <param name="lobbyType">Pre-validated lobby type.</param>
    private static JoinLobbyResult BuildJoinLobbyResult(
        ReadOnlySpan<char> span,
        string connectionData,
        PublicLobbyType lobbyType
    ) => new() {
        ConnectionData = connectionData,
        LobbyType = lobbyType,
        LanConnectionData = MmsJsonParser.ExtractValue(span, MmsFields.LanConnectionData),
        JoinId = MmsJsonParser.ExtractValue(span, MmsFields.JoinId)
    };

    /// <summary>
    /// Advances <paramref name="idx"/> to the next <c>"connectionData":</c> key in
    /// <paramref name="span"/> and returns the sub-span starting at that key.
    /// </summary>
    /// <param name="span">The full response span being scanned.</param>
    /// <param name="idx">
    /// Current scan position. Updated to one character past the found key so the
    /// next call advances past the current entry.
    /// </param>
    /// <param name="slice">
    /// Receives a sub-span beginning at the found key, suitable for field extraction.
    /// Empty when the method returns <c>false</c>.
    /// </param>
    /// <returns><c>true</c> if another entry was found; <c>false</c> when the scan is exhausted.</returns>
    private static bool TryFindNextLobbySlice(
        ReadOnlySpan<char> span,
        ref int idx,
        out ReadOnlySpan<char> slice
    ) {
        var relative = span[idx..].IndexOf(ConnectionDataKey, StringComparison.Ordinal);
        if (relative == -1) {
            slice = default;
            return false;
        }

        var start = idx + relative;
        var nextRelative = span[(start + ConnectionDataKey.Length)..]
            .IndexOf(ConnectionDataKey, StringComparison.Ordinal);
        var end = nextRelative == -1 ? span.Length : start + ConnectionDataKey.Length + nextRelative;
        slice = span[start..end];
        idx = end;
        return true;
    }

    /// <summary>
    /// Parses a single lobby object from a span that starts at its
    /// <c>"connectionData":</c> key. Returns <c>null</c> if either
    /// <c>connectionData</c> or <c>name</c> are absent.
    /// An unrecognised <c>lobbyType</c> defaults to <see cref="PublicLobbyType.Matchmaking"/>.
    /// </summary>
    /// <param name="slice">Sub-span starting at the lobby object's <c>connectionData</c> key.</param>
    /// <returns>A <see cref="PublicLobbyInfo"/>, or <c>null</c> if required fields are missing.</returns>
    private static PublicLobbyInfo? TryParsePublicLobbyEntry(ReadOnlySpan<char> slice) {
        var connectionData = MmsJsonParser.ExtractValue(slice, MmsFields.ConnectionData);
        var name = MmsJsonParser.ExtractValue(slice, MmsFields.Name);

        if (connectionData == null || name == null) return null;

        var typeString = MmsJsonParser.ExtractValue(slice, MmsFields.LobbyType);
        var code = MmsJsonParser.ExtractValue(slice, MmsFields.LobbyCode);

        var type = PublicLobbyType.Matchmaking;
        if (typeString != null) Enum.TryParse(typeString, true, out type);

        return new PublicLobbyInfo(connectionData, name, type, code ?? "");
    }

    /// <summary>
    /// Extracts and validates all three required fields from a <c>start_punch</c>
    /// message: <c>hostIp</c>, <c>hostPort</c>, and <c>startTimeMs</c>.
    /// </summary>
    /// <param name="span">Span over the raw message text.</param>
    /// <param name="hostIp">Receives the host IP string, or <c>null</c> on failure.</param>
    /// <param name="hostPort">Receives the parsed host port on success; 0 on failure.</param>
    /// <param name="startTimeMs">Receives the parsed start timestamp on success; 0 on failure.</param>
    /// <returns><c>true</c> if all three fields were present and parseable; <c>false</c> otherwise.</returns>
    private static bool TryExtractPunchFields(
        ReadOnlySpan<char> span,
        out string? hostIp,
        out int hostPort,
        out long startTimeMs
    ) {
        hostIp = MmsJsonParser.ExtractValue(span, MmsFields.HostIp);
        var hostPortStr = MmsJsonParser.ExtractValue(span, MmsFields.HostPort);
        var startTimeStr = MmsJsonParser.ExtractValue(span, MmsFields.StartTimeMs);

        if (hostIp == null ||
            !int.TryParse(hostPortStr, out hostPort) ||
            !long.TryParse(startTimeStr, out startTimeMs)) {
            hostPort = 0;
            startTimeMs = 0;
            return false;
        }

        return true;
    }
}

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SSMP.Logging;
using SSMP.Networking.Matchmaking.Protocol;

namespace SSMP.Networking.Matchmaking.Parsing;

/// <summary>Builds Matchmaking models from raw MMS response payloads.</summary>
internal static class MmsResponseParser {
    /// <summary>
    /// Reads the create-lobby response fields needed to start a host session.
    /// Returns false when any required field is missing.
    /// </summary>
    /// <remarks>
    /// <paramref name="hostDiscoveryToken"/> is intentionally excluded from the required-field
    /// check: MMS omits it for non-matchmaking lobbies, so null is a valid server response and
    /// callers must guard against it rather than treating it as a parse failure.
    /// </remarks>
    public static bool TryParseLobbyActivation(
        string response,
        out string? lobbyId,
        out string? hostToken,
        out string? lobbyName,
        out string? lobbyCode,
        out string? hostDiscoveryToken
    ) {
        var root = ParseJsonObject(response);
        lobbyId             = root?.Value<string>(MmsFields.ConnectionData);
        hostToken           = root?.Value<string>(MmsFields.HostToken);
        lobbyName           = root?.Value<string>(MmsFields.LobbyName);
        lobbyCode           = root?.Value<string>(MmsFields.LobbyCode);
        hostDiscoveryToken  = root?.Value<string>(MmsFields.HostDiscoveryToken);

        return lobbyId != null && hostToken != null && lobbyName != null && lobbyCode != null;
    }

    /// <summary>
    /// Parses the join response into the local result model.
    /// Returns null when MMS omits the required connection fields.
    /// </summary>
    public static JoinLobbyResult? ParseJoinLobbyResult(string response) {
        var root = ParseJsonObject(response);
        if (root == null) {
            return null;
        }

        var connectionData   = root.Value<string>(MmsFields.ConnectionData);
        var lobbyTypeString  = root.Value<string>(MmsFields.LobbyType);

        if (connectionData == null || lobbyTypeString == null) {
            return null;
        }

        return new JoinLobbyResult {
            ConnectionData    = connectionData,
            LobbyType         = ParseLobbyType(lobbyTypeString),
            LanConnectionData = root.Value<string>(MmsFields.LanConnectionData),
            JoinId            = root.Value<string>(MmsFields.JoinId)
        };
    }

    /// <summary>
    /// Parses the public lobby listing returned by MMS.
    /// Returns an empty list when the payload is malformed.
    /// </summary>
    public static List<PublicLobbyInfo> ParsePublicLobbies(string response) {
        try {
            var lobbies = ParseLobbiesAsArray(response);
            return ExtractValidLobbies(lobbies);
        } catch (JsonReaderException) {
            Logger.Debug("MmsResponseParser: Failed to parse public lobbies JSON.");
            return [];
        }
    }

    /// <summary>
    /// Parses the start-punch message sent before synchronized hole punching.
    /// Returns null when the host endpoint or timestamp is missing.
    /// </summary>
    private static MatchmakingJoinStartResult? ParseStartPunch(string json) {
        var root = ParseJsonObject(json);
        if (root == null) {
            return null;
        }

        var hostIp   = root.Value<string>(MmsFields.HostIp);
        var hostPort = root.Value<int?>(MmsFields.HostPort);
        var startTime = root.Value<long?>(MmsFields.StartTimeMs);

        if (hostIp == null || hostPort == null || startTime == null) {
            return null;
        }

        return new MatchmakingJoinStartResult {
            HostIp      = hostIp,
            HostPort    = hostPort.Value,
            StartTimeMs = startTime.Value
        };
    }

    /// <summary>Span-based wrapper for callers that already work with message spans.</summary>
    public static MatchmakingJoinStartResult? ParseStartPunch(ReadOnlySpan<char> span) =>
        ParseStartPunch(span.ToString());

    /// <summary>Normalizes lobby-list payloads so callers can always iterate a JSON array.</summary>
    private static JArray ParseLobbiesAsArray(string response) {
        return JToken.Parse(response) switch {
            JArray  array => array,
            JObject obj   => [obj],
            var other     => LogAndReturnEmpty(other)
        };

        static JArray LogAndReturnEmpty(JToken other) {
            Logger.Debug($"MmsResponseParser: Unexpected lobby payload token type '{other.Type}', expected array or object.");
            return [];
        }
    }

    /// <summary>Filters malformed lobby entries and converts the valid ones to models.</summary>
    private static List<PublicLobbyInfo> ExtractValidLobbies(JArray lobbies) {
        var result = new List<PublicLobbyInfo>(lobbies.Count);

        foreach (var token in lobbies) {
            if (token is not JObject lobbyObject) {
                Logger.Debug("MmsResponseParser: Skipped non-object lobby entry.");
                continue;
            }

            var lobby = TryParseLobbyEntry(lobbyObject);
            if (lobby != null) {
                result.Add(lobby);
            } else {
                Logger.Debug("MmsResponseParser: Skipped unparseable lobby entry.");
            }
        }

        return result;
    }

    /// <summary>Parses one lobby entry from the public lobby list.</summary>
    private static PublicLobbyInfo? TryParseLobbyEntry(JObject lobby) {
        var connectionData = lobby.Value<string>(MmsFields.ConnectionData);
        var name           = lobby.Value<string>(MmsFields.Name);
        var lobbyCode      = lobby.Value<string>(MmsFields.LobbyCode);

        // All three fields are required: a missing lobby code would break client-side
        // code entry and is not a valid state a well-formed MMS response can produce.
        if (connectionData == null || name == null || lobbyCode == null) {
            return null;
        }

        var lobbyTypeString = lobby.Value<string>(MmsFields.LobbyType);
        var lobbyType = lobbyTypeString != null
            ? ParseLobbyType(lobbyTypeString)
            : PublicLobbyType.Matchmaking;

        return new PublicLobbyInfo(connectionData, name, lobbyType, lobbyCode);
    }

    /// <summary>Parses a lobby type string and defaults unknown values to Matchmaking.</summary>
    private static PublicLobbyType ParseLobbyType(string lobbyTypeString) {
        if (Enum.TryParse(lobbyTypeString, ignoreCase: true, out PublicLobbyType lobbyType)) {
            return lobbyType;
        }

        Logger.Debug($"MmsResponseParser: Unknown lobby type '{lobbyTypeString}', defaulting to Matchmaking.");
        return PublicLobbyType.Matchmaking;
    }

    /// <summary>Parses a JSON object root and returns null when the payload is invalid.</summary>
    private static JObject? ParseJsonObject(string json) {
        try {
            return JObject.Parse(json);
        } catch (JsonReaderException) {
            return null;
        }
    }
}

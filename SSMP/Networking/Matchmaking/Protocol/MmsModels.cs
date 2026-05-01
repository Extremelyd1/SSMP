namespace SSMP.Networking.Matchmaking.Protocol;

/// <summary>MatchMaking Service (MMS) protocol constants.</summary>
internal static class MmsProtocol {
    /// <summary>The current version of the matchmaking protocol.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Error code returned by MMS when the client version is too old.</summary>
    public const string UpdateRequiredErrorCode = "update_required";

    /// <summary>Interval for sending heartbeats to the server.</summary>
    public const int HeartbeatIntervalMs = 30_000;

    /// <summary>Timeout for HTTP requests to the MMS API.</summary>
    public const int HttpTimeoutMs = 5_000;

    /// <summary>Timeout for the client-side matchmaking WebSocket handshake.</summary>
    public const int MatchmakingWebSocketTimeoutMs = 20_000;

    /// <summary>Duration for which UDP discovery packets are sent.</summary>
    public const int DiscoveryDurationSeconds = 15;

    /// <summary>Interval between individual UDP discovery packets.</summary>
    public const int DiscoveryIntervalMs = 500;
}

/// <summary>Matchmaking error classification.</summary>
internal enum MatchmakingError {
    /// <summary>No error.</summary>
    None,

    /// <summary>The client matchmaking version is outdated.</summary>
    UpdateRequired,

    /// <summary>The join operation failed (e.g. timeout or invalid ID).</summary>
    JoinFailed,

    /// <summary>A generic network error occurred.</summary>
    NetworkFailure
}

/// <summary>Supported MMS lobby subtypes.</summary>
public enum PublicLobbyType {
    /// <summary>Standalone matchmaking through MMS.</summary>
    Matchmaking,

    /// <summary>Steam matchmaking through MMS.</summary>
    Steam
}

/// <summary>Lobby join request metadata.</summary>
internal sealed class JoinLobbyResult {
    /// <summary>The connection string for the lobby (e.g. "IP:Port" or Steam ID).</summary>
    public required string ConnectionData { get; init; }

    /// <summary>The type of the lobby.</summary>
    public required PublicLobbyType LobbyType { get; init; }

    /// <summary>Optional LAN connection string for local play.</summary>
    public string? LanConnectionData { get; init; }

    /// <summary>Unique ID for the join session.</summary>
    public string? JoinId { get; init; }
}

/// <summary>NAT hole-punch peer data and synchronized start time.</summary>
internal sealed class MatchmakingJoinStartResult {
    /// <summary>The resolved public IP of the host.</summary>
    public required string HostIp { get; init; }

    /// <summary>The resolved public port of the host.</summary>
    public required int HostPort { get; init; }

    /// <summary>The Unix timestamp (ms) when both sides should start punching.</summary>
    public required long StartTimeMs { get; init; }
}

/// <summary>Registry entry for the lobby browser.</summary>
/// <param name="ConnectionData"> The connection string for the lobby (e.g. "IP:Port" or Steam ID). </param>
/// <param name="Name"> The display name of the lobby. </param>
/// <param name="LobbyType"> The type of the lobby (e.g. Matchmaking or Steam). </param>
/// <param name="LobbyCode"> A short alphanumeric code used to join the lobby. </param>
public record PublicLobbyInfo(
    string ConnectionData,
    string Name,
    PublicLobbyType LobbyType,
    string LobbyCode
);


namespace SSMP.Networking.Matchmaking.Protocol;

/// <summary>
/// JSON property names used in MMS request and response bodies.
/// </summary>
internal static class MmsFields {
    /// <summary>The action being performed in a WebSocket message.</summary>
    public const string Action = "action";

    /// <summary>The error code returned in a failed response.</summary>
    public const string ErrorCode = "errorCode";

    /// <summary>A machine-readable failure reason returned in an error or control message.</summary>
    public const string Reason = "reason";

    /// <summary>The version of the protocol or application.</summary>
    public const string Version = "version";

    /// <summary>A general-purpose name field (e.g., player name).</summary>
    public const string Name = "name";

    /// <summary>The current server time in milliseconds.</summary>
    public const string ServerTimeMs = "serverTimeMs";

    /// <summary>The start time of an event in milliseconds.</summary>
    public const string StartTimeMs = "startTimeMs";

    /// <summary>Opaque connection data used for NAT traversal.</summary>
    public const string ConnectionData = "connectionData";

    /// <summary>A unique token identifying a host session.</summary>
    public const string HostToken = "hostToken";

    /// <summary>The display name of the lobby.</summary>
    public const string LobbyName = "lobbyName";

    /// <summary>A short code used to join a specific lobby.</summary>
    public const string LobbyCode = "lobbyCode";

    /// <summary>A token used by the host for discovery purposes.</summary>
    public const string HostDiscoveryToken = "hostDiscoveryToken";

    /// <summary>The type of lobby (e.g., Public, Private).</summary>
    public const string LobbyType = "lobbyType";

    /// <summary>Connection data specifically for LAN connections.</summary>
    public const string LanConnectionData = "lanConnectionData";

    /// <summary>A token used by the client for discovery purposes.</summary>
    public const string ClientDiscoveryToken = "clientDiscoveryToken";

    /// <summary>A unique identifier for a join attempt.</summary>
    public const string JoinId = "joinId";

    /// <summary>The public IP address of the host.</summary>
    public const string HostIp = "hostIp";

    /// <summary>The public port of the host.</summary>
    public const string HostPort = "hostPort";

    /// <summary>The public IP address of the client.</summary>
    public const string ClientIp = "clientIp";

    /// <summary>The public port of the client.</summary>
    public const string ClientPort = "clientPort";

    // Request-specific fields (often used in HTTP POST bodies)

    /// <summary>The port the host is listening on (Request field).</summary>
    public const string HostPortRequest = "HostPort";

    /// <summary>Whether the lobby is public (Request field).</summary>
    public const string IsPublicRequest = "IsPublic";

    /// <summary>The version of the game (Request field).</summary>
    public const string GameVersionRequest = "GameVersion";

    /// <summary>The type of lobby (Request field).</summary>
    public const string LobbyTypeRequest = "LobbyType";

    /// <summary>The LAN IP address of the host (Request field).</summary>
    public const string HostLanIpRequest = "HostLanIp";

    /// <summary>Opaque connection data (Request field).</summary>
    public const string ConnectionDataRequest = "ConnectionData";

    /// <summary>The IP address of the client (Request field).</summary>
    public const string ClientIpRequest = "ClientIp";

    /// <summary>The port of the client (Request field).</summary>
    public const string ClientPortRequest = "ClientPort";

    /// <summary>The version of the matchmaking system (Request field).</summary>
    public const string MatchmakingVersionRequest = "MatchmakingVersion";
}

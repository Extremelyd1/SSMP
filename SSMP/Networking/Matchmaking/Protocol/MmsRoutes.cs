namespace SSMP.Networking.Matchmaking.Protocol;

/// <summary>
/// MMS REST and WebSocket route segments. Use the static helper methods to
/// build parameterised paths; use the constants directly for fixed routes.
/// </summary>
internal static class MmsRoutes {
    /// <summary>MMS health-check and version endpoint.</summary>
    public const string Root = "/health";

    /// <summary>Base path for all lobby operations.</summary>
    public const string Lobby = "/lobby";

    /// <summary>Public lobby listing endpoint.</summary>
    public const string Lobbies = "/lobbies";

    /// <summary>Base path for all WebSocket connections.</summary>
    private const string WebSocketBase = "/ws";

    /// <summary> Builds the URL path for a client to join a specific lobby. </summary>
    /// <param name="lobbyId"> The unique identifier or short code of the lobby. </param>
    /// <returns> The formatted join route. </returns>
    public static string LobbyJoin(string lobbyId) => $"{Lobby}/{lobbyId}/join";

    /// <summary> Builds the URL path for a host to send a heartbeat for its lobby. </summary>
    /// <param name="hostToken"> The token identifying the host session. </param>
    /// <returns> The formatted heartbeat route. </returns>
    public static string LobbyHeartbeat(string hostToken) => $"{Lobby}/heartbeat/{hostToken}";

    /// <summary> Builds the URL path for a host to delete its lobby. </summary>
    /// <param name="hostToken"> The token identifying the host session. </param>
    /// <returns> The formatted delete route. </returns>
    public static string LobbyDelete(string hostToken) => $"{Lobby}/{hostToken}";

    /// <summary> Builds the WebSocket path for a client to connect for matchmaking coordination. </summary>
    /// <param name="joinId"> The unique join attempt identifier. </param>
    /// <returns> The formatted WebSocket join route. </returns>
    public static string JoinWebSocket(string joinId) => $"{WebSocketBase}/join/{joinId}";

    /// <summary> Builds the WebSocket path for a host to connect for matchmaking coordination. </summary>
    /// <param name="hostToken"> The token identifying the host session. </param>
    /// <returns> The formatted WebSocket host route. </returns>
    public static string HostWebSocket(string hostToken) => $"{WebSocketBase}/{hostToken}";
}


using JetBrains.Annotations;

namespace MMS.Contracts;

/// <summary>
/// Response DTOs returned by the MMS HTTP API.
/// </summary>
internal static class Responses {
    /// <summary>
    /// Response payload returned by the health check endpoint.
    /// </summary>
    /// <param name="Service">The service name.</param>
    /// <param name="Version">The current matchmaking protocol version.</param>
    /// <param name="Status">Static health status string (e.g. <c>"healthy"</c>).</param>
    [UsedImplicitly]
    internal record HealthResponse(string Service, int Version, string Status);

    /// <summary>
    /// Response payload returned when a lobby is created.
    /// </summary>
    /// <param name="ConnectionData">Connection identifier (<c>IP:Port</c> or Steam lobby ID).</param>
    /// <param name="HostToken">Secret token required for host operations (heartbeat, close).</param>
    /// <param name="LobbyName">Display name assigned to the lobby.</param>
    /// <param name="LobbyCode">Human-readable invite code (e.g. <c>ABC123</c>).</param>
    /// <param name="HostDiscoveryToken">Token the host sends via UDP so MMS can map its external port.</param>
    [UsedImplicitly]
    internal record CreateLobbyResponse(
        string ConnectionData,
        string HostToken,
        string LobbyName,
        string LobbyCode,
        string? HostDiscoveryToken
    );

    /// <summary>
    /// Response payload returned when listing lobbies.
    /// </summary>
    /// <param name="ConnectionData">Connection identifier (<c>IP:Port</c> or Steam lobby ID).</param>
    /// <param name="Name">Display name.</param>
    /// <param name="LobbyType"><c>"steam"</c> or <c>"matchmaking"</c>.</param>
    /// <param name="LobbyCode">Human-readable invite code.</param>
    [UsedImplicitly]
    internal record LobbyResponse(
        string ConnectionData,
        string Name,
        string LobbyType,
        string LobbyCode
    );

    /// <summary>
    /// Response payload returned for join attempts.
    /// </summary>
    /// <param name="ConnectionData">Host connection data (<c>IP:Port</c> or Steam lobby ID).</param>
    /// <param name="LobbyType"><c>"steam"</c> or <c>"matchmaking"</c>.</param>
    /// <param name="ClientIp">Client public IP as observed by MMS.</param>
    /// <param name="ClientPort">Client public port as observed by MMS.</param>
    /// <param name="LanConnectionData">Host LAN address returned when client and host share a network.</param>
    /// <param name="ClientDiscoveryToken">Token the client sends via UDP so MMS can map its external port.</param>
    /// <param name="JoinId">Identifier for the WebSocket rendezvous session.</param>
    [UsedImplicitly]
    internal record JoinResponse(
        string ConnectionData,
        string LobbyType,
        string ClientIp,
        int ClientPort,
        string? LanConnectionData,
        string? ClientDiscoveryToken,
        string? JoinId
    );

    /// <summary>
    /// Response payload returned when a discovery token has resolved to an external port.
    /// </summary>
    /// <param name="ExternalPort">The external UDP port discovered for the token sender.</param>
    [UsedImplicitly]
    internal record DiscoveryResponse(int ExternalPort);

    /// <summary>
    /// Response payload used for API errors.
    /// </summary>
    /// <param name="Error">Human-readable error description.</param>
    /// <param name="ErrorCode">Optional machine-readable error code (e.g. <c>"update_required"</c>).</param>
    [UsedImplicitly]
    internal record ErrorResponse(string Error, string? ErrorCode = null);

    /// <summary>
    /// Response payload used for simple status responses.
    /// </summary>
    /// <param name="Status">Short status string (e.g. <c>"alive"</c>, <c>"pending"</c>).</param>
    [UsedImplicitly]
    internal record StatusResponse(string Status);
}

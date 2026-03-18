using JetBrains.Annotations;

namespace MMS.Contracts;

/// <summary>
/// Request DTOs accepted by the MMS HTTP API.
/// </summary>
internal static class Requests
{
    /// <summary>
    /// Request payload for lobby creation.
    /// </summary>
    /// <param name="HostIp">Host IP address (matchmaking only, optional - defaults to connection IP).</param>
    /// <param name="HostPort">Host UDP port (matchmaking only).</param>
    /// <param name="ConnectionData">Steam lobby ID (Steam only).</param>
    /// <param name="LobbyType"><c>"steam"</c> or <c>"matchmaking"</c> (default: <c>"matchmaking"</c>).</param>
    /// <param name="HostLanIp">Host LAN address for same-network fast-path discovery.</param>
    /// <param name="IsPublic">Whether the lobby appears in public browser listings (default: <see langword="true"/>).</param>
    /// <param name="MatchmakingVersion">Client matchmaking protocol version for compatibility checks.</param>
    [UsedImplicitly]
    internal record CreateLobbyRequest(
        string? HostIp,
        int? HostPort,
        string? ConnectionData,
        string? LobbyType,
        string? HostLanIp,
        bool? IsPublic,
        int? MatchmakingVersion
    );

    /// <summary>
    /// Request payload for a lobby join attempt.
    /// </summary>
    /// <param name="ClientIp">Client IP override (optional - defaults to the connection's remote IP).</param>
    /// <param name="ClientPort">Client UDP port for NAT hole-punching.</param>
    /// <param name="MatchmakingVersion">Client matchmaking protocol version for compatibility checks.</param>
    [UsedImplicitly]
    internal record JoinLobbyRequest(string? ClientIp, int ClientPort, int? MatchmakingVersion);

    /// <summary>
    /// Request payload for a lobby heartbeat.
    /// </summary>
    /// <param name="ConnectedPlayers">Number of remote players currently connected to the host.</param>
    [UsedImplicitly]
    internal record HeartbeatRequest(int ConnectedPlayers);
}

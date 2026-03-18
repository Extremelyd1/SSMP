using System.Net.WebSockets;
using MMS.Models.Matchmaking;
using MMS.Services.Lobby;
using MMS.Services.Network;
using _Lobby = MMS.Models.Lobby.Lobby;

namespace MMS.Services.Matchmaking;

/// <summary>
/// Sends matchmaking rendezvous messages over client and host WebSockets.
/// Methods returning <see cref="bool"/> distinguish "target missing" from transport exceptions.
/// </summary>
public sealed class JoinSessionMessenger(LobbyService lobbyService) {
    /// <summary>Tells the joining client to begin its UDP mapping phase.</summary>
    public static Task SendBeginClientMappingAsync(JoinSession session, CancellationToken cancellationToken) =>
        SendToJoinClientAsync(
            session,
            new {
                action = "begin_client_mapping",
                joinId = session.JoinId,
                clientDiscoveryToken = session.ClientDiscoveryToken,
                serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            },
            cancellationToken
        );

    /// <summary>
    /// Asks the host to refresh its NAT mapping by re-sending its UDP discovery packet.
    /// Returns <see langword="false"/> if the host is unavailable or missing a discovery token.
    /// </summary>
    public async Task<bool> SendHostRefreshRequestAsync(
        string joinId,
        string lobbyConnectionData,
        CancellationToken cancellationToken
    ) {
        var lobby = lobbyService.GetLobby(lobbyConnectionData);
        if (lobby?.HostWebSocket is not { State: WebSocketState.Open } hostWs ||
            string.IsNullOrEmpty(lobby.HostDiscoveryToken)) {
            return false;
        }

        await WebSocketMessenger.SendAsync(
            hostWs,
            new {
                action = "refresh_host_mapping",
                joinId,
                hostDiscoveryToken = lobby.HostDiscoveryToken,
                serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            },
            cancellationToken
        );
        return true;
    }

    /// <summary>Notifies the joining client that its external UDP port has been observed.</summary>
    public static Task SendClientMappingReceivedAsync(
        JoinSession session,
        int port,
        CancellationToken cancellationToken
    ) =>
        SendToJoinClientAsync(
            session,
            new {
                action = "client_mapping_received",
                clientPort = port,
                serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            },
            cancellationToken
        );

    /// <summary>Notifies the host that its external UDP port has been observed.</summary>
    public static Task SendHostMappingReceivedAsync(_Lobby lobby, int port, CancellationToken cancellationToken) =>
        SendToHostAsync(
            lobby,
            new {
                action = "host_mapping_received",
                hostPort = port,
                serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            },
            cancellationToken
        );

    /// <summary>
    /// Sends a synchronized NAT punch instruction to the joining client.
    /// <paramref name="startTimeMs"/> is the coordinated UTC timestamp (Unix ms) at which both sides punch.
    /// Returns <see langword="false"/> if the client socket is not open.
    /// </summary>
    public static async Task<bool> SendStartPunchToClientAsync(
        JoinSession session,
        int hostPort,
        string hostIp,
        long startTimeMs,
        CancellationToken cancellationToken
    ) {
        if (session.ClientWebSocket is not { State: WebSocketState.Open } ws)
            return false;

        await WebSocketMessenger.SendAsync(
            ws,
            new {
                action = "start_punch",
                joinId = session.JoinId,
                hostIp,
                hostPort,
                startTimeMs
            },
            cancellationToken
        );
        return true;
    }

    /// <summary>
    /// Sends a synchronized NAT punch instruction to the lobby host.
    /// <paramref name="startTimeMs"/> is the coordinated UTC timestamp (Unix ms) at which both sides punch.
    /// Returns <see langword="false"/> if the host socket is not open.
    /// </summary>
    public static async Task<bool> SendStartPunchToHostAsync(
        _Lobby lobby,
        string joinId,
        string clientIp,
        int clientPort,
        int hostPort,
        long startTimeMs,
        CancellationToken cancellationToken
    ) {
        if (lobby.HostWebSocket is not { State: WebSocketState.Open } hostWs)
            return false;

        await WebSocketMessenger.SendAsync(
            hostWs,
            new {
                action = "start_punch",
                joinId,
                clientIp,
                clientPort,
                hostPort,
                startTimeMs
            },
            cancellationToken
        );
        return true;
    }

    /// <summary>Notifies the joining client that the join attempt has failed.</summary>
    public static Task SendJoinFailedToClientAsync(
        JoinSession session,
        string reason,
        CancellationToken cancellationToken
    ) =>
        SendToJoinClientAsync(
            session,
            new { action = "join_failed", joinId = session.JoinId, reason },
            cancellationToken
        );

    /// <summary>Notifies the lobby host that a join attempt has failed.</summary>
    public async Task SendJoinFailedToHostAsync(
        string lobbyConnectionData,
        string joinId,
        string reason,
        CancellationToken cancellationToken
    ) {
        var lobby = lobbyService.GetLobby(lobbyConnectionData);
        if (lobby?.HostWebSocket is not { State: WebSocketState.Open } hostWs)
            return;

        await WebSocketMessenger.SendAsync(
            hostWs,
            new { action = "join_failed", joinId, reason },
            cancellationToken
        );
    }

    /// <summary>Sends <paramref name="payload"/> to the session's client WebSocket, if open.</summary>
    private static Task SendToJoinClientAsync(
        JoinSession session,
        object payload,
        CancellationToken cancellationToken
    ) {
        return session.ClientWebSocket is not { State: WebSocketState.Open } ws
            ? Task.CompletedTask
            : WebSocketMessenger.SendAsync(ws, payload, cancellationToken);
    }

    /// <summary>Sends <paramref name="payload"/> to the lobby's host WebSocket, if open.</summary>
    private static Task SendToHostAsync(
        _Lobby lobby,
        object payload,
        CancellationToken cancellationToken
    ) {
        return lobby.HostWebSocket is not { State: WebSocketState.Open } ws
            ? Task.CompletedTask
            : WebSocketMessenger.SendAsync(ws, payload, cancellationToken);
    }
}

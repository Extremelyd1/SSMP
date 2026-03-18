using System.Net.WebSockets;
using MMS.Models;
using MMS.Models.Matchmaking;
using MMS.Services.Lobby;
using MMS.Services.Utility;
using _Lobby = MMS.Models.Lobby.Lobby;

namespace MMS.Services.Matchmaking;

/// <summary>
/// Coordinates join-session lifecycle, discovery routing, and NAT punch orchestration.
/// </summary>
public sealed class JoinSessionCoordinator(
    JoinSessionStore store,
    JoinSessionMessenger messenger,
    LobbyService lobbyService,
    ILogger<JoinSessionCoordinator> logger
) {
    /// <summary>
    /// Allocates a new join session for a client attempting to connect to <paramref name="lobby"/>.
    /// </summary>
    /// <param name="lobby">The target lobby. Steam lobbies are rejected immediately.</param>
    /// <param name="clientIp">The joining client's IP address, used later for punch coordination.</param>
    /// <returns>
    /// The new <see cref="JoinSession"/>, or <see langword="null"/> for Steam lobbies
    /// </returns>
    public JoinSession? CreateJoinSession(_Lobby lobby, string clientIp) {
        if (lobby.LobbyType.Equals("steam", StringComparison.OrdinalIgnoreCase))
            return null;

        var session = new JoinSession {
            JoinId = TokenGenerator.GenerateToken(32),
            LobbyConnectionData = lobby.ConnectionData,
            ClientIp = clientIp,
            ClientDiscoveryToken = TokenGenerator.GenerateToken(32)
        };

        store.Add(session);
        store.UpsertDiscoveryToken(
            session.ClientDiscoveryToken,
            new DiscoveryTokenMetadata { JoinId = session.JoinId }
        );

        RegisterHostDiscoveryTokenIfAbsent(lobby);

        return session;
    }

    /// <summary>
    /// Returns an active, non-expired session by its identifier.
    /// Expired sessions are cleaned up upon access.
    /// </summary>
    /// <param name="joinId">The join session identifier.</param>
    /// <returns>The session, or <see langword="null"/> if not found or expired.</returns>
    public JoinSession? GetJoinSession(string joinId) {
        if (!store.TryGet(joinId, out var session) || session == null)
            return null;

        if (session.ExpiresAtUtc >= DateTime.UtcNow)
            return session;

        CleanupJoinSession(joinId);
        return null;
    }

    /// <summary>
    /// Associates a WebSocket with an existing session so the server can push events to the client.
    /// </summary>
    /// <param name="joinId">The join session identifier.</param>
    /// <param name="webSocket">The client's WebSocket connection.</param>
    /// <returns><see langword="true"/> if the session was found and the socket was attached.</returns>
    public bool AttachJoinWebSocket(string joinId, WebSocket webSocket) {
        var session = GetJoinSession(joinId);
        if (session == null) return false;

        session.ClientWebSocket = webSocket;
        return true;
    }

    /// <summary>
    /// Records the externally observed UDP port for a discovery token and advances the punch flow.
    /// </summary>
    /// <remarks>
    /// The token determines whether this is a host or client port discovery event and
    /// dispatches to the appropriate handler.
    /// </remarks>
    /// <param name="token">The discovery token included in the UDP packet.</param>
    /// <param name="port">The external port observed by the server.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    public async Task SetDiscoveredPortAsync(string token, int port, CancellationToken cancellationToken = default) {
        if (!store.TryGetDiscoveryMetadata(token, out var metadata) || metadata == null)
            return;

        if (!store.TrySetDiscoveredPort(token, port))
            return;

        if (metadata.HostConnectionData != null) {
            await HandleHostPortDiscoveredAsync(metadata.HostConnectionData, port, cancellationToken);
            return;
        }

        if (metadata.JoinId != null)
            await HandleClientPortDiscoveredAsync(metadata.JoinId, port, cancellationToken);
    }

    /// <summary>
    /// Returns the externally observed UDP port for a discovery token, or
    /// <see langword="null"/> if the port has not yet been recorded.
    /// </summary>
    /// <param name="token">The discovery token to query.</param>
    public int? GetDiscoveredPort(string token) => store.GetDiscoveredPort(token);

    /// <summary>
    /// Sends the <c>begin_client_mapping</c> message to the client identified by <paramref name="joinId"/>.
    /// </summary>
    /// <param name="joinId">The join session identifier.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    public Task SendBeginClientMappingAsync(string joinId, CancellationToken cancellationToken) {
        var session = GetJoinSession(joinId);
        return session == null
            ? Task.CompletedTask
            : JoinSessionMessenger.SendBeginClientMappingAsync(session, cancellationToken);
    }

    /// <summary>
    /// Asks the host to refresh its NAT mapping for the given join session.
    /// </summary>
    /// <param name="joinId">The join session identifier used to locate the lobby.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// <see langword="true"/> if the refresh message was dispatched successfully.
    /// </returns>
    public async Task<bool> SendHostRefreshRequestAsync(string joinId, CancellationToken cancellationToken) {
        var session = GetJoinSession(joinId);
        return session != null &&
               await messenger.SendHostRefreshRequestAsync(joinId, session.LobbyConnectionData, cancellationToken);
    }

    /// <summary>
    /// Fails a join session: notifies both the client and the host, then cleans up the session.
    /// </summary>
    /// <param name="joinId">The join session identifier.</param>
    /// <param name="reason">A short machine-readable failure reason (e.g. <c>"host_unreachable"</c>).</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    public async Task FailJoinSessionAsync(
        string joinId,
        string reason,
        CancellationToken cancellationToken = default
    ) {
        var session = GetJoinSession(joinId);
        if (session == null) return;

        try {
            await JoinSessionMessenger.SendJoinFailedToClientAsync(session, reason, cancellationToken);
        } catch (Exception ex) when (IsSocketSendFailure(ex)) {
            logger.LogDebug(ex, "Failed to notify join client for {JoinId}", joinId);
        }

        try {
            await messenger.SendJoinFailedToHostAsync(session.LobbyConnectionData, joinId, reason, cancellationToken);
        } catch (Exception ex) when (IsSocketSendFailure(ex)) {
            logger.LogDebug(ex, "Failed to notify host about join failure for {JoinId}", joinId);
        }

        CleanupJoinSession(joinId);
    }

    /// <summary>
    /// Removes all sessions that have passed their expiry time and purges stale discovery tokens.
    /// </summary>
    /// <remarks>
    /// Discovery tokens are considered stale after 2 minutes, giving them a longer
    /// grace period than sessions to handle timing edge cases.
    /// </remarks>
    public void CleanupExpiredSessions() {
        var now = DateTime.UtcNow;

        foreach (var joinId in store.GetExpiredJoinIds(now))
            CleanupJoinSession(joinId);

        foreach (var token in store.GetExpiredDiscoveryTokens(now.AddMinutes(-2)))
            store.RemoveDiscoveryToken(token);
    }

    /// <summary>
    /// Removes all sessions belonging to <paramref name="lobby"/> and its host discovery token.
    /// Called when a lobby is closed or evicted.
    /// </summary>
    /// <param name="lobby">The lobby being removed.</param>
    public void CleanupSessionsForLobby(_Lobby lobby) {
        foreach (var joinId in store.GetJoinIdsForLobby(lobby.ConnectionData))
            CleanupJoinSession(joinId);

        if (!string.IsNullOrEmpty(lobby.HostDiscoveryToken))
            store.RemoveDiscoveryToken(lobby.HostDiscoveryToken);
    }

    /// <summary>
    /// Registers the host discovery token for a lobby if it is not already tracked.
    /// This ensures the server can correlate the host's UDP packet with the correct lobby.
    /// </summary>
    private void RegisterHostDiscoveryTokenIfAbsent(_Lobby lobby) {
        if (lobby.HostDiscoveryToken == null || store.ContainsDiscoveryToken(lobby.HostDiscoveryToken))
            return;

        store.UpsertDiscoveryToken(
            lobby.HostDiscoveryToken,
            new DiscoveryTokenMetadata { HostConnectionData = lobby.ConnectionData }
        );
    }

    /// <summary>
    /// Handles a UDP port discovery event originating from the host side.
    /// Records the host's external port and attempts to advance any pending client sessions.
    /// </summary>
    private async Task HandleHostPortDiscoveredAsync(
        string lobbyConnectionData,
        int port,
        CancellationToken cancellationToken
    ) {
        var lobby = lobbyService.GetLobby(lobbyConnectionData);
        if (lobby == null) return;

        lobby.ExternalPort = port;
        await JoinSessionMessenger.SendHostMappingReceivedAsync(lobby, port, cancellationToken);
        await TryStartPendingJoinSessionsAsync(lobbyConnectionData, cancellationToken);
    }

    /// <summary>
    /// Handles a UDP port discovery event originating from the client side.
    /// Records the client's external port, requests a host refresh, and attempts to start punching.
    /// </summary>
    private async Task HandleClientPortDiscoveredAsync(
        string joinId,
        int port,
        CancellationToken cancellationToken
    ) {
        var session = GetJoinSession(joinId);
        if (session == null) return;

        session.ClientExternalPort = port;
        await JoinSessionMessenger.SendClientMappingReceivedAsync(session, port, cancellationToken);

        var hostRefreshed = await messenger.SendHostRefreshRequestAsync(
            joinId,
            session.LobbyConnectionData,
            cancellationToken
        );

        if (!hostRefreshed) {
            await FailJoinSessionAsync(joinId, "host_unreachable", cancellationToken);
            return;
        }

        await TryStartJoinSessionAsync(joinId, cancellationToken);
    }

    /// <summary>
    /// Attempts to start all client sessions waiting on the given lobby.
    /// Called after the host's external port becomes known.
    /// </summary>
    private async Task TryStartPendingJoinSessionsAsync(
        string lobbyConnectionData,
        CancellationToken cancellationToken
    ) {
        foreach (var joinId in store.GetJoinIdsForLobby(lobbyConnectionData))
            await TryStartJoinSessionAsync(joinId, cancellationToken);
    }

    /// <summary>
    /// Attempts to issue synchronized <c>start_punch</c> messages to both sides.
    /// </summary>
    /// <remarks>
    /// The host is notified first so a late host disconnect cannot leave the client punching alone.
    /// Any socket failure while dispatching the pair is converted into a join failure.
    /// </remarks>
    private async Task TryStartJoinSessionAsync(string joinId, CancellationToken cancellationToken) {
        var session = GetJoinSession(joinId);
        if (session?.ClientExternalPort == null) return;

        var lobby = lobbyService.GetLobby(session.LobbyConnectionData);
        if (lobby == null) {
            await FailJoinSessionAsync(joinId, "lobby_closed", cancellationToken);
            return;
        }

        if (lobby.HostWebSocket is not { State: WebSocketState.Open }) {
            await FailJoinSessionAsync(joinId, "host_unreachable", cancellationToken);
            return;
        }

        if (session.ClientWebSocket is not { State: WebSocketState.Open }) {
            await FailJoinSessionAsync(joinId, "client_disconnected", cancellationToken);
            return;
        }

        // Host port is not yet known so we wait for the next host discovery event.
        if (lobby.ExternalPort == null) return;

        // Matchmaking lobbies store ConnectionData as "IP:Port".
        // Steam lobbies are filtered out at session creation.
        var hostIp = lobby.ConnectionData.Split(':')[0];
        var startTimeMs = DateTimeOffset.UtcNow
                                        .AddMilliseconds(MatchmakingProtocol.PunchTimingOffsetMs)
                                        .ToUnixTimeMilliseconds();

        try {
            var hostSent = await JoinSessionMessenger.SendStartPunchToHostAsync(
                lobby,
                joinId,
                session.ClientIp,
                session.ClientExternalPort.Value,
                lobby.ExternalPort.Value,
                startTimeMs,
                cancellationToken
            );
            if (!hostSent) {
                await FailJoinSessionAsync(joinId, "host_unreachable", cancellationToken);
                return;
            }

            var clientSent = await JoinSessionMessenger.SendStartPunchToClientAsync(
                session,
                lobby.ExternalPort.Value,
                hostIp,
                startTimeMs,
                cancellationToken
            );
            if (!clientSent) {
                await FailJoinSessionAsync(joinId, "client_disconnected", cancellationToken);
                return;
            }
        } catch (Exception ex) when (IsSocketSendFailure(ex)) {
            logger.LogDebug(ex, "Failed to dispatch start_punch for join {JoinId}", joinId);
            await FailJoinSessionAsync(joinId, "host_unreachable", cancellationToken);
            return;
        }

        CleanupJoinSession(joinId);
    }

    /// <summary>
    /// Removes a session and its client discovery token from the store,
    /// then performs a best-effort close of the client WebSocket.
    /// </summary>
    private void CleanupJoinSession(string joinId) {
        if (!store.Remove(joinId, out var session) || session == null) return;

        store.RemoveDiscoveryToken(session.ClientDiscoveryToken);

        if (session.ClientWebSocket is { State: WebSocketState.Open } ws)
            _ = CloseJoinSocketAsync(ws, joinId);
    }

    /// <summary>
    /// Attempts a graceful close of the client rendezvous socket using a bounded timeout.
    /// </summary>
    /// <param name="webSocket">The client WebSocket to close.</param>
    /// <param name="joinId">Join identifier used for debug logging.</param>
    private async Task CloseJoinSocketAsync(WebSocket webSocket, string joinId) {
        try {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "join complete", timeoutCts.Token);
        } catch (Exception ex) when (IsSocketSendFailure(ex) || ex is OperationCanceledException) {
            logger.LogDebug(ex, "Failed to close client join WebSocket for join {JoinId}", joinId);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> for exceptions that commonly mean the peer disappeared during send/close.
    /// </summary>
    /// <param name="ex">The exception raised by a socket operation.</param>
    private static bool IsSocketSendFailure(Exception ex) =>
        ex is WebSocketException or ObjectDisposedException;
}

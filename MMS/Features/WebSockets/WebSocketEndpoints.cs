using System.Net.Sockets;
using System.Net.WebSockets;
using MMS.Bootstrap;
using MMS.Features.Matchmaking;
using MMS.Http;
using MMS.Models;
using MMS.Models.Matchmaking;
using MMS.Services.Lobby;
using MMS.Services.Matchmaking;
using MMS.Services.Utility;
using static MMS.Contracts.Responses;

namespace MMS.Features.WebSockets;

/// <summary>
/// Maps WebSocket endpoints used by hosts and matchmaking join sessions.
/// </summary>
internal static class WebSocketEndpoints {
    /// <summary>
    /// Maps all MMS WebSocket endpoints onto the provided route group builders.
    /// </summary>
    /// <param name="webSockets">The grouped route builder for <c>/ws</c> routes.</param>
    /// <param name="joinWebSockets">The grouped route builder for <c>/ws/join</c> routes.</param>
    public static void MapWebSocketEndpoints(
        RouteGroupBuilder webSockets,
        RouteGroupBuilder joinWebSockets
    ) {
        webSockets.Endpoint()
                  .Map("/{token}")
                  .Handler(HandleHostWebSocketAsync)
                  .Build();

        joinWebSockets.Endpoint()
                      .Map("/{joinId}")
                      .Handler(HandleJoinWebSocketAsync)
                      .Build();
    }

    /// <summary>
    /// Handles the persistent host WebSocket used for push notifications.
    /// Keeps the socket open until the host closes the connection or disconnects.
    /// </summary>
    private static async Task HandleHostWebSocketAsync(
        HttpContext context,
        string token,
        LobbyService lobbyService
    ) {
        if (!context.WebSockets.IsWebSocketRequest) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var lobby = lobbyService.GetLobbyByToken(token);
        if (lobby == null) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var previousSocket = lobby.HostWebSocket;
        if (previousSocket != null && !ReferenceEquals(previousSocket, webSocket))
            await CloseReplacedHostSocketAsync(previousSocket, PrivacyFormatter.Format(lobby.ConnectionData), context.RequestAborted);

        lobby.HostWebSocket = webSocket;

        ProgramState.Logger.LogInformation(
            "[WS] Host connected for lobby {LobbyIdentifier}",
            PrivacyFormatter.Format(lobby.ConnectionData)
        );

        try {
            await DrainHostWebSocketAsync(webSocket, PrivacyFormatter.Format(lobby.ConnectionData));
        } finally {
            if (ReferenceEquals(lobby.HostWebSocket, webSocket))
                lobby.HostWebSocket = null;

            ProgramState.Logger.LogInformation(
                "[WS] Host disconnected from lobby {LobbyIdentifier}",
                PrivacyFormatter.Format(lobby.ConnectionData)
            );
        }
    }

    /// <summary>
    /// Reads and discards incoming frames until the socket closes or the connection is reset.
    /// The host WebSocket is receive-only; all meaningful communication is server-to-host push.
    /// </summary>
    /// <param name="webSocket">The accepted host WebSocket.</param>
    /// <param name="lobbyIdentifier">The lobby identifier, used for diagnostic logging on unexpected disconnects.</param>
    private static async Task DrainHostWebSocketAsync(WebSocket webSocket, string lobbyIdentifier) {
        var buffer = new byte[1024];
        try {
            while (webSocket.State == WebSocketState.Open) {
                var result = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        } catch (WebSocketException ex) {
            ProgramState.Logger.LogDebug(
                ex, "[WS] Host socket closed unexpectedly for lobby {LobbyIdentifier}", lobbyIdentifier
            );
            // Host disconnected without a proper close handshake.
        } catch (Exception ex) when (ex.InnerException is SocketException) {
            ProgramState.Logger.LogDebug(ex, "[WS] Host socket reset for lobby {LobbyIdentifier}", lobbyIdentifier);
            // Connection was forcibly reset during shutdown or game exit.
        }
    }

    /// <summary>
    /// Attempts a graceful close of a host WebSocket that has been superseded by a newer connection,
    /// falling back to an abort if the socket is not in a closeable state or if the close fails.
    /// </summary>
    /// <param name="previousSocket">The WebSocket to close and dispose.</param>
    /// <param name="lobbyIdentifier">The lobby identifier, used for diagnostic logging on failure.</param>
    /// <param name="cancellationToken">A token to cancel the close handshake.</param>
    private static async Task CloseReplacedHostSocketAsync(
        WebSocket previousSocket,
        string lobbyIdentifier,
        CancellationToken cancellationToken
    ) {
        try {
            if (previousSocket.State is WebSocketState.Open or WebSocketState.CloseReceived) {
                await previousSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Replaced by newer host connection",
                    cancellationToken
                );
            } else {
                previousSocket.Abort();
            }
        } catch (Exception ex) {
            ProgramState.Logger.LogDebug(
                ex,
                "[WS] Failed to close replaced host socket for lobby {LobbyIdentifier}",
                lobbyIdentifier
            );
            previousSocket.Abort();
        } finally {
            previousSocket.Dispose();
        }
    }

    /// <summary>
    /// Handles the short-lived matchmaking WebSocket used during join rendezvous.
    /// Validates the request, attaches the socket to the session, orchestrates the
    /// host reachability check, then waits for the client to disconnect.
    /// </summary>
    private static async Task HandleJoinWebSocketAsync(
        HttpContext context,
        string joinId,
        LobbyService lobbyService,
        JoinSessionService joinService
    ) {
        if (!context.WebSockets.IsWebSocketRequest) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!ValidateMatchmakingVersion(context))
            return;

        var session = joinService.GetJoinSession(joinId);
        if (session == null) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        if (!joinService.AttachJoinWebSocket(joinId, webSocket)) {
            await webSocket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation,
                "join session not found",
                context.RequestAborted
            );
            return;
        }

        await joinService.SendBeginClientMappingAsync(joinId, context.RequestAborted);

        if (!await EnsureHostReachableAsync(context, joinId, session, lobbyService, joinService)) {
            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived) {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Host unreachable",
                    context.RequestAborted
                );
            }

            return;
        }

        await DrainJoinWebSocketAsync(webSocket, joinId, joinService, context.RequestAborted);
    }

    /// <summary>
    /// Validates the <c>matchmakingVersion</c> query parameter.
    /// Writes a <c>426 Upgrade Required</c> response if validation fails.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>
    /// <see langword="true"/> if the version is acceptable; otherwise <see langword="false"/>.
    /// </returns>
    private static bool ValidateMatchmakingVersion(HttpContext context) {
        if (MatchmakingVersionValidation.TryValidate(context.Request.Query["matchmakingVersion"]))
            return true;

        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        context.Response
               .WriteAsJsonAsync(
                   new ErrorResponse(
                       "Please update to the latest version in order to use matchmaking!",
                       MatchmakingProtocol.UpdateRequiredErrorCode
                   ),
                   context.RequestAborted
               )
               .GetAwaiter()
               .GetResult();
        return false;
    }

    /// <summary>
    /// Verifies that the host WebSocket for the session's lobby is reachable,
    /// and requests a port refresh if the lobby has no known external port.
    /// Fails the join session if the host cannot be reached.
    /// </summary>
    /// <param name="context">The current HTTP context, used for cancellation.</param>
    /// <param name="joinId">The join session identifier.</param>
    /// <param name="session">The resolved join session.</param>
    /// <param name="lobbyService">The lobby service for host lookup.</param>
    /// <param name="joinService">The join session service for signaling.</param>
    /// <returns>
    /// <see langword="true"/> if the host is reachable and the session may proceed;
    /// otherwise <see langword="false"/>.
    /// </returns>
    private static async Task<bool> EnsureHostReachableAsync(
        HttpContext context,
        string joinId,
        JoinSession session,
        LobbyService lobbyService,
        JoinSessionService joinService
    ) {
        var lobby = lobbyService.GetLobby(session.LobbyConnectionData);
        if (lobby?.HostWebSocket is not { State: WebSocketState.Open }) {
            await joinService.FailJoinSessionAsync(joinId, "host_unreachable", context.RequestAborted);
            return false;
        }

        if (lobby.ExternalPort != null)
            return true;

        var refreshSent = await joinService.SendHostRefreshRequestAsync(joinId, context.RequestAborted);
        if (!refreshSent) {
            await joinService.FailJoinSessionAsync(joinId, "host_unreachable", context.RequestAborted);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads and discards incoming frames on the join WebSocket until the client disconnects
    /// or the request is canceled. Fails the session as <c>client_disconnected</c> on exit
    /// if the session is still active.
    /// </summary>
    /// <param name="webSocket">The accepted join WebSocket.</param>
    /// <param name="joinId">The join session identifier.</param>
    /// <param name="joinService">The join session service used to fail the session on disconnect.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    private static async Task DrainJoinWebSocketAsync(
        WebSocket webSocket,
        string joinId,
        JoinSessionService joinService,
        CancellationToken cancellationToken
    ) {
        var buffer = new byte[256];
        try {
            while (webSocket.State == WebSocketState.Open) {
                var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        } catch (OperationCanceledException) {
            // Request was canceled so session cleanup is handled in finally.
        } catch (WebSocketException) {
            // Client disconnected without a proper close handshake.
        } finally {
            if (joinService.GetJoinSession(joinId) != null)
                await joinService.FailJoinSessionAsync(joinId, "client_disconnected", CancellationToken.None);
        }
    }

}

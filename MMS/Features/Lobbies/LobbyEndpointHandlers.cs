using System.Net;
using Microsoft.AspNetCore.Http.HttpResults;
using MMS.Bootstrap;
using MMS.Features.Matchmaking;
using MMS.Models;
using MMS.Models.Lobby;
using MMS.Services.Lobbies;
using MMS.Services.Matchmaking;
using MMS.Services.Utility;
using static MMS.Contracts.Requests;
using static MMS.Contracts.Responses;

// ReSharper disable once CheckNamespace
namespace MMS.Features.Lobbies;

/// <summary>
/// Contains handler and validation logic for lobby-oriented MMS endpoints.
/// Pair this with <see cref="LobbyEndpoints"/>, which owns route registration
/// and delegates each mapped route to the handlers in this class.
/// </summary>
internal static class LobbyEndpointHandlers {
    /// <summary>
    /// Returns all lobbies, optionally filtered by type.
    /// </summary>
    internal static Ok<IEnumerable<LobbyResponse>> GetLobbies(LobbyService lobbyService, string? type = null) {
        var lobbies = lobbyService.GetLobbies(type)
                                  .Select(l => new LobbyResponse(
                                          l.AdvertisedConnectionData,
                                          l.LobbyName,
                                          l.LobbyType,
                                          l.LobbyCode
                                      )
                                  );
        return TypedResults.Ok(lobbies);
    }

    /// <summary>
    /// Creates a new lobby (Steam or Matchmaking).
    /// </summary>
    internal static IResult CreateLobby(
        CreateLobbyRequest request,
        LobbyService lobbyService,
        LobbyNameService lobbyNameService,
        HttpContext context
    ) {
        var lobbyType = request.LobbyType ?? "matchmaking";

        if (string.Equals(lobbyType, "matchmaking", StringComparison.OrdinalIgnoreCase) &&
            !MatchmakingVersionValidation.Validate(request.MatchmakingVersion))
            return MatchmakingOutdatedResult();

        if (!TryResolveConnectionData(request, lobbyType, context, out var connectionData, out var error))
            return error!;

        var lobbyName = lobbyNameService.GenerateLobbyName();
        var lobby = lobbyService.CreateLobby(
            connectionData,
            lobbyName,
            lobbyType,
            request.HostLanIp,
            request.IsPublic ?? true
        );

        ProgramState.Logger.LogInformation(
            "[LOBBY] Created: '{LobbyName}' [{LobbyType}] ({Visibility}) -> {ConnectionData} (Code: {LobbyCode})",
            lobby.LobbyName,
            lobby.LobbyType,
            lobby.IsPublic ? "Public" : "Private",
            PrivacyFormatter.Format(lobby.AdvertisedConnectionData),
            lobby.LobbyCode
        );

        return TypedResults.Created(
            $"/lobby/{lobby.LobbyCode}",
            new CreateLobbyResponse(
                lobby.AdvertisedConnectionData,
                lobby.HostToken,
                lobby.LobbyName,
                lobby.LobbyCode,
                lobby.HostDiscoveryToken
            )
        );
    }

    /// <summary>
    /// Returns the externally discovered port for a discovery token when available.
    /// </summary>
    /// <remarks>
    /// Retained for compatibility. The active matchmaking client flow uses the WebSocket
    /// rendezvous instead of polling this endpoint.
    /// </remarks>
    internal static IResult VerifyDiscovery(string token, JoinSessionService joinService) {
        var port = joinService.GetDiscoveredPort(token);
        return port is null
            ? TypedResults.Ok(new StatusResponse("pending"))
            : TypedResults.Ok(new DiscoveryResponse(port.Value));
    }

    /// <summary>
    /// Closes a lobby by host token.
    /// </summary>
    internal static Results<NoContent, NotFound<ErrorResponse>> CloseLobby(
        string token,
        LobbyService lobbyService,
        JoinSessionService joinService
    ) {
        if (!lobbyService.RemoveLobbyByToken(token, joinService.CleanupSessionsForLobby))
            return TypedResults.NotFound(new ErrorResponse("Lobby not found"));

        ProgramState.Logger.LogInformation("[LOBBY] Closed by host");
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Refreshes the lobby heartbeat to prevent expiration.
    /// </summary>
    internal static Results<Ok<StatusResponse>, NotFound<ErrorResponse>> Heartbeat(
        string token,
        HeartbeatRequest request,
        LobbyService lobbyService
    ) {
        return lobbyService.Heartbeat(token, request.ConnectedPlayers)
            ? TypedResults.Ok(new StatusResponse("alive"))
            : TypedResults.NotFound(new ErrorResponse("Lobby not found"));
    }

    /// <summary>
    /// Registers a client join attempt, returning host connection info and rendezvous metadata.
    /// </summary>
    internal static IResult JoinLobby(
        string connectionData,
        JoinLobbyRequest request,
        LobbyService lobbyService,
        JoinSessionService joinService,
        HttpContext context
    ) {
        var lobby = lobbyService.GetLobbyByCode(connectionData) ?? lobbyService.GetLobby(connectionData);
        if (lobby == null)
            return TypedResults.NotFound(new ErrorResponse("Lobby not found"));

        if (string.Equals(lobby.LobbyType, "matchmaking", StringComparison.OrdinalIgnoreCase) &&
            !MatchmakingVersionValidation.Validate(request.MatchmakingVersion))
            return MatchmakingOutdatedResult();

        if (!TryResolveClientAddress(request, context, out var clientIp, out var clientIpError))
            return clientIpError!;

        ProgramState.Logger.LogInformation(
            "[JOIN] {ClientEndPoint} -> {LobbyEndPoint}",
            $"{PrivacyFormatter.Format(clientIp)}:{request.ClientPort}",
            PrivacyFormatter.Format(lobby.AdvertisedConnectionData)
        );

        var lanConnectionData = TryResolveLanConnectionData(lobby, clientIp);

        if (!string.Equals(lobby.LobbyType, "matchmaking", StringComparison.OrdinalIgnoreCase)) {
            return TypedResults.Ok(
                new JoinResponse(
                    lobby.AdvertisedConnectionData,
                    lobby.LobbyType,
                    clientIp,
                    request.ClientPort,
                    lanConnectionData,
                    null,
                    null
                )
            );
        }

        var session = joinService.CreateJoinSession(lobby, clientIp);
        if (session == null)
            return TypedResults.NotFound(new ErrorResponse("Lobby not found"));

        return TypedResults.Ok(
            new JoinResponse(
                lobby.AdvertisedConnectionData,
                lobby.LobbyType,
                clientIp,
                request.ClientPort,
                lanConnectionData,
                session.ClientDiscoveryToken,
                session.JoinId
            )
        );
    }

    /// <summary>
    /// Resolves the <c>connectionData</c> string for a lobby being created.
    /// </summary>
    /// <param name="request">The create-lobby request.</param>
    /// <param name="lobbyType">The resolved lobby type.</param>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="connectionData">
    /// When this method returns <see langword="true"/>, contains the resolved connection data string.
    /// Otherwise, <see cref="string.Empty"/>.
    /// </param>
    /// <param name="error">
    /// When this method returns <see langword="false"/>, contains the validation error result to return to the caller.
    /// Otherwise, <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when connection data was resolved successfully; otherwise <see langword="false"/>.
    /// </returns>
    private static bool TryResolveConnectionData(
        CreateLobbyRequest request,
        string lobbyType,
        HttpContext context,
        out string connectionData,
        out IResult? error
    ) {
        connectionData = string.Empty;
        error = null;

        if (string.Equals(lobbyType, "steam", StringComparison.OrdinalIgnoreCase)) {
            if (string.IsNullOrEmpty(request.ConnectionData)) {
                error = TypedResults.BadRequest(new ErrorResponse("Steam lobby requires ConnectionData"));
                return false;
            }

            connectionData = request.ConnectionData;
            return true;
        }

        var rawHostIp = request.HostIp ?? context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrEmpty(rawHostIp) || !IPAddress.TryParse(rawHostIp, out var parsedHostIp)) {
            error = TypedResults.BadRequest(new ErrorResponse("Invalid IP address"));
            return false;
        }

        if (request.HostPort is null or <= 0 or > 65535) {
            error = TypedResults.BadRequest(new ErrorResponse("Invalid port number"));
            return false;
        }

        connectionData = $"{parsedHostIp}:{request.HostPort}";
        return true;
    }

    /// <summary>
    /// Resolves and validates the client IP address for a join request.
    /// </summary>
    private static bool TryResolveClientAddress(
        JoinLobbyRequest request,
        HttpContext context,
        out string clientIp,
        out IResult? error
    ) {
        clientIp = string.Empty;
        error = null;

        var rawClientIp = request.ClientIp ?? context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrEmpty(rawClientIp) || !IPAddress.TryParse(rawClientIp, out var parsedIp)) {
            error = TypedResults.BadRequest(new ErrorResponse("Invalid IP address"));
            return false;
        }

        if (request.ClientPort is <= 0 or > 65535) {
            error = TypedResults.BadRequest(new ErrorResponse("Invalid port"));
            return false;
        }

        clientIp = parsedIp.ToString();
        return true;
    }

    /// <summary>
    /// Returns the host LAN address when the joining client shares the host's WAN IP.
    /// </summary>
    private static string? TryResolveLanConnectionData(Lobby lobby, string clientIp) {
        if (string.IsNullOrEmpty(lobby.HostLanIp))
            return null;

        var hostWanIp = lobby.ConnectionData.Split(':')[0];
        if (clientIp != hostWanIp)
            return null;

        ProgramState.Logger.LogInformation(
            "[JOIN] Local network detected - returning LAN IP: {HostLanIp}",
            PrivacyFormatter.Format(lobby.HostLanIp)
        );

        return lobby.HostLanIp;
    }

    /// <summary>
    /// Returns a bad request result indicating the client's matchmaking version is outdated.
    /// </summary>
    private static IResult MatchmakingOutdatedResult() =>
        TypedResults.BadRequest(
            new ErrorResponse(
                "Please update to the latest version in order to use matchmaking!",
                MatchmakingProtocol.UpdateRequiredErrorCode
            )
        );

}

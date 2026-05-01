using MMS.Http;

// ReSharper disable once CheckNamespace
namespace MMS.Features.Lobbies;

/// <summary>
/// Maps lobby-oriented MMS HTTP endpoints.
/// Pair this with <see cref="LobbyEndpointHandlers"/>, which contains the
/// corresponding handler and validation logic for the routes defined here.
/// </summary>
internal static class LobbyEndpoints {
    /// <summary>
    /// Maps lobby management and matchmaking HTTP endpoints.
    /// </summary>
    /// <param name="app">The web application to map non-lobby-root endpoints onto.</param>
    /// <param name="lobby">The grouped route builder for <c>/lobby</c> routes.</param>
    public static void MapLobbyEndpoints(this WebApplication app, RouteGroupBuilder lobby) {
        app.Endpoint()
           .Get("/lobbies")
           .Handler(LobbyEndpointHandlers.GetLobbies)
           .WithName("ListLobbies")
           .RequireRateLimiting("search")
           .Build();

        lobby.Endpoint()
             .Post("")
             .Handler(LobbyEndpointHandlers.CreateLobby)
             .WithName("CreateLobby")
             .RequireRateLimiting("create")
             .Build();

        lobby.Endpoint()
             .Delete("/{token}")
             .Handler(LobbyEndpointHandlers.CloseLobby)
             .WithName("CloseLobby")
             .Build();

        lobby.Endpoint()
             .Post("/heartbeat/{token}")
             .Handler(LobbyEndpointHandlers.Heartbeat)
             .WithName("Heartbeat")
             .Build();

        lobby.Endpoint()
             .Post("/discovery/verify/{token}")
             .Handler(LobbyEndpointHandlers.VerifyDiscovery)
             .WithName("VerifyDiscovery")
             .Build();

        lobby.Endpoint()
             .Post("/{connectionData}/join")
             .Handler(LobbyEndpointHandlers.JoinLobby)
             .WithName("JoinLobby")
             .RequireRateLimiting("join")
             .Build();
    }
}

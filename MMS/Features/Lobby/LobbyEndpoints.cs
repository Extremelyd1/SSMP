using MMS.Http;

namespace MMS.Features.Lobby;

/// <summary>
/// Maps lobby-oriented MMS HTTP endpoints.
/// </summary>
internal static partial class LobbyEndpoints
{
    /// <summary>
    /// Maps lobby management and matchmaking HTTP endpoints.
    /// </summary>
    /// <param name="app">The web application to map non-lobby-root endpoints onto.</param>
    /// <param name="lobby">The grouped route builder for <c>/lobby</c> routes.</param>
    public static void MapLobbyEndpoints(this WebApplication app, RouteGroupBuilder lobby)
    {
        app.Endpoint()
           .Get("/lobbies")
           .Handler(GetLobbies)
           .WithName("ListLobbies")
           .RequireRateLimiting("search")
           .Build();

        lobby.Endpoint()
             .Post("")
             .Handler(CreateLobby)
             .WithName("CreateLobby")
             .RequireRateLimiting("create")
             .Build();

        lobby.Endpoint()
             .Delete("/{token}")
             .Handler(CloseLobby)
             .WithName("CloseLobby")
             .Build();

        lobby.Endpoint()
             .Post("/heartbeat/{token}")
             .Handler(Heartbeat)
             .WithName("Heartbeat")
             .Build();

        lobby.Endpoint()
             .Post("/discovery/verify/{token}")
             .Handler(VerifyDiscovery)
             .WithName("VerifyDiscovery")
             .Build();

        lobby.Endpoint()
             .Post("/{connectionData}/join")
             .Handler(JoinLobby)
             .WithName("JoinLobby")
             .RequireRateLimiting("join")
             .Build();
    }
}

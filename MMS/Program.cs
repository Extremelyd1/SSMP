#pragma warning disable CS1587 // XML comment is not placed on a valid language element

using JetBrains.Annotations;
using MMS.Services;
using Microsoft.AspNetCore.Http.HttpResults;

/// <summary>
/// MatchMaking Service (MMS) API entry point.
/// Provides lobby management and NAT hole-punching coordination for peer-to-peer gaming.
/// </summary>
var builder = WebApplication.CreateBuilder(args);

ConfigureServices(builder.Services);

var app = builder.Build();

ConfigureMiddleware(app);
ConfigureEndpoints(app);
app.Urls.Add("http://0.0.0.0:5000");

app.Run();

#region Configuration

/// <summary>
/// Configures dependency injection services.
/// </summary>
static void ConfigureServices(IServiceCollection services) {
    // Singleton lobby service maintains all active lobbies in memory
    services.AddSingleton<LobbyService>();

    // Background service cleans up expired lobbies every 60 seconds
    services.AddHostedService<LobbyCleanupService>();
}

/// <summary>
/// Configures middleware pipeline.
/// </summary>
static void ConfigureMiddleware(WebApplication app) {
    // Add exception handling in production
    if (!app.Environment.IsDevelopment()) {
        app.UseExceptionHandler("/error");
    }
}

/// <summary>
/// Configures HTTP endpoints for the MMS API.
/// </summary>
static void ConfigureEndpoints(WebApplication app) {
    MapHealthEndpoints(app);
    MapLobbyEndpoints(app);
    MapHostEndpoints(app);
    MapClientEndpoints(app);
}

#endregion

#region Health & Monitoring

/// <summary>
/// Maps health check and monitoring endpoints.
/// </summary>
static void MapHealthEndpoints(WebApplication app) {
    // Root health check
    app.MapGet(
           "/", () => Results.Ok(
               new {
                   service = "MMS MatchMaking Service",
                   version = "1.0",
                   status = "healthy"
               }
           )
       )
       .WithName("HealthCheck");

    // List all active lobbies (debugging)
    app.MapGet("/lobbies", GetAllLobbies)
       .WithName("ListLobbies");
}

/// <summary>
/// Gets all active lobbies for monitoring.
/// </summary>
static Ok<IEnumerable<LobbyInfoResponse>> GetAllLobbies(LobbyService lobbyService) {
    var lobbies = lobbyService.GetAllLobbies()
                              .Select(l => new LobbyInfoResponse(l.Id, l.HostIp, l.HostPort));
    return TypedResults.Ok(lobbies);
}

#endregion

#region Lobby Management

/// <summary>
/// Maps lobby creation and query endpoints.
/// </summary>
static void MapLobbyEndpoints(WebApplication app) {
    // Create new lobby
    app.MapPost("/lobby", CreateLobby)
       .WithName("CreateLobby");

    // Get lobby by public ID
    app.MapGet("/lobby/{id}", GetLobby)
       .WithName("GetLobby");

    // Get lobby by host token
    app.MapGet("/lobby/mine/{token}", GetMyLobby)
       .WithName("GetMyLobby");

    // Close lobby
    app.MapDelete("/lobby/{token}", CloseLobby)
       .WithName("CloseLobby");
}

/// <summary>
/// Creates a new lobby with the provided host endpoint.
/// </summary>
static Created<CreateLobbyResponse> CreateLobby(
    CreateLobbyRequest request,
    LobbyService lobbyService,
    HttpContext context
) {
    // Extract host IP from request or connection
    var hostIp = GetIpAddress(request.HostIp, context);

    // Validate port number
    if (request.HostPort <= 0 || request.HostPort > 65535) {
        return TypedResults.Created(
            $"/lobby/invalid",
            new CreateLobbyResponse("error", "Invalid port number")
        );
    }

    // Create lobby
    var lobby = lobbyService.CreateLobby(hostIp, request.HostPort);

    Console.WriteLine($"[LOBBY] Created: {lobby.Id} -> {lobby.HostIp}:{lobby.HostPort}");

    return TypedResults.Created(
        $"/lobby/{lobby.Id}",
        new CreateLobbyResponse(lobby.Id, lobby.HostToken)
    );
}

/// <summary>
/// Gets public lobby information by ID.
/// </summary>
static Results<Ok<LobbyInfoResponse>, NotFound<ErrorResponse>> GetLobby(
    string id,
    LobbyService lobbyService
) {
    var lobby = lobbyService.GetLobby(id);
    if (lobby == null) {
        return TypedResults.NotFound(new ErrorResponse("Lobby not found or offline"));
    }

    return TypedResults.Ok(new LobbyInfoResponse(lobby.Id, lobby.HostIp, lobby.HostPort));
}

/// <summary>
/// Gets lobby information using host token.
/// </summary>
static Results<Ok<LobbyInfoResponse>, NotFound<ErrorResponse>> GetMyLobby(
    string token,
    LobbyService lobbyService
) {
    var lobby = lobbyService.GetLobbyByToken(token);
    if (lobby == null) {
        return TypedResults.NotFound(new ErrorResponse("Lobby not found"));
    }

    return TypedResults.Ok(new LobbyInfoResponse(lobby.Id, lobby.HostIp, lobby.HostPort));
}

/// <summary>
/// Closes a lobby using the host token.
/// </summary>
static Results<NoContent, NotFound<ErrorResponse>> CloseLobby(
    string token,
    LobbyService lobbyService
) {
    if (lobbyService.RemoveLobbyByToken(token)) {
        Console.WriteLine($"[LOBBY] Closed by host");
        return TypedResults.NoContent();
    }

    return TypedResults.NotFound(new ErrorResponse("Lobby not found"));
}

#endregion

#region Host Operations

/// <summary>
/// Maps host-specific endpoints.
/// </summary>
static void MapHostEndpoints(WebApplication app) {
    // Heartbeat to keep lobby alive
    app.MapPost("/lobby/heartbeat/{token}", Heartbeat)
       .WithName("Heartbeat");

    // Get pending clients for hole-punching
    app.MapGet("/lobby/pending/{token}", GetPendingClients)
       .WithName("GetPendingClients");
}

/// <summary>
/// Updates lobby heartbeat timestamp.
/// </summary>
static Results<Ok<StatusResponse>, NotFound<ErrorResponse>> Heartbeat(
    string token,
    LobbyService lobbyService
) {
    if (lobbyService.Heartbeat(token)) {
        return TypedResults.Ok(new StatusResponse("alive"));
    }

    return TypedResults.NotFound(new ErrorResponse("Lobby not found"));
}

/// <summary>
/// Gets and clears pending clients for NAT hole-punching.
/// </summary>
static Results<Ok<List<PendingClientResponse>>, NotFound<ErrorResponse>> GetPendingClients(
    string token,
    LobbyService lobbyService
) {
    var lobby = lobbyService.GetLobbyByToken(token);    
    if (lobby == null) {
        return TypedResults.NotFound(new ErrorResponse("Lobby not found"));
    }

    var pending = new List<PendingClientResponse>();
    var cutoffTime = DateTime.UtcNow.AddSeconds(-30);

    // Dequeue and filter clients by age (30 second window)
    while (lobby.PendingClients.TryDequeue(out var client)) {
        if (client.RequestedAt >= cutoffTime) {
            pending.Add(new PendingClientResponse(client.ClientIp, client.ClientPort));
        }
    }

    return TypedResults.Ok(pending);
}

#endregion

#region Client Operations

/// <summary>
/// Maps client-specific endpoints.
/// </summary>
static void MapClientEndpoints(WebApplication app) {
    // Join lobby (queue for hole-punching)
    app.MapPost("/lobby/{id}/join", JoinLobby)
       .WithName("JoinLobby");
}

/// <summary>
/// Queues a client for NAT hole-punching coordination.
/// </summary>
static Results<Ok<JoinResponse>, NotFound<ErrorResponse>> JoinLobby(
    string id,
    JoinLobbyRequest request,
    LobbyService lobbyService,
    HttpContext context
) {
    var lobby = lobbyService.GetLobby(id);
    if (lobby == null) {
        return TypedResults.NotFound(new ErrorResponse("Lobby not found or offline"));
    }

    // Extract client IP from request or connection
    var clientIp = GetIpAddress(request.ClientIp, context);

    // Validate port number
    if (request.ClientPort <= 0 || request.ClientPort > 65535) {
        return TypedResults.NotFound(new ErrorResponse("Invalid port number"));
    }

    // Queue client for host to punch back
    lobby.PendingClients.Enqueue(
        new MMS.Models.PendingClient(clientIp, request.ClientPort, DateTime.UtcNow)
    );

    Console.WriteLine($"[JOIN] {clientIp}:{request.ClientPort} queued for lobby {lobby.Id}");

    return TypedResults.Ok(new JoinResponse(lobby.HostIp, lobby.HostPort, clientIp, request.ClientPort));
}

#endregion

#region Helper Methods

/// <summary>
/// Extracts IP address from request or HTTP context.
/// </summary>
static string GetIpAddress(string? providedIp, HttpContext context) {
    if (!string.IsNullOrWhiteSpace(providedIp)) {
        return providedIp;
    }

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

#endregion

#region Data Transfer Objects

/// <summary>
/// Request to create a new lobby.
/// </summary>
record CreateLobbyRequest(string? HostIp, int HostPort);

/// <summary>
/// Response containing new lobby information.
/// </summary>
record CreateLobbyResponse([UsedImplicitly] string LobbyId, string HostToken);

/// <summary>
/// Public lobby information.
/// </summary>
record LobbyInfoResponse([UsedImplicitly] string Id, string HostIp, int HostPort);

/// <summary>
/// Request to join a lobby.
/// </summary>
record JoinLobbyRequest([UsedImplicitly] string? ClientIp, int ClientPort);

/// <summary>
/// Response containing connection information after joining.
/// </summary>
record JoinResponse([UsedImplicitly] string HostIp, int HostPort, string ClientIp, int ClientPort);

/// <summary>
/// Pending client information for hole-punching.
/// </summary>
record PendingClientResponse([UsedImplicitly] string ClientIp, int ClientPort);

/// <summary>
/// Generic error response.
/// </summary>
record ErrorResponse([UsedImplicitly] string Error);

/// <summary>
/// Generic status response.
/// </summary>
record StatusResponse([UsedImplicitly] string Status);

#endregion

using MMS.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<LobbyService>();
builder.Services.AddHostedService<LobbyCleanupService>();

var app = builder.Build();

// Health check
app.MapGet("/", () => "MMS MatchMaking Service v1.0");

// Create lobby - returns ID and secret host token
app.MapPost("/lobby", (CreateLobbyRequest request, LobbyService lobbyService, HttpContext context) => {
    var hostIp = request.HostIp ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var lobby = lobbyService.CreateLobby(hostIp, request.HostPort);
    
    Console.WriteLine($"[LOBBY] Created: {lobby.Id} -> {lobby.HostIp}:{lobby.HostPort}");
    
    // Return ID (public) and token (secret, only for host)
    return Results.Created($"/lobby/{lobby.Id}", new CreateLobbyResponse(lobby.Id, lobby.HostToken));
});

// Get lobby by ID (public info only)
app.MapGet("/lobby/{id}", (string id, LobbyService lobbyService) => {
    var lobby = lobbyService.GetLobby(id);
    if (lobby == null) {
        return Results.NotFound(new { error = "Lobby not found or offline" });
    }
    
    return Results.Ok(new LobbyInfoResponse(lobby.Id, lobby.HostIp, lobby.HostPort));
});

// Get my lobby (host uses token to find their own lobby)
app.MapGet("/lobby/mine/{token}", (string token, LobbyService lobbyService) => {
    var lobby = lobbyService.GetLobbyByToken(token);
    if (lobby == null) {
        return Results.NotFound(new { error = "Lobby not found" });
    }
    
    return Results.Ok(new LobbyInfoResponse(lobby.Id, lobby.HostIp, lobby.HostPort));
});

// Heartbeat - host calls this every 30s to stay alive
app.MapPost("/lobby/heartbeat/{token}", (string token, LobbyService lobbyService) => {
    if (lobbyService.Heartbeat(token)) {
        return Results.Ok(new { status = "alive" });
    }
    return Results.NotFound(new { error = "Lobby not found" });
});

// Close lobby (host uses token)
app.MapDelete("/lobby/{token}", (string token, LobbyService lobbyService) => {
    if (lobbyService.RemoveLobbyByToken(token)) {
        Console.WriteLine($"[LOBBY] Closed by host");
        return Results.NoContent();
    }
    return Results.NotFound(new { error = "Lobby not found" });
});

// List all lobbies (for debugging/browsing)
app.MapGet("/lobbies", (LobbyService lobbyService) => {
    var lobbies = lobbyService.GetAllLobbies()
        .Select(l => new LobbyInfoResponse(l.Id, l.HostIp, l.HostPort));
    return Results.Ok(lobbies);
});

// Client requests to join - queues for host to punch back
app.MapPost("/lobby/{id}/join", (string id, JoinLobbyRequest request, LobbyService lobbyService, HttpContext context) => {
    var lobby = lobbyService.GetLobby(id);
    if (lobby == null) {
        return Results.NotFound(new { error = "Lobby not found or offline" });
    }
    
    var clientIp = request.ClientIp ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    
    // Queue client for host to punch
    lobby.PendingClients.Enqueue(new MMS.Models.PendingClient(clientIp, request.ClientPort, DateTime.UtcNow));
    
    Console.WriteLine($"[JOIN] {clientIp}:{request.ClientPort} queued for {lobby.Id}");
    
    return Results.Ok(new JoinResponse(lobby.HostIp, lobby.HostPort, clientIp, request.ClientPort));
});

// Host polls for pending clients that need punch-back
app.MapGet("/lobby/pending/{token}", (string token, LobbyService lobbyService) => {
    var lobby = lobbyService.GetLobbyByToken(token);
    if (lobby == null) {
        return Results.NotFound(new { error = "Lobby not found" });
    }
    
    var pending = new List<PendingClientResponse>();
    while (lobby.PendingClients.TryDequeue(out var client)) {
        // Only include clients from last 30 seconds
        if (DateTime.UtcNow - client.RequestedAt < TimeSpan.FromSeconds(30)) {
            pending.Add(new PendingClientResponse(client.ClientIp, client.ClientPort));
        }
    }
    
    return Results.Ok(pending);
});

app.Run();

// Request/Response DTOs
record CreateLobbyRequest(string? HostIp, int HostPort);
record CreateLobbyResponse(string LobbyId, string HostToken);
record LobbyInfoResponse(string Id, string HostIp, int HostPort);
record JoinLobbyRequest(string? ClientIp, int ClientPort);
record JoinResponse(string HostIp, int HostPort, string ClientIp, int ClientPort);
record PendingClientResponse(string ClientIp, int ClientPort);

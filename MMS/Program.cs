#pragma warning disable CS1587 // XML comment is not placed on a valid language element

using System.Net.WebSockets;
using System.Text;
using JetBrains.Annotations;
using MMS.Services;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => {
        options.SingleLine = true;
        options.IncludeScopes = false;
        options.TimestampFormat = "HH:mm:ss ";
    }
);

builder.Services.AddSingleton<LobbyService>();
builder.Services.AddHostedService<LobbyCleanupService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/error");
}

app.UseWebSockets();
MapEndpoints(app);
app.Urls.Add("http://0.0.0.0:5000");
app.Run();

#region Endpoint Registration

static void MapEndpoints(WebApplication app) {
    var lobbyService = app.Services.GetRequiredService<LobbyService>();

    // Health & Monitoring
    app.MapGet("/", () => Results.Ok(new { service = "MMS", version = "1.0", status = "healthy" }))
       .WithName("HealthCheck");
    app.MapGet("/lobbies", GetLobbies).WithName("ListLobbies");

    // Lobby Management
    app.MapPost("/lobby", CreateLobby).WithName("CreateLobby");
    app.MapGet("/lobby/{connectionData}", GetLobby).WithName("GetLobby");
    app.MapGet("/lobby/mine/{token}", GetMyLobby).WithName("GetMyLobby");
    app.MapDelete("/lobby/{token}", CloseLobby).WithName("CloseLobby");

    // Host Operations
    app.MapPost("/lobby/heartbeat/{token}", Heartbeat).WithName("Heartbeat");
    app.MapGet("/lobby/pending/{token}", GetPendingClients).WithName("GetPendingClients");

    // WebSocket for host push notifications
    app.Map("/ws/{token}", async (HttpContext context, string token) => {
        if (!context.WebSockets.IsWebSocketRequest) {
            context.Response.StatusCode = 400;
            return;
        }

        var lobby = lobbyService.GetLobbyByToken(token);
        if (lobby == null) {
            context.Response.StatusCode = 404;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        lobby.HostWebSocket = webSocket;
        Console.WriteLine($"[WS] Host connected for lobby {lobby.ConnectionData}");

        // Keep connection alive until closed
        var buffer = new byte[1024];
        try {
            while (webSocket.State == WebSocketState.Open) {
                var result = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        } catch (WebSocketException) {
            // Host disconnected without proper close handshake (normal during game exit)
        } catch (Exception ex) when (ex.InnerException is System.Net.Sockets.SocketException) {
            // Connection forcibly reset (normal during game exit)
        } finally {
            lobby.HostWebSocket = null;
            Console.WriteLine($"[WS] Host disconnected from lobby {lobby.ConnectionData}");
        }
    });

    // Client Operations
    app.MapPost("/lobby/{connectionData}/join", JoinLobby).WithName("JoinLobby");
}

#endregion

#region Endpoint Handlers

/// <summary>
/// Returns all lobbies, optionally filtered by type.
/// </summary>
static Ok<IEnumerable<LobbyResponse>> GetLobbies(LobbyService lobbyService, string? type = null) {
    var lobbies = lobbyService.GetLobbies(type)
                              .Select(l => new LobbyResponse(
                                      l.ConnectionData,
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
static Created<CreateLobbyResponse> CreateLobby(
    CreateLobbyRequest request,
    LobbyService lobbyService,
    HttpContext context
) {
    var lobbyType = request.LobbyType ?? "matchmaking";
    string connectionData;

    if (lobbyType == "steam") {
        if (string.IsNullOrEmpty(request.ConnectionData)) {
            return TypedResults.Created(
                "/lobby/invalid",
                new CreateLobbyResponse("error", "Steam lobby requires ConnectionData", "")
            );
        }

        connectionData = request.ConnectionData;
    } else {
        var hostIp = request.HostIp ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (request.HostPort is null or <= 0 or > 65535) {
            return TypedResults.Created(
                "/lobby/invalid",
                new CreateLobbyResponse("error", "Invalid port number", "")
            );
        }

        connectionData = $"{hostIp}:{request.HostPort}";
    }

    var lobby = lobbyService.CreateLobby(
        connectionData,
        request.LobbyName ?? "Unnamed Lobby",
        lobbyType,
        request.HostLanIp,
        request.IsPublic ?? true
    );

    var visibility = lobby.IsPublic ? "Public" : "Private";
    Console.WriteLine($"[LOBBY] Created: '{lobby.LobbyName}' [{lobby.LobbyType}] ({visibility}) -> {lobby.ConnectionData} (Code: {lobby.LobbyCode})");
    return TypedResults.Created($"/lobby/{lobby.ConnectionData}", new CreateLobbyResponse(lobby.ConnectionData, lobby.HostToken, lobby.LobbyCode));
}

/// <summary>
/// Gets lobby info by ConnectionData.
/// </summary>
static Results<Ok<LobbyResponse>, NotFound<ErrorResponse>> GetLobby(string connectionData, LobbyService lobbyService) {
    // Try as lobby code first, then as connectionData
    var lobby = lobbyService.GetLobbyByCode(connectionData) ?? lobbyService.GetLobby(connectionData);
    return lobby == null
        ? TypedResults.NotFound(new ErrorResponse("Lobby not found"))
        : TypedResults.Ok(new LobbyResponse(lobby.ConnectionData, lobby.LobbyName, lobby.LobbyType, lobby.LobbyCode));
}

/// <summary>
/// Gets lobby info by host token.
/// </summary>
static Results<Ok<LobbyResponse>, NotFound<ErrorResponse>> GetMyLobby(string token, LobbyService lobbyService) {
    var lobby = lobbyService.GetLobbyByToken(token);
    return lobby == null
        ? TypedResults.NotFound(new ErrorResponse("Lobby not found"))
        : TypedResults.Ok(new LobbyResponse(lobby.ConnectionData, lobby.LobbyName, lobby.LobbyType, lobby.LobbyCode));
}



/// <summary>
/// Closes a lobby by host token.
/// </summary>
static Results<NoContent, NotFound<ErrorResponse>> CloseLobby(string token, LobbyService lobbyService) {
    if (!lobbyService.RemoveLobbyByToken(token)) {
        return TypedResults.NotFound(new ErrorResponse("Lobby not found"));
    }

    Console.WriteLine("[LOBBY] Closed by host");
    return TypedResults.NoContent();
}

/// <summary>
/// Refreshes lobby heartbeat to prevent expiration.
/// </summary>
static Results<Ok<StatusResponse>, NotFound<ErrorResponse>> Heartbeat(string token, LobbyService lobbyService) {
    return lobbyService.Heartbeat(token)
        ? TypedResults.Ok(new StatusResponse("alive"))
        : TypedResults.NotFound(new ErrorResponse("Lobby not found"));
}

/// <summary>
/// Returns pending clients waiting for NAT hole-punch (clears the queue).
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
    var cutoff = DateTime.UtcNow.AddSeconds(-30);

    while (lobby.PendingClients.TryDequeue(out var client)) {
        if (client.RequestedAt >= cutoff) {
            pending.Add(new PendingClientResponse(client.ClientIp, client.ClientPort));
        }
    }

    return TypedResults.Ok(pending);
}

/// <summary>
/// Notifies host of pending client and returns host connection info.
/// Uses WebSocket push if available, otherwise queues for polling.
/// </summary>
static async Task<Results<Ok<JoinResponse>, NotFound<ErrorResponse>>> JoinLobby(
    string connectionData,
    JoinLobbyRequest request,
    LobbyService lobbyService,
    HttpContext context
) {
    // Try as lobby code first, then as connectionData
    var lobby = lobbyService.GetLobbyByCode(connectionData) ?? lobbyService.GetLobby(connectionData);
    if (lobby == null) {
        return TypedResults.NotFound(new ErrorResponse("Lobby not found"));
    }

    var clientIp = request.ClientIp ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    if (request.ClientPort is <= 0 or > 65535) {
        return TypedResults.NotFound(new ErrorResponse("Invalid port"));
    }

    Console.WriteLine($"[JOIN] {clientIp}:{request.ClientPort} -> {lobby.ConnectionData}");

    // Try WebSocket push first (instant notification)
    if (lobby.HostWebSocket is { State: WebSocketState.Open }) {
        var message = $"{{\"clientIp\":\"{clientIp}\",\"clientPort\":{request.ClientPort}}}";
        var bytes = Encoding.UTF8.GetBytes(message);
        await lobby.HostWebSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        Console.WriteLine($"[WS] Pushed client to host via WebSocket");
    } else {
        // Fallback to queue for polling (legacy clients)
        lobby.PendingClients.Enqueue(new MMS.Models.PendingClient(clientIp, request.ClientPort, DateTime.UtcNow));
    }

    // Check if client is on the same network as the host
    var joinConnectionData = lobby.ConnectionData;
    
    // We can only check IP equality if we have the host's IP (for matchmaking lobbies mainly)
    // NOTE: This assumes lobby.ConnectionData is in "IP:Port" format for matchmaking
    if (!string.IsNullOrEmpty(lobby.HostLanIp)) {
        // Parse Host Public IP from ConnectionData (format: "IP:Port")
        var hostPublicIp = lobby.ConnectionData.Split(':')[0];
        
        if (clientIp == hostPublicIp) {
            Console.WriteLine($"[JOIN] Local Network Detected! Returning LAN IP: {lobby.HostLanIp}");
            joinConnectionData = lobby.HostLanIp;
        }
    }

    return TypedResults.Ok(new JoinResponse(joinConnectionData, lobby.LobbyType, clientIp, request.ClientPort));
}

#endregion

#region DTOs

/// <param name="HostIp">Host IP (Matchmaking only, optional).</param>
/// <param name="HostPort">Host port (Matchmaking only).</param>
/// <param name="ConnectionData">Steam lobby ID (Steam only).</param>
/// <param name="LobbyName">Display name for the lobby.</param>
/// <param name="LobbyType">"steam" or "matchmaking" (default: matchmaking).</param>
/// <param name="HostLanIp">Host LAN IP for local network discovery.</param>
/// <param name="IsPublic">Whether lobby appears in browser (default: true).</param>
record CreateLobbyRequest(
    string? HostIp,
    int? HostPort,
    string? ConnectionData,
    string? LobbyName,
    string? LobbyType,
    string? HostLanIp,
    bool? IsPublic
);

/// <param name="ConnectionData">Connection identifier (IP:Port or Steam lobby ID).</param>
/// <param name="HostToken">Secret token for host operations.</param>
/// <param name="LobbyCode">Human-readable invite code.</param>
record CreateLobbyResponse([UsedImplicitly] string ConnectionData, string HostToken, string LobbyCode);

/// <param name="ConnectionData">Connection identifier (IP:Port or Steam lobby ID).</param>
/// <param name="Name">Display name.</param>
/// <param name="LobbyType">"steam" or "matchmaking".</param>
/// <param name="LobbyCode">Human-readable invite code.</param>
record LobbyResponse(
    [UsedImplicitly] string ConnectionData,
    string Name,
    string LobbyType,
    string LobbyCode
);

/// <param name="ClientIp">Client IP (optional - uses connection IP if null).</param>
/// <param name="ClientPort">Client's local port for hole-punching.</param>
record JoinLobbyRequest([UsedImplicitly] string? ClientIp, int ClientPort);

/// <param name="ConnectionData">Host connection data (IP:Port or Steam lobby ID).</param>
/// <param name="LobbyType">"steam" or "matchmaking".</param>
/// <param name="ClientIp">Client's public IP as seen by MMS.</param>
/// <param name="ClientPort">Client's public port.</param>
record JoinResponse([UsedImplicitly] string ConnectionData, string LobbyType, string ClientIp, int ClientPort);

/// <param name="ClientIp">Pending client's IP.</param>
/// <param name="ClientPort">Pending client's port.</param>
record PendingClientResponse([UsedImplicitly] string ClientIp, int ClientPort);

/// <param name="Error">Error message.</param>
record ErrorResponse([UsedImplicitly] string Error);

/// <param name="Status">Status message.</param>
record StatusResponse([UsedImplicitly] string Status);

#endregion

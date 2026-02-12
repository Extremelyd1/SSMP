using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using JetBrains.Annotations;
using MMS.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.HttpOverrides;

namespace MMS;

/// <summary>
/// Main class for the MatchMaking Server.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class Program {
    /// <summary>
    /// Whether we are running a development environment.
    /// </summary>
    private static bool IsDevelopment { get; set; }
    
    /// <summary>
    /// The logger for logging information to the console.
    /// </summary>
    private static ILogger Logger { get; set; } = null!;

    /// <summary>
    /// Entrypoint for the MMS.
    /// </summary>
    public static void Main(string[] args) {
        var builder = WebApplication.CreateBuilder(args);

        IsDevelopment = builder.Environment.IsDevelopment();

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => {
                options.SingleLine = true;
                options.IncludeScopes = false;
                options.TimestampFormat = "HH:mm:ss ";
            }
        );

        builder.Services.AddSingleton<LobbyService>();
        builder.Services.AddSingleton<LobbyNameService>();
        builder.Services.AddHostedService<LobbyCleanupService>();

        builder.Services.Configure<ForwardedHeadersOptions>(options => {
            options.ForwardedHeaders = 
                ForwardedHeaders.XForwardedFor | 
                ForwardedHeaders.XForwardedHost |
                ForwardedHeaders.XForwardedProto;
        });
        
        if (IsDevelopment) {
            builder.Services.AddHttpLogging(_ => { });
        } else {
            if (!ConfigureHttpsCertificate(builder)) {
                return;
            }
        }

        var app = builder.Build();

        Logger = app.Logger;

        if (IsDevelopment) {
            app.UseHttpLogging();
        } else {
            app.UseExceptionHandler("/error");
        }

        app.UseForwardedHeaders();
        app.UseWebSockets();
        MapEndpoints(app);
        app.Urls.Add(IsDevelopment ? "http://0.0.0.0:5000" : "https://0.0.0.0:5000");
        app.Run();
    }

    #region Web Application Initialization

    /// <summary>
    /// Tries to configure HTTPS by reading an SSL certificate and enabling HTTPS when the web application is built.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>True if the certificate could be read, false otherwise.</returns>
    private static bool ConfigureHttpsCertificate(WebApplicationBuilder builder) {
        if (!File.Exists("cert.pem")) {
            Console.WriteLine("Certificate file 'cert.pem' does not exist");
            return false;
        }
            
        if (!File.Exists("key.pem")) {
            Console.WriteLine("Certificate key file 'key.pem' does not exist");
            return false;
        }

        string pem;
        string key;
        try {
            pem = File.ReadAllText("cert.pem");
            key = File.ReadAllText("key.pem");
        } catch (Exception e) {
            Console.WriteLine($"Could not read either 'cert.pem' or 'key.pem':\n{e}");
            return false;
        }

        X509Certificate2 x509;
        try {
            x509 = X509Certificate2.CreateFromPem(pem, key);
        } catch (CryptographicException e) {
            Console.WriteLine($"Could not create certificate object from pem files:\n{e}");
            return false;
        }

        builder.WebHost.ConfigureKestrel(s => {
                s.ListenAnyIP(
                    5000, options => {
                        options.UseHttps(x509);
                    }
                );
            }
        );

        return true;
    }
    
    #endregion

    #region Endpoint Registration

    private static void MapEndpoints(WebApplication app) {
        var lobbyService = app.Services.GetRequiredService<LobbyService>();

        // Health & Monitoring
        app.MapGet("/", () => Results.Ok(new { service = "MMS", version = "1.0", status = "healthy" }))
           .WithName("HealthCheck");
        app.MapGet("/lobbies", GetLobbies).WithName("ListLobbies");

        // Lobby Management
        app.MapPost("/lobby", CreateLobby).WithName("CreateLobby");
        app.MapDelete("/lobby/{token}", CloseLobby).WithName("CloseLobby");

        // Host Operations
        app.MapPost("/lobby/heartbeat/{token}", Heartbeat).WithName("Heartbeat");
        app.MapGet("/lobby/pending/{token}", GetPendingClients).WithName("GetPendingClients");

        // WebSocket for host push notifications
        app.Map(
            "/ws/{token}", async (HttpContext context, string token) => {
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

                Logger.LogInformation(
                    "[WS] Host connected for lobby {}", 
                    IsDevelopment ? lobby.ConnectionData : lobby.LobbyName
                );

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
                    Logger.LogInformation(
                        "[WS] Host disconnected from lobby {}", 
                        IsDevelopment ? lobby.ConnectionData : lobby.LobbyName
                    );
                }
            }
        );

        // Client Operations
        app.MapPost("/lobby/{connectionData}/join", JoinLobby).WithName("JoinLobby");
    }

    #endregion

    #region Endpoint Handlers

    /// <summary>
    /// Returns all lobbies, optionally filtered by type.
    /// </summary>
    private static Ok<IEnumerable<LobbyResponse>> GetLobbies(LobbyService lobbyService, string? type = null) {
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
    private static Results<Created<CreateLobbyResponse>, BadRequest<ErrorResponse>> CreateLobby(
        CreateLobbyRequest request,
        LobbyService lobbyService,
        LobbyNameService lobbyNameService,
        HttpContext context
    ) {
        var lobbyType = request.LobbyType ?? "matchmaking";
        string connectionData;

        if (lobbyType == "steam") {
            if (string.IsNullOrEmpty(request.ConnectionData)) {
                return TypedResults.BadRequest(new ErrorResponse("Steam lobby requires ConnectionData"));
            }

            connectionData = request.ConnectionData;
        } else {
            var rawHostIp = request.HostIp ?? context.Connection.RemoteIpAddress?.ToString();
            if (string.IsNullOrEmpty(rawHostIp) || !IPAddress.TryParse(rawHostIp, out var parsedHostIp)) {
                return TypedResults.BadRequest(new ErrorResponse("Invalid IP address"));
            }

            var hostIp = parsedHostIp.ToString();
            if (request.HostPort is null or <= 0 or > 65535) {
                return TypedResults.BadRequest(new ErrorResponse("Invalid port number"));
            }

            connectionData = $"{hostIp}:{request.HostPort}";
        }

        var lobbyName = lobbyNameService.GenerateLobbyName();

        var lobby = lobbyService.CreateLobby(
            connectionData,
            lobbyName,
            lobbyType,
            request.HostLanIp,
            request.IsPublic ?? true
        );

        var visibility = lobby.IsPublic ? "Public" : "Private";
        var connectionDataString = IsDevelopment ? lobby.ConnectionData : "[Redacted]";
        Logger.LogInformation(
            "[LOBBY] Created: '{LobbyName}' [{LobbyType}] ({Visibility}) -> {ConnectionDataString} (Code: {LobbyCode})", 
            lobby.LobbyName, 
            lobby.LobbyType, 
            visibility, 
            connectionDataString, 
            lobby.LobbyCode
        );

        return TypedResults.Created(
            $"/lobby/{lobby.LobbyCode}",
            new CreateLobbyResponse(lobby.ConnectionData, lobby.HostToken, lobby.LobbyName, lobby.LobbyCode)
        );
    }

    /// <summary>
    /// Closes a lobby by host token.
    /// </summary>
    private static Results<NoContent, NotFound<ErrorResponse>> CloseLobby(string token, LobbyService lobbyService) {
        if (!lobbyService.RemoveLobbyByToken(token)) {
            return TypedResults.NotFound(new ErrorResponse("Lobby not found"));
        }

        Logger.LogInformation("[LOBBY] Closed by host");
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Refreshes lobby heartbeat to prevent expiration.
    /// </summary>
    private static Results<Ok<StatusResponse>, NotFound<ErrorResponse>> Heartbeat(string token, LobbyService lobbyService) {
        return lobbyService.Heartbeat(token)
            ? TypedResults.Ok(new StatusResponse("alive"))
            : TypedResults.NotFound(new ErrorResponse("Lobby not found"));
    }

    /// <summary>
    /// Returns pending clients waiting for NAT hole-punch (clears the queue).
    /// </summary>
    private static Results<Ok<List<PendingClientResponse>>, NotFound<ErrorResponse>> GetPendingClients(
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
    private static async Task<Results<Ok<JoinResponse>, NotFound<ErrorResponse>>> JoinLobby(
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

        var rawClientIp = request.ClientIp ?? context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrEmpty(rawClientIp) || !IPAddress.TryParse(rawClientIp, out var parsedIp)) {
            return TypedResults.NotFound(new ErrorResponse("Invalid IP address"));
        }

        var clientIp = parsedIp.ToString();

        if (request.ClientPort is <= 0 or > 65535) {
            return TypedResults.NotFound(new ErrorResponse("Invalid port"));
        }

        Logger.LogInformation(
            "[JOIN] {}", 
            IsDevelopment ? 
                $"{clientIp}:{request.ClientPort} -> {lobby.ConnectionData}" :
                $"[Redacted]:{request.ClientPort} -> [Redacted]"
        );

        // Try WebSocket push first (instant notification)
        if (lobby.HostWebSocket is { State: WebSocketState.Open }) {
            var message = $"{{\"clientIp\":\"{clientIp}\",\"clientPort\":{request.ClientPort}}}";
            var bytes = Encoding.UTF8.GetBytes(message);
            await lobby.HostWebSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            Logger.LogInformation("[WS] Pushed client to host via WebSocket");
        } else {
            // Fallback to queue for polling (legacy clients)
            lobby.PendingClients.Enqueue(
                new Models.PendingClient(clientIp, request.ClientPort, DateTime.UtcNow)
            );
        }

        // Check if client is on the same network as the host
        var joinConnectionData = lobby.ConnectionData;
        string? lanConnectionData = null;

        // We can only check IP equality if we have the host's IP (for matchmaking lobbies mainly)
        // NOTE: This assumes lobby.ConnectionData is in "IP:Port" format for matchmaking
        if (!string.IsNullOrEmpty(lobby.HostLanIp)) {
            // Parse Host Public IP from ConnectionData (format: "IP:Port")
            var hostPublicIp = lobby.ConnectionData.Split(':')[0];

            if (clientIp == hostPublicIp) {
                Logger.LogInformation("[JOIN] Local Network Detected! Returning LAN IP: {}", lobby.HostLanIp);
                lanConnectionData = lobby.HostLanIp;
            }
        }

        return TypedResults.Ok(new JoinResponse(joinConnectionData, lobby.LobbyType, clientIp, request.ClientPort, lanConnectionData));
    }

    #endregion

    #region DTOs

    /// <param name="HostIp">Host IP (Matchmaking only, optional).</param>
    /// <param name="HostPort">Host port (Matchmaking only).</param>
    /// <param name="ConnectionData">Steam lobby ID (Steam only).</param>
    /// <param name="LobbyType">"steam" or "matchmaking" (default: matchmaking).</param>
    /// <param name="HostLanIp">Host LAN IP for local network discovery.</param>
    /// <param name="IsPublic">Whether lobby appears in browser (default: true).</param>
    [UsedImplicitly]
    private record CreateLobbyRequest(
        string? HostIp,
        int? HostPort,
        string? ConnectionData,
        string? LobbyType,
        string? HostLanIp,
        bool? IsPublic
    );

    /// <param name="ConnectionData">Connection identifier (IP:Port or Steam lobby ID).</param>
    /// <param name="HostToken">Secret token for host operations.</param>
    /// <param name="LobbyName">Name for the lobby.</param>
    /// <param name="LobbyCode">Human-readable invite code.</param>
    [UsedImplicitly]
    internal record CreateLobbyResponse(
        string ConnectionData,
        string HostToken,
        string LobbyName,
        string LobbyCode
    );

    /// <param name="ConnectionData">Connection identifier (IP:Port or Steam lobby ID).</param>
    /// <param name="Name">Display name.</param>
    /// <param name="LobbyType">"steam" or "matchmaking".</param>
    /// <param name="LobbyCode">Human-readable invite code.</param>
    [UsedImplicitly]
    internal record LobbyResponse(
        string ConnectionData,
        string Name,
        string LobbyType,
        string LobbyCode
    );

    /// <param name="ClientIp">Client IP (optional - uses connection IP if null).</param>
    /// <param name="ClientPort">Client's local port for hole-punching.</param>
    [UsedImplicitly]
    internal record JoinLobbyRequest(string? ClientIp, int ClientPort);

    /// <param name="ConnectionData">Host connection data (IP:Port or Steam lobby ID).</param>
    /// <param name="LobbyType">"steam" or "matchmaking".</param>
    /// <param name="ClientIp">Client's public IP as seen by MMS.</param>
    /// <param name="ClientPort">Client's public port.</param>
    /// <param name="LanConnectionData">Host's LAN connection data in case LAN is detected.</param>
    [UsedImplicitly]
    internal record JoinResponse(
        string ConnectionData,
        string LobbyType,
        string ClientIp,
        int ClientPort,
        string? LanConnectionData
    );

    /// <param name="ClientIp">Pending client's IP.</param>
    /// <param name="ClientPort">Pending client's port.</param>
    [UsedImplicitly]
    internal record PendingClientResponse(string ClientIp, int ClientPort);

    /// <param name="Error">Error message.</param>
    [UsedImplicitly]
    internal record ErrorResponse(string Error);

    /// <param name="Status">Status message.</param>
    [UsedImplicitly]
    internal record StatusResponse(string Status);

    #endregion
}

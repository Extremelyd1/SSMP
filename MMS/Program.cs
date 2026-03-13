using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using JetBrains.Annotations;
using MMS.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;

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
    /// The number of times to poll for a discovered port before timing out.
    /// Combined with <see cref="DiscoveryPollIntervalMs"/>, defines a total timeout of
    /// <c>DiscoveryPollCount * DiscoveryPollIntervalMs</c> milliseconds (default: 10 seconds).
    /// </summary>
    private const int DiscoveryPollCount = 40;

    /// <summary>
    /// The delay in milliseconds between each discovery poll attempt.
    /// Combined with <see cref="DiscoveryPollCount"/>, defines a total timeout of
    /// <c>DiscoveryPollCount * DiscoveryPollIntervalMs</c> milliseconds (default: 10 seconds).
    /// </summary>
    private const int DiscoveryPollIntervalMs = 250;

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
        builder.Services.AddHostedService<UdpDiscoveryService>();

        builder.Services.Configure<ForwardedHeadersOptions>(options => {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor |
                    ForwardedHeaders.XForwardedHost |
                    ForwardedHeaders.XForwardedProto;
            }
        );

        if (IsDevelopment) {
            builder.Services.AddHttpLogging(_ => { });
        } else {
            if (!ConfigureHttpsCertificate(builder)) {
                return;
            }
        }

        builder.Services.AddRateLimiter(options => {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, token) => {
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new ErrorResponse("Too many requests. Please try again later."), cancellationToken: token
                    );
                };

                options.AddPolicy(
                    "create", context =>
                        RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                            factory: _ => new FixedWindowRateLimiterOptions {
                                PermitLimit = 5,
                                Window = TimeSpan.FromSeconds(30),
                                QueueLimit = 0
                            }
                        )
                );

                options.AddPolicy(
                    "search", context =>
                        RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                            factory: _ => new FixedWindowRateLimiterOptions {
                                PermitLimit = 10,
                                Window = TimeSpan.FromSeconds(10),
                                QueueLimit = 0
                            }
                        )
                );

                options.AddPolicy(
                    "join", context =>
                        RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                            factory: _ => new FixedWindowRateLimiterOptions {
                                PermitLimit = 5,
                                Window = TimeSpan.FromSeconds(30),
                                QueueLimit = 0
                            }
                        )
                );
            }
        );

        var app = builder.Build();

        Logger = app.Logger;

        if (IsDevelopment) {
            app.UseHttpLogging();
        } else {
            app.UseExceptionHandler("/error");
        }

        app.UseForwardedHeaders();
        app.UseRateLimiter();
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
                    5000, options => { options.UseHttps(x509); }
                );
            }
        );

        return true;
    }

    #endregion

    #region Endpoint Registration

    /// <summary>
    /// Registers all API endpoints for the MatchMaking Server.
    /// </summary>
    private static void MapEndpoints(WebApplication app) {
        var lobbyService = app.Services.GetRequiredService<LobbyService>();

        // Health & Monitoring
        app.MapGet("/", () => Results.Ok(new { service = "MMS", version = "1.0", status = "healthy" }))
           .WithName("HealthCheck");
        app.MapGet("/lobbies", GetLobbies).WithName("ListLobbies").RequireRateLimiting("search");

        // Lobby Management
        app.MapPost("/lobby", CreateLobby).WithName("CreateLobby").RequireRateLimiting("create");
        app.MapDelete("/lobby/{token}", CloseLobby).WithName("CloseLobby");

        // Host Operations
        app.MapPost("/lobby/heartbeat/{token}", Heartbeat).WithName("Heartbeat");
        app.MapGet("/lobby/pending/{token}", GetPendingClients).WithName("GetPendingClients");
        app.MapPost("/lobby/discovery/verify/{token}", VerifyDiscovery).WithName("VerifyDiscovery");

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
                    "[WS] Host connected for lobby {LobbyIdentifier}",
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
                        "[WS] Host disconnected from lobby {LobbyIdentifier}",
                        IsDevelopment ? lobby.ConnectionData : lobby.LobbyName
                    );
                }
            }
        );

        // Client Operations
        app.MapPost("/lobby/{connectionData}/join", JoinLobby).WithName("JoinLobby").RequireRateLimiting("join");
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
            new CreateLobbyResponse(
                lobby.ConnectionData, lobby.HostToken, lobby.LobbyName, lobby.LobbyCode, lobby.HostDiscoveryToken
            )
        );
    }

    /// <summary>
    /// Waits for UDP discovery to complete and returns the discovered port.
    /// Notifies the host via WebSocket if a client token is provided.
    /// </summary>
    private static async Task<Results<Ok<DiscoveryResponse>, BadRequest<ErrorResponse>>> VerifyDiscovery(
        string token,
        LobbyService lobbyService,
        CancellationToken cancellationToken = default
    ) {
        for (var i = 0; i < DiscoveryPollCount; i++) {
            var port = lobbyService.GetDiscoveredPort(token);

            if (port is not null) {
                await TryNotifyHostAsync(token, port.Value, lobbyService, cancellationToken);
                lobbyService.ApplyHostPort(token, port.Value);
                lobbyService.RemoveDiscoveryToken(token);
                return TypedResults.Ok(new DiscoveryResponse(port.Value));
            }

            await Task.Delay(DiscoveryPollIntervalMs, cancellationToken);
        }

        return TypedResults.BadRequest(new ErrorResponse("Discovery timed out"));
    }

    /// <summary>
    /// If the token belongs to a client, pushes their external endpoint to the host via WebSocket.
    /// Silently skips if the lobby or WebSocket is unavailable.
    /// </summary>
    private static async Task TryNotifyHostAsync(
        string token,
        int port,
        LobbyService lobbyService,
        CancellationToken cancellationToken
    ) {
        if (!lobbyService.TryGetClientInfo(token, out var lobbyCode, out var clientIp))
            return;

        var lobby = lobbyService.GetLobbyByCode(lobbyCode);

        if (lobby?.HostWebSocket is not { State: WebSocketState.Open } ws)
            return;

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new {
                clientIp,
                clientPort = port
            }
        );

        await ws.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

        Logger.LogInformation(
            "Pushed client {ClientIp}:{ClientPort} to host via WebSocket after discovery",
            clientIp,
            port
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
    private static Results<Ok<StatusResponse>, NotFound<ErrorResponse>> Heartbeat(
        string token,
        LobbyService lobbyService
    ) {
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
    private static Results<Ok<JoinResponse>, NotFound<ErrorResponse>> JoinLobby(
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

        var clientDiscoveryToken = lobbyService.RegisterClientDiscoveryToken(lobby.LobbyCode, clientIp);

        Logger.LogInformation(
            "[JOIN] {ConnectionDetails}",
            IsDevelopment
                ? $"{clientIp}:{request.ClientPort} -> {lobby.ConnectionData}"
                : $"[Redacted]:{request.ClientPort} -> [Redacted]"
        );

        /* Host notification is now delayed until VerifyDiscovery is called by the client */
        /* This ensures the host gets the actual external port for hole-punching */

        // Fallback to queue for polling (legacy clients)
        lobby.PendingClients.Enqueue(
            new Models.PendingClient(clientIp, request.ClientPort, DateTime.UtcNow)
        );

        // Check if client is on the same network as the host
        var joinConnectionData = lobby.ConnectionData;
        string? lanConnectionData = null;

        // We can only check IP equality if we have the host's IP (for matchmaking lobbies mainly)
        // NOTE: This assumes lobby.ConnectionData is in "IP:Port" format for matchmaking
        if (string.IsNullOrEmpty(lobby.HostLanIp)) {
            return TypedResults.Ok(
                new JoinResponse(
                    joinConnectionData, lobby.LobbyType, clientIp, request.ClientPort, lanConnectionData,
                    clientDiscoveryToken
                )
            );
        }

        // Parse Host Public IP from ConnectionData (format: "IP:Port")
        var hostPublicIp = lobby.ConnectionData.Split(':')[0];

        if (clientIp != hostPublicIp) {
            return TypedResults.Ok(
                new JoinResponse(
                    joinConnectionData, lobby.LobbyType, clientIp, request.ClientPort, lanConnectionData,
                    clientDiscoveryToken
                )
            );
        }

        Logger.LogInformation("[JOIN] Local Network Detected! Returning LAN IP: {HostLanIp}", lobby.HostLanIp);
        lanConnectionData = lobby.HostLanIp;

        return TypedResults.Ok(
            new JoinResponse(
                joinConnectionData, lobby.LobbyType, clientIp, request.ClientPort, lanConnectionData,
                clientDiscoveryToken
            )
        );
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
        string LobbyCode,
        string? HostDiscoveryToken
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
        string? LanConnectionData,
        string? ClientDiscoveryToken
    );

    /// <param name="ExternalPort">Discovered external port.</param>
    [UsedImplicitly]
    internal record DiscoveryResponse(int ExternalPort);

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

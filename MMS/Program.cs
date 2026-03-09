using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using JetBrains.Annotations;
using MMS.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.HttpOverrides;
using MMS.Models;

namespace MMS;

/// <summary>
/// Entry point and HTTP endpoint definitions for the MMS (Multiplayer Matchmaking Service).
/// Hosts lobby creation, discovery, heartbeat, and WebSocket push-notification endpoints.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class Program {
    /// <summary>Well-known lobby type string constants used when creating or filtering lobbies.</summary>
    private static class LobbyTypes {
        /// <summary>
        /// Steam lobby — connection data is a Steam lobby ID.
        /// Used as the <c>LobbyType</c> field in <see cref="CreateLobbyRequest"/> and <see cref="LobbyResponse"/>.
        /// </summary>
        public const string Steam = "steam";

        /// <summary>
        /// Matchmaking lobby — connection data is an <c>IP:Port</c> pair discovered via UDP.
        /// Used as the <c>LobbyType</c> field in <see cref="CreateLobbyRequest"/> and <see cref="LobbyResponse"/>.
        /// </summary>
        public const string Matchmaking = "matchmaking";
    }

    /// <summary>
    /// Application entry point. Configures services, middleware, and Kestrel,
    /// then starts the web host.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the host builder.</param>
    public static void Main(string[] args) {
        var builder = WebApplication.CreateBuilder(args);
        var isDevelopment = builder.Environment.IsDevelopment();

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => {
                options.SingleLine = true;
                options.IncludeScopes = false;
                options.TimestampFormat = "HH:mm:ss ";
            }
        );

        builder.Services.AddSingleton<LobbyService>();
        builder.Services.AddSingleton<LobbyNameService>();
        builder.Services.AddSingleton<DiscoveryService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHostedService<LobbyCleanupService>();
        builder.Services.AddHostedService<UdpDiscoveryListener>();

        builder.Services.Configure<ForwardedHeadersOptions>(options => {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor |
                    ForwardedHeaders.XForwardedHost |
                    ForwardedHeaders.XForwardedProto;
            }
        );

        if (isDevelopment) {
            builder.Services.AddHttpLogging(_ => { });
        } else if (!ConfigureHttpsCertificate(builder)) {
            return;
        }

        var app = builder.Build();

        if (isDevelopment)
            app.UseHttpLogging();
        else
            app.UseExceptionHandler("/error");

        app.UseForwardedHeaders();
        app.UseWebSockets();
        MapEndpoints(app, isDevelopment);

        app.Urls.Add(isDevelopment ? "http://0.0.0.0:5000" : "https://0.0.0.0:5000");
        app.Run();
    }

    /// <summary>
    /// Configures HTTPS from <c>cert.pem</c> / <c>key.pem</c> files in the working directory.
    /// Reads both files, constructs an <see cref="X509Certificate2"/>, and attaches it to Kestrel.
    /// </summary>
    /// <param name="builder">The <see cref="WebApplicationBuilder"/> whose Kestrel instance will be configured.</param>
    /// <returns>
    /// <see langword="true"/> if the certificate was loaded and Kestrel configured successfully;
    /// <see langword="false"/> if any file is missing, unreadable, or cryptographically invalid
    /// (the failure reason is written to <see cref="Console"/>).
    /// </returns>
    private static bool ConfigureHttpsCertificate(WebApplicationBuilder builder) {
        const string certPath = "cert.pem";
        const string keyPath = "key.pem";

        if (!File.Exists(certPath)) {
            Console.WriteLine($"Certificate file '{certPath}' does not exist");
            return false;
        }

        if (!File.Exists(keyPath)) {
            Console.WriteLine($"Certificate key file '{keyPath}' does not exist");
            return false;
        }

        // Both read errors and malformed PEM content are non-recoverable start-up failures,
        // so a single catch covering both keeps the path linear.
        X509Certificate2 x509;
        try {
            var pem = File.ReadAllText(certPath);
            var key = File.ReadAllText(keyPath);
            x509 = X509Certificate2.CreateFromPem(pem, key);
        } catch (Exception e) when (e is IOException or CryptographicException) {
            Console.WriteLine($"Could not load HTTPS certificate from '{certPath}' / '{keyPath}':\n{e}");
            return false;
        }

        builder.WebHost.ConfigureKestrel(s =>
            s.ListenAnyIP(5000, o => o.UseHttps(x509))
        );

        return true;
    }

    /// <summary>
    /// Registers all HTTP and WebSocket route mappings on the application.
    /// </summary>
    /// <param name="app">The built <see cref="WebApplication"/> instance.</param>
    /// <param name="isDevelopment">
    /// <see langword="true"/> when running in the Development environment;
    /// controls whether sensitive values are included in logs.
    /// </param>
    private static void MapEndpoints(WebApplication app, bool isDevelopment) {
        // Health & Monitoring
        app.MapGet("/", () => Results.Ok(new { service = "MMS", version = "1.0", status = "healthy" }))
           .WithName("HealthCheck");
        app.MapGet("/lobbies", GetLobbies)
           .WithName("ListLobbies");

        // Lobby Management
        app.MapPost("/lobby", CreateLobby).WithName("CreateLobby");
        app.MapDelete("/lobby/{token}", CloseLobby).WithName("CloseLobby");

        // Host Operations
        app.MapPost("/lobby/heartbeat/{token}", Heartbeat).WithName("Heartbeat");
        app.MapGet("/lobby/pending/{token}", GetPendingClients).WithName("GetPendingClients");

        // WebSocket — host push notifications
        app.Map(
            "/ws/{token}", (HttpContext context, string token, LobbyService lobbyService, ILogger<Program> logger) =>
                HandleHostWebSocketAsync(context, token, lobbyService, logger, isDevelopment)
        );

        // Client Operations
        app.MapPost("/lobby/{connectionData}/join", JoinLobby).WithName("JoinLobby");
    }

    /// <summary>Returns all lobbies, optionally filtered by type.</summary>
    /// <param name="lobbyService">The lobby service used to query active lobbies.</param>
    /// <param name="type">Optional lobby type filter (e.g. <c>"steam"</c> or <c>"matchmaking"</c>).</param>
    /// <returns>A 200 OK response containing the matching lobby list.</returns>
    private static Ok<IEnumerable<LobbyResponse>> GetLobbies(LobbyService lobbyService, string? type = null) {
        var lobbies = lobbyService.GetLobbies(type)
                                  .Select(l => new LobbyResponse(
                                          l.ConnectionData, l.LobbyName, l.LobbyType, l.LobbyCode
                                      )
                                  );

        return TypedResults.Ok(lobbies);
    }

    /// <summary>
    /// Creates a new lobby (Steam or Matchmaking).
    /// For matchmaking lobbies, waits up to 5 seconds for a UDP discovery packet
    /// from the host bearing the supplied <see cref="CreateLobbyRequest.DiscoveryToken"/>.
    /// </summary>
    /// <param name="request">The lobby creation parameters supplied in the request body.</param>
    /// <param name="lobbyService">Service used to persist the new lobby.</param>
    /// <param name="lobbyNameService">Service used to generate a human-readable lobby name.</param>
    /// <param name="discoveryService">Service used to await the host's UDP endpoint advertisement.</param>
    /// <param name="logger">Logger for audit and diagnostic output.</param>
    /// <param name="env">The host environment, used to redact sensitive values outside Development.</param>
    /// <returns>
    /// 201 Created with <see cref="CreateLobbyResponse"/> on success;
    /// 400 Bad Request with an <see cref="ErrorResponse"/> on validation or discovery failure.
    /// </returns>
    private static async Task<Results<Created<CreateLobbyResponse>, BadRequest<ErrorResponse>>> CreateLobby(
        CreateLobbyRequest request,
        LobbyService lobbyService,
        LobbyNameService lobbyNameService,
        DiscoveryService discoveryService,
        ILogger<Program> logger,
        IWebHostEnvironment env
    ) {
        var lobbyType = request.LobbyType ?? LobbyTypes.Matchmaking;
        string connectionData;

        if (lobbyType == LobbyTypes.Steam) {
            if (string.IsNullOrEmpty(request.ConnectionData))
                return TypedResults.BadRequest(new ErrorResponse("Steam lobby requires ConnectionData"));

            connectionData = request.ConnectionData;
        } else {
            if (!request.DiscoveryToken.HasValue)
                return TypedResults.BadRequest(new ErrorResponse("DiscoveryToken is required for matchmaking lobbies"));

            var discovered = await discoveryService.WaitForDiscoveryAsync(
                request.DiscoveryToken.Value,
                TimeSpan.FromSeconds(5)
            );

            if (discovered is null) {
                return TypedResults.BadRequest(
                    new ErrorResponse(
                        "Endpoint discovery timed out — ensure UDP port 5001 is reachable"
                    )
                );
            }

            connectionData = $"{discovered.Address}:{discovered.Port}";
        }

        var lobby = lobbyService.CreateLobby(
            connectionData,
            lobbyNameService.GenerateLobbyName(),
            lobbyType,
            request.HostLanIp,
            request.IsPublic ?? true
        );

        logger.LogInformation(
            "[LOBBY] Created: '{LobbyName}' [{LobbyType}] ({Visibility}) -> {ConnectionData} (Code: {LobbyCode})",
            lobby.LobbyName,
            lobby.LobbyType,
            lobby.IsPublic ? "Public" : "Private",
            env.IsDevelopment() ? lobby.ConnectionData : "[Redacted]",
            lobby.LobbyCode
        );

        return TypedResults.Created(
            $"/lobby/{lobby.LobbyCode}",
            new CreateLobbyResponse(lobby.ConnectionData, lobby.HostToken, lobby.LobbyName, lobby.LobbyCode)
        );
    }

    /// <summary>Closes a lobby by host token.</summary>
    /// <param name="token">The host token identifying the lobby to close.</param>
    /// <param name="lobbyService">Service used to remove the lobby.</param>
    /// <param name="logger">Logger for audit output.</param>
    /// <returns>204 No Content on success; 404 Not Found if the token is unknown.</returns>
    private static Results<NoContent, NotFound<ErrorResponse>> CloseLobby(
        string token,
        LobbyService lobbyService,
        ILogger<Program> logger
    ) {
        if (!lobbyService.RemoveLobbyByToken(token))
            return TypedResults.NotFound(new ErrorResponse("Lobby not found"));

        logger.LogInformation("[LOBBY] Closed lobby with token {Token}", token);
        return TypedResults.NoContent();
    }

    /// <summary>Refreshes a lobby's heartbeat timestamp to prevent expiration.</summary>
    /// <param name="token">The host token of the lobby to refresh.</param>
    /// <param name="lobbyService">Service used to update the heartbeat.</param>
    /// <returns>200 OK with status <c>"alive"</c> on success; 404 Not Found if the token is unknown.</returns>
    private static Results<Ok<StatusResponse>, NotFound<ErrorResponse>> Heartbeat(
        string token,
        LobbyService lobbyService
    ) {
        return lobbyService.Heartbeat(token)
            ? TypedResults.Ok(new StatusResponse("alive"))
            : TypedResults.NotFound(new ErrorResponse("Lobby not found"));
    }

    /// <summary>
    /// Returns and clears pending clients waiting for NAT hole-punch.
    /// Clients older than 30 seconds are silently discarded.
    /// </summary>
    /// <param name="token">The host token of the lobby to query.</param>
    /// <param name="lobbyService">Service used to look up the lobby.</param>
    /// <param name="timeProvider">Time provider used to evaluate the 30-second staleness cutoff.</param>
    /// <returns>
    /// 200 OK with the list of recent pending clients on success;
    /// 404 Not Found if the token is unknown.
    /// </returns>
    private static Results<Ok<List<PendingClientResponse>>, NotFound<ErrorResponse>> GetPendingClients(
        string token,
        LobbyService lobbyService,
        TimeProvider timeProvider
    ) {
        var lobby = lobbyService.GetLobbyByToken(token);
        if (lobby is null)
            return TypedResults.NotFound(new ErrorResponse("Lobby not found"));

        var pending = new List<PendingClientResponse>();
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(-30);

        while (lobby.PendingClients.TryDequeue(out var client)) {
            if (client.RequestedAt >= cutoff)
                pending.Add(new PendingClientResponse(client.ClientIp, client.ClientPort));
        }

        return TypedResults.Ok(pending);
    }

    /// <summary>
    /// Registers a pending client join; returns host connection info and a <see cref="Guid"/> ClientToken.
    /// The client sends a UDP discovery packet with this token so MMS can observe the
    /// real external endpoint and push it to the host WebSocket automatically.
    /// </summary>
    /// <remarks>
    /// LAN detection compares the client's public IP against the host's public IP extracted
    /// from <see cref="LobbyResponse.ConnectionData"/>. When they match, the host's
    /// <see cref="CreateLobbyRequest.HostLanIp"/> is returned in <see cref="JoinResponse.LanConnectionData"/>
    /// so the client can connect over the local network instead.
    /// </remarks>
    /// <param name="connectionData">Lobby code or connection identifier supplied in the route.</param>
    /// <param name="lobbyService">Service used to locate the target lobby.</param>
    /// <param name="discoveryService">Service used to register the pending join and its client token.</param>
    /// <param name="logger">Logger for audit output.</param>
    /// <param name="env">The host environment, used to redact sensitive values outside Development.</param>
    /// <param name="context">The HTTP context, used to resolve the client's remote IP address.</param>
    /// <returns>
    /// 200 OK with a <see cref="JoinResponse"/> on success;
    /// 404 Not Found if the lobby does not exist.
    /// </returns>
    private static Results<Ok<JoinResponse>, NotFound<ErrorResponse>> JoinLobby(
        string connectionData,
        LobbyService lobbyService,
        DiscoveryService discoveryService,
        ILogger<Program> logger,
        IWebHostEnvironment env,
        HttpContext context
    ) {
        var lobby = lobbyService.GetLobbyByCode(connectionData) ?? lobbyService.GetLobby(connectionData);
        if (lobby is null)
            return TypedResults.NotFound(new ErrorResponse("Lobby not found"));

        // Normalize the remote IP — handles IPv6-mapped IPv4 (e.g., "::ffff:1.2.3.4" → "1.2.3.4")
        var clientIp = context.Connection.RemoteIpAddress is { } remoteIp
            ? (remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp).ToString()
            : "unknown";

        var clientToken = Guid.NewGuid();
        discoveryService.RegisterPendingJoin(clientToken, lobby.HostToken);

        logger.LogInformation(
            "[JOIN] Registered pending join for lobby {Lobby} (token: {Token})",
            env.IsDevelopment() ? lobby.ConnectionData : "[Redacted]",
            env.IsDevelopment() ? clientToken : "[Redacted]"
        );

        var lanConnectionData = ResolveLanConnectionData(lobby, clientIp, logger);
        return TypedResults.Ok(new JoinResponse(lobby.ConnectionData, lobby.LobbyType, lanConnectionData, clientToken));
    }

    /// <summary>
    /// Determines whether the joining client is on the same LAN as the host and,
    /// if so, returns the host's LAN address.
    /// </summary>
    /// <remarks>
    /// LAN membership is inferred by comparing the client's public IP to the host's
    /// public IP parsed from <c>lobby.ConnectionData</c>.  Both IPs must be available
    /// and equal for a LAN address to be returned.
    /// </remarks>
    /// <param name="lobby">The lobby whose host LAN IP and connection data are checked.</param>
    /// <param name="clientIp">The normalized public IP of the joining client.</param>
    /// <param name="logger">Logger used to emit a diagnostic message when a LAN peer is detected.</param>
    /// <returns>
    /// The host's LAN <c>IP:Port</c> string when the client shares the host's public IP;
    /// otherwise <see langword="null"/>.
    /// </returns>
    private static string? ResolveLanConnectionData(Lobby lobby, string clientIp, ILogger logger) {
        if (string.IsNullOrEmpty(lobby.HostLanIp))
            return null;

        if (!TryParseHostIp(lobby.ConnectionData, out var hostPublicIp))
            return null;

        if (clientIp != hostPublicIp)
            return null;

        logger.LogInformation("[JOIN] Local network detected — returning LAN IP: {LanIp}", lobby.HostLanIp);
        return lobby.HostLanIp;
    }

    /// <summary>
    /// Maintains an open WebSocket connection for a lobby host, keeping it alive
    /// for receiving push notifications (e.g., incoming client endpoints).
    /// Closes gracefully on a proper WebSocket close frame, or silently on a
    /// forcible connection reset (normal during game exit).
    /// </summary>
    /// <remarks>
    /// A receive buffer is rented from <see cref="ArrayPool{T}"/> for the lifetime of the
    /// connection to avoid per-message allocations.  The buffer is always returned in the
    /// <see langword="finally"/> block regardless of how the connection terminates.
    /// </remarks>
    /// <param name="context">The HTTP context for the incoming WebSocket upgrade request.</param>
    /// <param name="token">The host token used to look up the associated lobby.</param>
    /// <param name="lobbyService">Service used to locate the lobby and attach the WebSocket.</param>
    /// <param name="logger">Logger for connection lifecycle events.</param>
    /// <param name="isDevelopment">
    /// When <see langword="true"/>, logs the raw connection data; otherwise logs the lobby name.
    /// </param>
    private static async Task HandleHostWebSocketAsync(
        HttpContext context,
        string token,
        LobbyService lobbyService,
        ILogger<Program> logger,
        bool isDevelopment
    ) {
        if (!context.WebSockets.IsWebSocketRequest) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var lobby = lobbyService.GetLobbyByToken(token);
        if (lobby is null) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        lobby.HostWebSocket = webSocket;

        var lobbyId = isDevelopment ? lobby.ConnectionData : lobby.LobbyName;
        logger.LogInformation("[WS] Host connected for lobby {Lobby}", lobbyId);

        // Rent a receive buffer for the lifetime of this connection rather than allocating
        var buffer = ArrayPool<byte>.Shared.Rent(1024);
        try {
            while (webSocket.State == WebSocketState.Open) {
                var result = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        } catch (Exception ex) when (ex is WebSocketException || ex.InnerException is SocketException) {
            // Host disconnected without a proper close handshake — expected during game exit
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
            lobby.HostWebSocket = null;
            logger.LogInformation("[WS] Host disconnected from lobby {Lobby}", lobbyId);
        }
    }

    /// <summary>
    /// Extracts the host IP string from a <c>"ip:port"</c> or bare IP <paramref name="connectionData"/> value.
    /// </summary>
    /// <remarks>
    /// Delegates first to <see cref="IPEndPoint.TryParse(ReadOnlySpan&lt;char&gt;, out IPEndPoint?)"/> which handles IPv4 (<c>"1.2.3.4:7777"</c>),
    /// bracketed IPv6 with port (<c>"[::1]:7777"</c>), and bare IPv6.
    /// Falls back to <see cref="IPAddress.TryParse(string?, out IPAddress?)"/> for addresses with no port component.
    /// </remarks>
    /// <param name="connectionData">The raw connection string to parse.</param>
    /// <param name="hostIp">
    /// When this method returns <see langword="true"/>, contains the extracted IP address string;
    /// otherwise <see cref="string.Empty"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if an IP address was successfully parsed; otherwise <see langword="false"/>.
    /// </returns>
    private static bool TryParseHostIp(string connectionData, out string hostIp) {
        // IPEndPoint.TryParse handles IPv4, IPv6, and bracketed IPv6 with port
        if (IPEndPoint.TryParse(connectionData, out var ep)) {
            hostIp = ep.Address.ToString();
            return true;
        }

        // Fallback: bare IP with no port
        if (IPAddress.TryParse(connectionData, out var addr)) {
            hostIp = addr.ToString();
            return true;
        }

        hostIp = string.Empty;
        return false;
    }

    /// <summary>Request body for the <c>POST /lobby</c> endpoint.</summary>
    /// <param name="DiscoveryToken">Token sent via UDP for endpoint discovery (Matchmaking only).</param>
    /// <param name="ConnectionData">Steam lobby ID (Steam only).</param>
    /// <param name="LobbyType"><c>"steam"</c> or <c>"matchmaking"</c> (default: matchmaking).</param>
    /// <param name="HostLanIp">Host LAN IP:Port used for local-network detection.</param>
    /// <param name="IsPublic">Whether the lobby appears in the browser (default: <see langword="true"/>).</param>
    [UsedImplicitly]
    private record CreateLobbyRequest(
        Guid? DiscoveryToken,
        string? ConnectionData,
        string? LobbyType,
        string? HostLanIp,
        bool? IsPublic
    );

    /// <summary>Response body returned by a successful <c>POST /lobby</c> call.</summary>
    /// <param name="ConnectionData">Connection identifier (IP:Port or Steam lobby ID).</param>
    /// <param name="HostToken">Secret token for subsequent host operations.</param>
    /// <param name="LobbyName">Auto-generated display name of the lobby.</param>
    /// <param name="LobbyCode">Human-readable invite code.</param>
    [UsedImplicitly]
    internal record CreateLobbyResponse(
        string ConnectionData,
        string HostToken,
        string LobbyName,
        string LobbyCode
    );

    /// <summary>Lobby summary returned by <c>GET /lobbies</c>.</summary>
    /// <param name="ConnectionData">Connection identifier (IP:Port or Steam lobby ID).</param>
    /// <param name="Name">Display name.</param>
    /// <param name="LobbyType"><c>"steam"</c> or <c>"matchmaking"</c>.</param>
    /// <param name="LobbyCode">Human-readable invite code.</param>
    [UsedImplicitly]
    internal record LobbyResponse(
        string ConnectionData,
        string Name,
        string LobbyType,
        string LobbyCode
    );

    /// <summary>Response body returned by a successful <c>POST /lobby/{connectionData}/join</c> call.</summary>
    /// <param name="ConnectionData">Host connection data (IP:Port or Steam lobby ID).</param>
    /// <param name="LobbyType"><c>"steam"</c> or <c>"matchmaking"</c>.</param>
    /// <param name="LanConnectionData">Host LAN address if the client is on the same network; otherwise <see langword="null"/>.</param>
    /// <param name="ClientToken">Token the client must include in its UDP discovery packet.</param>
    [UsedImplicitly]
    internal record JoinResponse(
        string ConnectionData,
        string LobbyType,
        string? LanConnectionData,
        Guid ClientToken
    );

    /// <summary>Represents a client that has joined a lobby but whose NAT hole-punch is still pending.</summary>
    /// <param name="ClientIp">Pending client's public IP address.</param>
    /// <param name="ClientPort">Pending client's public port.</param>
    [UsedImplicitly]
    internal record PendingClientResponse(string ClientIp, int ClientPort);

    /// <summary>Generic error response body.</summary>
    /// <param name="Error">Human-readable error message.</param>
    [UsedImplicitly]
    internal record ErrorResponse(string Error);

    /// <summary>Generic status response body.</summary>
    /// <param name="Status">Human-readable status message.</param>
    [UsedImplicitly]
    internal record StatusResponse(string Status);
}

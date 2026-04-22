using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using MMS.Services.Lobbies;
using MMS.Services.Matchmaking;
using MMS.Services.Network;
using static MMS.Contracts.Responses;

namespace MMS.Bootstrap;

/// <summary>
/// Extension methods for registering MMS services and infrastructure concerns.
/// </summary>
internal static class ServiceCollectionExtensions {
    /// <summary>
    /// Registers MMS application services and hosted background services.
    /// </summary>
    /// <param name="services">The service collection being configured.</param>
    public static void AddMmsCoreServices(this IServiceCollection services) {
        services.AddSingleton<LobbyNameService>();
        services.AddSingleton<LobbyService>();
        services.AddSingleton<JoinSessionStore>();
        services.AddSingleton<JoinSessionMessenger>();
        services.AddSingleton<JoinSessionCoordinator>();
        services.AddSingleton<JoinSessionService>();
        services.AddHostedService<LobbyCleanupService>();
        services.AddHostedService<UdpDiscoveryService>();
    }

    /// <summary>
    /// Registers logging, forwarded headers, HTTP logging, and rate limiting for MMS.
    /// </summary>
    /// <param name="services">The service collection being configured.</param>
    /// <param name="configuration">The application configuration, used to bind infrastructure settings such as forwarded header options.</param>
    /// <param name="isDevelopment">Whether the app is running in development.</param>
    public static void AddMmsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment
    ) {
        services.AddMmsLogging(isDevelopment);
        services.AddMmsForwardedHeaders(configuration);
        services.AddMmsRateLimiting();
    }

    /// <summary>
    /// Configures structured console logging.
    /// Enables HTTP request logging when running in development.
    /// </summary>
    /// <param name="services">The service collection being configured.</param>
    /// <param name="isDevelopment">Whether the app is running in development.</param>
    private static void AddMmsLogging(this IServiceCollection services, bool isDevelopment) {
        services.AddLogging(builder => {
                builder.ClearProviders();
                builder.AddSimpleConsole(options => {
                        options.SingleLine = true;
                        options.IncludeScopes = false;
                        options.TimestampFormat = "HH:mm:ss ";
                    }
                );
            }
        );

        if (isDevelopment)
            services.AddHttpLogging(_ => { });
    }

    /// <summary>
    /// Configures forwarded header processing for reverse proxy support.
    /// Enables forwarding of <c>X-Forwarded-For</c>, <c>X-Forwarded-Host</c>,
    /// and <c>X-Forwarded-Proto</c> headers.
    /// </summary>
    /// <param name="services">The service collection being configured.</param>
    /// <param name="configuration">
    /// The application configuration. Reads <c>ForwardedHeaders:KnownProxies</c> as an array of IP address strings
    /// and <c>ForwardedHeaders:KnownNetworks</c> as an array of CIDR notation strings to populate
    /// <see cref="ForwardedHeadersOptions.KnownProxies"/> and <see cref="ForwardedHeadersOptions.KnownNetworks"/> respectively.
    /// </param>
    private static void AddMmsForwardedHeaders(this IServiceCollection services, IConfiguration configuration) {
        services.Configure<ForwardedHeadersOptions>(options => {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor |
                    ForwardedHeaders.XForwardedHost |
                    ForwardedHeaders.XForwardedProto;

                // Trust individual reverse-proxy IPs from configuration so ASP.NET Core
                // accepts forwarded client IP/protocol/host headers from those addresses.
                foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? []) {
                    if (IPAddress.TryParse(proxy, out var address))
                        options.KnownProxies.Add(address);
                }

                // Trust whole proxy networks expressed as CIDR ranges. Invalid entries are
                // ignored so a malformed value does not break startup.
                foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ??
                                        []) {
                    if (!TryParseNetwork(network, out var ipNetwork))
                        continue;

                    options.KnownNetworks.Add(ipNetwork);
                }
            }
        );
    }

    /// <summary>
    /// Attempts to parse a CIDR notation string into an <see cref="Microsoft.AspNetCore.HttpOverrides.IPNetwork"/>.
    /// </summary>
    /// <param name="value">The CIDR string to parse, expected in the format <c>address/prefixLength</c> (e.g. <c>192.168.1.0/24</c>).</param>
    /// <param name="network">
    /// When this method returns <see langword="true"/>, contains the parsed <see cref="Microsoft.AspNetCore.HttpOverrides.IPNetwork"/>;
    /// otherwise, the default value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="value"/> was successfully parsed; otherwise, <see langword="false"/>.
    /// </returns>
    private static bool TryParseNetwork(string value, out Microsoft.AspNetCore.HttpOverrides.IPNetwork network) {
        network = null!;

        var slashIndex = value.IndexOf('/');
        if (slashIndex <= 0 || slashIndex >= value.Length - 1)
            return false;

        var prefixText = value[(slashIndex + 1)..];
        if (!IPAddress.TryParse(value[..slashIndex], out var prefix) ||
            !int.TryParse(prefixText, out var prefixLength)) {
            return false;
        }

        network = new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength);
        return true;
    }

    /// <summary>
    /// Registers IP-based fixed-window rate limiting policies for all MMS endpoints.
    /// Rejected requests receive a <c>429 Too Many Requests</c> response.
    /// </summary>
    /// <remarks>
    /// Policies:
    /// <list type="bullet">
    ///   <item><term>create</term><description>5 requests per 30 seconds.</description></item>
    ///   <item><term>search</term><description>10 requests per 10 seconds.</description></item>
    ///   <item><term>join</term><description>5 requests per 30 seconds.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="services">The service collection being configured.</param>
    private static void AddMmsRateLimiting(this IServiceCollection services) {
        services.AddRateLimiter(options => {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, token) => {
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new ErrorResponse("Too many requests. Please try again later."),
                        cancellationToken: token
                    );
                };

                options.AddFixedWindowPolicy("create", permitLimit: 5, windowSeconds: 30);
                options.AddFixedWindowPolicy("search", permitLimit: 10, windowSeconds: 10);
                options.AddFixedWindowPolicy("join", permitLimit: 5, windowSeconds: 30);
            }
        );
    }

    /// <summary>
    /// Adds a named IP-keyed fixed-window rate limiter policy to the rate limiter options.
    /// </summary>
    /// <param name="options">The rate limiter options to configure.</param>
    /// <param name="policyName">The name used to reference this policy on endpoints.</param>
    /// <param name="permitLimit">Maximum number of requests allowed per window.</param>
    /// <param name="windowSeconds">Duration of the rate limit window in seconds.</param>
    private static void AddFixedWindowPolicy(
        this RateLimiterOptions options,
        string policyName,
        int permitLimit,
        int windowSeconds
    ) {
        options.AddPolicy(
            policyName,
            context => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: GetRateLimitPartitionKey(context),
                factory: _ => new FixedWindowRateLimiterOptions {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0
                }
            )
        );
    }

    /// <summary>
    /// Determines the rate limit partition key for an HTTP request.
    /// Uses the first IP address from the <c>X-Forwarded-For</c> header when present,
    /// falling back to <see cref="ConnectionInfo.RemoteIpAddress"/>, then <see cref="ConnectionInfo.Id"/>.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A string key identifying the client for rate limiting purposes.</returns>
    private static string GetRateLimitPartitionKey(HttpContext context) {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
            return forwardedFor.Split(',')[0].Trim();

        return context.Connection.RemoteIpAddress?.ToString()
               ?? context.Connection.Id;
    }
}

using MMS.Contracts;
using MMS.Models;
using MMS.Http;

namespace MMS.Features.Health;

/// <summary>
/// Maps health and monitoring endpoints.
/// </summary>
internal static class HealthEndpoints {
    /// <summary>
    /// Maps health-related MMS endpoints.
    /// </summary>
    /// <param name="app">The web application to map endpoints onto.</param>
    public static void MapHealthEndpoints(this WebApplication app) {
        app.Endpoint()
           .Get("/health")
           .Handler(HealthCheck)
           .WithName("HealthCheck")
           .Build();
    }

    /// <summary>
    /// Returns the service name, current matchmaking protocol version, and a static health status.
    /// </summary>
    private static IResult HealthCheck() =>
        Results.Ok(new Responses.HealthResponse("MMS", MatchmakingProtocol.CurrentVersion, "healthy"));
}

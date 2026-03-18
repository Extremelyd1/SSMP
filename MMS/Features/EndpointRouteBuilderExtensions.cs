using MMS.Features.Health;
using MMS.Features.Lobby;
using MMS.Features.WebSockets;

namespace MMS.Features;

/// <summary>
/// Composes all MMS endpoint groups onto the web application.
/// </summary>
internal static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps all HTTP and WebSocket endpoints exposed by MMS.
    /// </summary>
    /// <param name="app">The web application to map endpoints onto.</param>
    public static void MapMmsEndpoints(this WebApplication app)
    {
        var lobby = app.MapGroup("/lobby");
        var webSockets = app.MapGroup("/ws");
        var joinWebSockets = webSockets.MapGroup("/join");

        app.MapHealthEndpoints();
        app.MapLobbyEndpoints(lobby);
        WebSocketEndpoints.MapWebSocketEndpoints(webSockets, joinWebSockets);
    }
}

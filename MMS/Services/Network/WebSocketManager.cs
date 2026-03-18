using System.Net.WebSockets;
using System.Text.Json;
using MMS.Services.Lobby;
using MMS.Services.Matchmaking;

namespace MMS.Services.Network;

/// <summary>
/// Serializes and sends JSON payloads over WebSocket connections.
/// Shared by <see cref="LobbyService"/> and <see cref="JoinSessionService"/> so that
/// both use the same serialization path without a common base class.
/// </summary>
internal static class WebSocketMessenger
{
    /// <summary>
    /// Serializes <paramref name="payload"/> to UTF-8 JSON and sends it as a single text frame.
    /// </summary>
    /// <param name="webSocket">The open WebSocket to send on.</param>
    /// <param name="payload">The object to serialize. Must be JSON-serializable.</param>
    /// <param name="cancellationToken">Token used to cancel the send operation.</param>
    public static async Task SendAsync(WebSocket webSocket, object payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }
}

using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SSMP.Logging;

namespace SSMP.Networking.Matchmaking.Utilities;

/// <summary>Stateless helpers for MMS protocol handling.</summary>
internal static class MmsUtilities {
    /// <summary>Converts http(s) to ws(s). Throws if not absolute.</summary>
    public static string ToWebSocketUrl(string httpUrl) {
        if (!Uri.TryCreate(httpUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException("Matchmaking URL must be an absolute URI.", nameof(httpUrl));

        var scheme = uri.Scheme switch {
            "http" => "ws",
            "https" => "wss",
            _ => throw new ArgumentException("Matchmaking URL must use http or https.", nameof(httpUrl))
        };

        var builder = new UriBuilder(uri) { Scheme = scheme };
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    /// <summary>Logs unexpected failures in fire-and-forget task.</summary>
    /// <param name="task">The task to monitor.</param>
    /// <param name="owner">Component name included in failure logs.</param>
    /// <param name="operationName">Human-readable operation label for diagnostics.</param>
    public static Task RunBackground(Task task, string owner, string operationName) =>
        ObserveAsync(task, owner, operationName);

    /// <summary>
    /// Assembles text frames from WebSocket. Returns null payload for non-text/close.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the socket is not connected.</exception>
    public static async Task<(WebSocketMessageType messageType, string? message)> ReceiveTextMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken,
        int maxMessageBytes = 16 * 1024
    ) {
        const int chunkSize = 1024;
        var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
        var writer = new ArrayBufferWriter<byte>();

        try {
            while (true) {
                var frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (frame.MessageType == WebSocketMessageType.Close)
                    return (frame.MessageType, null);

                AppendFrame(writer, buffer, frame.Count, maxMessageBytes);

                if (!frame.EndOfMessage)
                    continue;

                return frame.MessageType != WebSocketMessageType.Text
                    ? (frame.MessageType, null)
                    : (frame.MessageType,
                        writer.WrittenCount == 0 ? string.Empty : Encoding.UTF8.GetString(writer.WrittenSpan));
            }
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Deterministically identifies outbound IPv4 via dummy UDP connect.</summary>
    public static string? GetLocalIpAddress() {
        try {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
        } catch (Exception ex) {
            Logger.Debug($"MmsUtilities: GetLocalIpAddress failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Awaits a background task and suppresses expected cancellation while logging unexpected failures.
    /// </summary>
    /// <param name="task">The task being observed.</param>
    /// <param name="owner">Component name included in failure logs.</param>
    /// <param name="operationName">Human-readable operation label for diagnostics.</param>
    private static async Task ObserveAsync(Task task, string owner, string operationName) {
        try {
            await task.ConfigureAwait(false);
        } catch (OperationCanceledException) {
            /*ignored*/
        } catch (Exception ex) {
            Logger.Warn($"{owner}: {operationName} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Appends a single received frame into the accumulating message buffer while enforcing the message size cap.
    /// </summary>
    /// <param name="writer">The destination buffer holding the message assembled so far.</param>
    /// <param name="buffer">Scratch receive buffer containing the latest frame bytes.</param>
    /// <param name="count">Number of valid bytes currently in <paramref name="buffer"/>.</param>
    /// <param name="maxMessageBytes">Maximum total message size allowed.</param>
    private static void AppendFrame(ArrayBufferWriter<byte> writer, byte[] buffer, int count, int maxMessageBytes) {
        if (count <= 0)
            return;

        var nextLength = writer.WrittenCount + count;
        if (nextLength > maxMessageBytes)
            throw new InvalidOperationException("Matchmaking WebSocket message exceeded the maximum size.");

        buffer.AsSpan(0, count).CopyTo(writer.GetSpan(count));
        writer.Advance(count);
    }
}

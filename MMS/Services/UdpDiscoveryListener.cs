using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace MMS.Services;

/// <summary>
/// Background service that listens on UDP port 5001 for endpoint discovery packets.
/// Accepts 17-byte packets: <c>[0x44 ('D')][16-byte Guid token]</c>.
/// <para>
/// For host tokens: records the external endpoint in <see cref="DiscoveryService"/>
/// so <c>POST /lobby</c> can use it.
/// For client tokens: records the endpoint and immediately pushes it to the host WebSocket.
/// </para>
/// </summary>
public sealed class UdpDiscoveryListener(
    DiscoveryService discoveryService,
    LobbyService lobbyService,
    ILogger<UdpDiscoveryListener> logger
) : BackgroundService {
    /// <summary>UDP port the listener binds to.</summary>
    private const int UdpPort = 5001;

    /// <summary>Expected packet size: 1-byte opcode + 16-byte Guid.</summary>
    private const int PacketSize = 17;

    /// <summary>Opcode for endpoint discovery packets.</summary>
    private const byte DiscoveryOpcode = 0x44; // 'D'

    /// <summary>
    /// Binds a UDP socket on <see cref="UdpPort"/> and dispatches received packets
    /// to <see cref="HandlePacketAsync"/> until <paramref name="stoppingToken"/> is signalled.
    /// Individual packet errors are logged and swallowed so a single bad packet cannot
    /// bring down the listener loop.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token that stops the service when the host shuts down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var udp = new UdpClient(UdpPort);
        logger.LogInformation("[UDP] Discovery listener started on port {Port}", UdpPort);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                var result = await udp.ReceiveAsync(stoppingToken);
                await HandlePacketAsync(result, stoppingToken);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                // Pass full exception — preserves stack trace in structured logging
                logger.LogError(ex, "[UDP] Error processing discovery packet");
            }
        }

        logger.LogInformation("[UDP] Discovery listener stopped");
    }

    /// <summary>
    /// Validates an incoming UDP packet, records its endpoint in <see cref="DiscoveryService"/>,
    /// and — if the token belongs to a pending client join — pushes the endpoint to the host
    /// WebSocket via <see cref="PushClientEndpointAsync"/>.
    /// Packets with an unexpected length or opcode are silently discarded.
    /// </summary>
    /// <param name="result">The received UDP datagram and its remote endpoint.</param>
    /// <param name="cancellationToken">Propagated from <see cref="ExecuteAsync"/>.</param>
    private async Task HandlePacketAsync(UdpReceiveResult result, CancellationToken cancellationToken) {
        ReadOnlySpan<byte> data = result.Buffer;

        // Validate packet length and opcode before any further processing
        if (data.Length != PacketSize || data[0] != DiscoveryOpcode)
            return;

        // Parse Guid directly from span — avoids a 16-byte heap allocation
        var token = new Guid(data.Slice(1, 16));
        var endpoint = result.RemoteEndPoint;

        discoveryService.Record(token, endpoint);
        logger.LogDebug("[UDP] Recorded endpoint {Endpoint} for token {Token}", endpoint, token);

        // If this token belongs to a pending client join, push the endpoint to the host WebSocket
        var hostToken = discoveryService.TryConsumePendingJoin(token);
        if (hostToken is null)
            return;

        var lobby = lobbyService.GetLobbyByToken(hostToken);
        if (lobby?.HostWebSocket is not { State: WebSocketState.Open } ws) {
            logger.LogWarning("[UDP] Host WebSocket unavailable for pending join token {Token}", token);
            return;
        }

        await PushClientEndpointAsync(ws, endpoint, cancellationToken);
        logger.LogInformation("[UDP] Pushed client endpoint {Endpoint} to host via WebSocket", endpoint);
    }

    /// <summary>
    /// Serialises the client endpoint as a JSON object and sends it as a WebSocket text frame.
    /// Uses a pooled byte buffer to avoid per-send heap allocations.
    /// </summary>
    /// <remarks>
    /// JSON format: <c>{"clientIp":"&lt;address&gt;","clientPort":&lt;port&gt;}</c>.
    /// The buffer is sized via <see cref="Encoding.GetMaxByteCount"/> and always returned
    /// to the pool in the <see langword="finally"/> block.
    /// </remarks>
    /// <param name="ws">The open WebSocket to send on.</param>
    /// <param name="endpoint">The client's external <see cref="IPEndPoint"/>.</param>
    /// <param name="cancellationToken">Propagated from <see cref="ExecuteAsync"/>.</param>
    private static async Task PushClientEndpointAsync(
        WebSocket ws,
        IPEndPoint endpoint,
        CancellationToken cancellationToken
    ) {
        var json = $"{{\"clientIp\":\"{endpoint.Address}\",\"clientPort\":{endpoint.Port}}}";

        // Rent a byte buffer from the pool instead of allocating a new array
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(json.Length);
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
        try {
            var byteCount = Encoding.UTF8.GetBytes(json, rentedBuffer);
            var segment = new ArraySegment<byte>(rentedBuffer, 0, byteCount);
            await ws.SendAsync(segment, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        } finally {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }
}

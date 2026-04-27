using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using MMS.Bootstrap;
using MMS.Services.Matchmaking;
using MMS.Services.Utility;

namespace MMS.Services.Network;

/// <summary>
/// Hosted background service that listens for incoming UDP packets on a fixed port
/// as part of the NAT traversal discovery flow.
/// </summary>
/// <remarks>
/// Each valid packet carries a session token encoded as UTF-8. The sender's observed
/// external endpoint is recorded in <see cref="JoinSessionService"/>, advancing
/// the hole-punch state machine for the corresponding host or client session.
/// </remarks>
public sealed class UdpDiscoveryService : BackgroundService {
    /// <summary>
    /// Service used to record discovered UDP ports and advance join session states.
    /// </summary>
    private readonly JoinSessionService _joinSessionService;

    /// <summary>
    /// Logger instance for this service.
    /// </summary>
    private readonly ILogger<UdpDiscoveryService> _logger;

    /// <summary>
    /// The fixed UDP port used for discovery packets.
    /// </summary>
    private static readonly int Port = ProgramState.DiscoveryPort;

    /// <summary>
    /// Valid discovery packets must be exactly this many bytes.
    /// Packets of any other length are dropped before string decoding.
    /// </summary>
    private const int TokenByteLength = 32;

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpDiscoveryService"/> class.
    /// </summary>
    /// <param name="joinSessionService">Service used to record discovered UDP ports and advance join session states.</param>
    /// <param name="logger">Logger instance for this service.</param>
    public UdpDiscoveryService(JoinSessionService joinSessionService, ILogger<UdpDiscoveryService> logger) {
        _joinSessionService = joinSessionService;
        _logger = logger;
    }

    /// <summary>
    /// Binds a <see cref="UdpClient"/> to <see cref="Port"/> and enters a receive loop
    /// until <paramref name="stoppingToken"/> is canceled by the hosting infrastructure.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var udpClient = new UdpClient(Port);
        _logger.LogInformation("UDP Discovery Service listening on port {Port}", Port);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                var result = await udpClient.ReceiveAsync(stoppingToken);
                await ProcessPacketAsync(result.Buffer, result.RemoteEndPoint, stoppingToken);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error in UDP Discovery Service receive loop");
            }
        }

        _logger.LogInformation("UDP Discovery Service stopped");
    }

    /// <summary>
    /// Validates and processes a single UDP packet.
    /// Byte-length is checked before string decoding to avoid allocations for packets
    /// that would be rejected anyway (oversized probes, garbage data, etc.).
    /// </summary>
    private async Task ProcessPacketAsync(
        byte[] buffer,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    ) {
        if (buffer.Length != TokenByteLength) {
            _logger.LogWarning(
                "Received malformed discovery packet from {EndPoint} (length: {Length})",
                PrivacyFormatter.Format(remoteEndPoint),
                buffer.Length
            );
            return;
        }

        var token = Encoding.UTF8.GetString(buffer);

        _logger.LogDebug(
            "Received discovery packet {TokenFingerprint} from {EndPoint}",
            GetTokenFingerprint(token),
            PrivacyFormatter.Format(remoteEndPoint)
        );

        if (!await _joinSessionService.ValidateDiscoveredClientIpAsync(
                token,
                remoteEndPoint.Address.ToString(),
                cancellationToken
            ))
            return;

        await _joinSessionService.SetDiscoveredPortAsync(token, remoteEndPoint.Port, cancellationToken);
    }

    /// <summary>
    /// Generates a short, non-reversible SHA-256 fingerprint for a session token.
    /// Used for correlation in debug logs without exposing the full token.
    /// </summary>
    /// <param name="token">The token string whose fingerprint to generate.</param>
    /// <returns>A 12-character hex string representing the fingerprint.</returns>
    private static string GetTokenFingerprint(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..12];
}

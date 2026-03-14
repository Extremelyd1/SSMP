using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MMS.Services;

/// <summary>
/// A hosted background service that listens for incoming UDP packets on a fixed port,
/// used as part of a NAT traversal / hole-punching discovery flow.
/// </summary>
public sealed class UdpDiscoveryService : BackgroundService {
    private readonly LobbyService _lobbyService;
    private readonly ILogger<UdpDiscoveryService> _logger;

    /// <summary>
    /// The UDP port this service binds to at startup.
    /// </summary>
    private const int Port = 5001;

    /// <summary>
    /// The exact number of bytes a valid discovery packet must contain.
    /// Packets shorter or longer than this are considered malformed and are dropped.
    /// </summary>
    private const int TokenByteLength = 32;

    /// <summary>
    /// Creates a new instance of <see cref="UdpDiscoveryService"/> with the services
    /// it needs to store discovered port mappings and emit log output.
    /// </summary>
    /// <param name="lobbyService">
    /// The lobby service that maps session tokens to their discovered external ports.
    /// Called once per valid packet to record the NAT-mapped port for the sending client.
    /// </param>
    /// <param name="logger">
    /// Logger used to emit startup, shutdown, and per-packet diagnostic messages.
    /// </param>
    public UdpDiscoveryService(LobbyService lobbyService, ILogger<UdpDiscoveryService> logger) {
        _lobbyService = lobbyService;
        _logger = logger;
    }

    /// <summary>
    /// Entry point called by the .NET host when the application starts.
    /// Binds a <see cref="UdpClient"/> to <see cref="Port"/> and enters a receive loop
    /// that runs until the host requests shutdown via <paramref name="stoppingToken"/>.
    /// </summary>
    /// <param name="stoppingToken">
    /// Cancellation token provided by the .NET hosting infrastructure.
    /// Triggered automatically on application shutdown (e.g. Ctrl+C, SIGTERM, IIS stop).
    /// When fired, the current <c>ReceiveAsync</c> call is cancelled and the loop exits cleanly.
    /// </param>
    /// <returns>A <see cref="Task"/> that completes when the service has fully stopped.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var udpClient = new UdpClient(Port);
        _logger.LogInformation("UDP Discovery Service listening on port {Port}", Port);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                var result = await udpClient.ReceiveAsync(stoppingToken);
                ProcessPacket(result.Buffer, result.RemoteEndPoint);
            } catch (OperationCanceledException) { break; } catch (Exception ex) { _logger.LogError(ex, "Error in UDP Discovery Service receive loop"); }
        }

        _logger.LogInformation("UDP Discovery Service stopped");
    }

    /// <summary>
    /// Validates and processes a single incoming UDP packet.
    /// If the packet is well-formed, the session token is decoded and the sender's
    /// external port is recorded in <see cref="LobbyService"/>.
    /// </summary>
    /// <remarks>
    /// Validation is intentionally done on the raw <paramref name="buffer"/> byte count
    /// <b>before</b> decoding to a string. This avoids a heap allocation for packets
    /// that would be rejected anyway (e.g. oversized probes, garbage data).
    /// </remarks>
    /// <param name="buffer">
    /// The raw bytes received from the UDP socket. Must be exactly <see cref="TokenByteLength"/>
    /// bytes long to be considered valid.
    /// </param>
    /// <param name="remoteEndPoint">
    /// The IP address and port of the sender as seen by this server — i.e. the
    /// NAT-translated public endpoint, not the client's LAN address.
    /// The port component of this value is what gets stored as the discovered port.
    /// </param>
    private void ProcessPacket(byte[] buffer, IPEndPoint remoteEndPoint) {
        // Validate byte length before decoding to avoid unnecessary string allocation
        if (buffer.Length != TokenByteLength) {
            _logger.LogWarning(
                "Received malformed discovery packet from {EndPoint} (length: {Length})",
                FormatEndPoint(remoteEndPoint),
                buffer.Length);
            return;
        }

        var token = Encoding.UTF8.GetString(buffer);

        _logger.LogInformation(
            "Received discovery token {Token} from {EndPoint}",
            token,
            FormatEndPoint(remoteEndPoint));

        _lobbyService.SetDiscoveredPort(token, remoteEndPoint.Port);
    }

    private static string FormatEndPoint(IPEndPoint remoteEndPoint) =>
        Program.IsDevelopment ? remoteEndPoint.ToString() : "[Redacted]";
}

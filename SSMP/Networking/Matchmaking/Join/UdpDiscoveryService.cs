using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SSMP.Logging;
using SSMP.Networking.Matchmaking.Protocol;

namespace SSMP.Networking.Matchmaking.Join;

/// <summary>
/// Sends periodic UDP packets carrying a discovery token to the MMS discovery port.
/// MMS uses the incoming packets to learn the sender's external IP and port,
/// which it then shares with the peer to enable NAT hole-punching.
/// </summary>
internal static class UdpDiscoveryService {
    /// <summary>
    /// The expected length of the discovery token in bytes. 
    /// This corresponds to a 32-character hexadecimal UUID.
    /// </summary>
    private const int ExpectedTokenByteLength = 32;

    /// <summary>
    /// Resolves the MMS discovery endpoint and sends token bytes every
    /// <see cref="MmsProtocol.DiscoveryIntervalMs"/> until cancellation.
    /// </summary>
    public static async Task SendUntilCancelledAsync(
        string discoveryHost,
        string token,
        Action<byte[], IPEndPoint> sendRaw,
        CancellationToken cancellationToken
    ) {
        var endpoint = await ResolveEndpointAsync(discoveryHost);
        if (endpoint is null) return;

        var tokenBytes = EncodeToken(token);
        if (tokenBytes.Length != ExpectedTokenByteLength) {
            Logger.Error(
                $"UdpDiscoveryService: discovery token encoded to {tokenBytes.Length} bytes; expected {ExpectedTokenByteLength}. Aborting discovery."
            );
            return;
        }

        await RunDiscoveryLoopAsync(sendRaw, tokenBytes, endpoint, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves <paramref name="host"/> to an <see cref="IPEndPoint"/> on
    /// <see cref="MmsProtocol.DiscoveryPort"/>. Returns <c>null</c> and logs an
    /// error if DNS resolution yields no addresses.
    /// </summary>
    private static async Task<IPEndPoint?> ResolveEndpointAsync(string host) {
        try {
            var addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);

            if (addresses is { Length: > 0 }) {
                var address = addresses[0];
                foreach (var a in addresses) {
                    if (a.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    address = a;
                    break;
                }
                return new IPEndPoint(address, MmsProtocol.DiscoveryPort);
            }

            Logger.Error($"UdpDiscoveryService: could not resolve host '{host}'");
            return null;
        } catch (Exception ex) when (ex is SocketException or OperationCanceledException) {
            Logger.Warn($"UdpDiscoveryService: DNS resolution failed for '{host}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Encodes <paramref name="token"/> to a UTF-8 byte array.</summary>
    private static byte[] EncodeToken(string token) =>
        Encoding.UTF8.GetBytes(token);

    /// <summary>
    /// Loops, sending <paramref name="tokenBytes"/> to <paramref name="endpoint"/>
    /// every <see cref="MmsProtocol.DiscoveryIntervalMs"/> until
    /// <paramref name="cancellationToken"/> fires or a send error occurs.
    /// </summary>
    private static async Task RunDiscoveryLoopAsync(
        Action<byte[], IPEndPoint> sendRaw,
        byte[] tokenBytes,
        IPEndPoint endpoint,
        CancellationToken cancellationToken
    ) {
        while (!cancellationToken.IsCancellationRequested) {
            TrySend(sendRaw, tokenBytes, endpoint);

            if (!await TryDelayAsync(cancellationToken).ConfigureAwait(false)) return;
        }
    }

    /// <summary>
    /// Attempts a single send. Returns <c>false</c> (and logs a warning) on failure.
    /// </summary>
    private static void TrySend(
        Action<byte[], IPEndPoint> sendRaw,
        byte[] tokenBytes,
        IPEndPoint endpoint
    ) {
        try {
            sendRaw(tokenBytes, endpoint);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            Logger.Warn($"UdpDiscoveryService: send error, aborting – {ex}");
        }
    }

    /// <summary>
    /// Waits for one discovery interval. Returns <c>false</c> when the
    /// cancellation token fires (normal shutdown), <c>true</c> otherwise.
    /// </summary>
    private static async Task<bool> TryDelayAsync(CancellationToken cancellationToken) {
        try {
            await Task.Delay(MmsProtocol.DiscoveryIntervalMs, cancellationToken)
                      .ConfigureAwait(false);
            return true;
        } catch (OperationCanceledException) {
            return false;
        }
    }
}

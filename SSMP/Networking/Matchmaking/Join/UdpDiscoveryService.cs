using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SSMP.Logging;
using SSMP.Networking.Matchmaking.Protocol;

namespace SSMP.Networking.Matchmaking.Join;

/// <summary>Sends UDP discovery pulses to learn external IP/port for NAT hole-punching.</summary>
internal static class UdpDiscoveryService {
    /// <summary>Expected discovery token length in bytes.</summary>
    private const int ExpectedTokenByteLength = 32;

    /// <summary>Resolves endpoint and sends token pulses until cancellation.</summary>
    public static async Task SendUntilCancelledAsync(
        string discoveryHost,
        int discoveryPort,
        string token,
        Action<byte[], IPEndPoint> sendRaw,
        CancellationToken cancellationToken
    ) {
        var endpoint = await ResolveEndpointAsync(discoveryHost, discoveryPort);
        if (endpoint is null) return;

        var tokenBytes = Encoding.UTF8.GetBytes(token);
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
    /// <paramref name="port"/>. Returns <c>null</c> and logs an
    /// error if DNS resolution yields no addresses.
    /// </summary>
    private static async Task<IPEndPoint?> ResolveEndpointAsync(string host, int port) {
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
                return new IPEndPoint(address, port);
            }

            Logger.Error($"UdpDiscoveryService: could not resolve host '{host}'");
            return null;
        } catch (Exception ex) when (ex is SocketException or OperationCanceledException) {
            Logger.Warn($"UdpDiscoveryService: DNS resolution failed for '{host}': {ex.Message}");
            return null;
        }
    }

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
            try {
                sendRaw.Invoke(tokenBytes, endpoint);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                Logger.Warn($"UdpDiscoveryService: send error, aborting – {ex}");
            }

            if (!await TryDelayAsync(cancellationToken)) return;
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

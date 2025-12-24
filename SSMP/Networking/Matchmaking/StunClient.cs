using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using SSMP.Logging;

namespace SSMP.Networking.Matchmaking;

/// <summary>
/// Simple STUN client for discovering the public IP:Port of a UDP socket.
/// Uses the STUN Binding Request/Response as per RFC 5389.
/// </summary>
internal static class StunClient {
    /// <summary>
    /// Default STUN servers to try.
    /// </summary>
    private static readonly string[] StunServers = {
        "stun.l.google.com:19302",
        "stun1.l.google.com:19302",
        "stun2.l.google.com:19302",
        "stun.cloudflare.com:3478"
    };

    /// <summary>
    /// Timeout for STUN requests in milliseconds.
    /// </summary>
    private const int TimeoutMs = 3000;

    /// <summary>
    /// STUN message type: Binding Request
    /// </summary>
    private const ushort BindingRequest = 0x0001;

    /// <summary>
    /// STUN message type: Binding Response
    /// </summary>
    private const ushort BindingResponse = 0x0101;

    /// <summary>
    /// STUN attribute type: XOR-MAPPED-ADDRESS
    /// </summary>
    private const ushort XorMappedAddress = 0x0020;

    /// <summary>
    /// STUN attribute type: MAPPED-ADDRESS (fallback)
    /// </summary>
    private const ushort MappedAddress = 0x0001;

    /// <summary>
    /// STUN magic cookie (RFC 5389)
    /// </summary>
    private const uint MagicCookie = 0x2112A442;

    /// <summary>
    /// Discovers the public endpoint for the given local socket.
    /// Returns (publicIp, publicPort) or null on failure.
    /// </summary>
    public static (string ip, int port)? DiscoverPublicEndpoint(Socket socket) {
        foreach (var server in StunServers) {
            try {
                var result = QueryStunServer(socket, server);
                if (result != null) {
                    Logger.Info($"STUN: Discovered public endpoint {result.Value.ip}:{result.Value.port} via {server}");
                    return result;
                }
            } catch (Exception ex) {
                Logger.Debug($"STUN: Failed with {server}: {ex.Message}");
            }
        }

        Logger.Warn("STUN: Failed to discover public endpoint from all servers");
        return null;
    }

    /// <summary>
    /// Discovers the public endpoint by creating a temporary socket.
    /// </summary>
    public static (string ip, int port)? DiscoverPublicEndpoint(int localPort = 0) {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
        return DiscoverPublicEndpoint(socket);
    }

    /// <summary>
    /// Discovers the public endpoint and returns the socket for reuse.
    /// The caller is responsible for disposing the socket.
    /// </summary>
    public static (string ip, int port, Socket socket)? DiscoverPublicEndpointWithSocket(int localPort = 0) {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
        
        var result = DiscoverPublicEndpoint(socket);
        if (result == null) {
            socket.Dispose();
            return null;
        }
        
        return (result.Value.ip, result.Value.port, socket);
    }

    private static (string ip, int port)? QueryStunServer(Socket socket, string serverAddress) {
        // Parse server address
        var parts = serverAddress.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? int.Parse(parts[1]) : 3478;

        // Resolve hostname - filter for IPv4 only
        var addresses = Dns.GetHostAddresses(host);
        var ipv4Address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        if (ipv4Address == null) return null;

        var serverEndpoint = new IPEndPoint(ipv4Address, port);

        // Build STUN Binding Request
        var request = BuildBindingRequest();

        // Send request
        socket.ReceiveTimeout = TimeoutMs;
        socket.SendTo(request, serverEndpoint);

        // Receive response
        var buffer = new byte[512];
        EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
        var received = socket.ReceiveFrom(buffer, ref remoteEp);

        // Parse response
        return ParseBindingResponse(buffer, received);
    }

    private static byte[] BuildBindingRequest() {
        var request = new byte[20];

        // Message Type: Binding Request (0x0001)
        request[0] = 0;
        request[1] = (BindingRequest & 0xFF);

        // Message Length: 0 (no attributes)
        request[2] = 0;
        request[3] = 0;

        // Magic Cookie
        request[4] = (byte)((MagicCookie >> 24) & 0xFF);
        request[5] = (byte)((MagicCookie >> 16) & 0xFF);
        request[6] = (byte)((MagicCookie >> 8) & 0xFF);
        request[7] = (byte)(MagicCookie & 0xFF);

        // Transaction ID (12 random bytes)
        var random = new Random();
        for (var i = 8; i < 20; i++) {
            request[i] = (byte)random.Next(256);
        }

        return request;
    }

    private static (string ip, int port)? ParseBindingResponse(byte[] buffer, int length) {
        if (length < 20) return null;

        // Check message type
        var messageType = (ushort)((buffer[0] << 8) | buffer[1]);
        if (messageType != BindingResponse) return null;

        // Verify magic cookie
        var cookie = (uint)((buffer[4] << 24) | (buffer[5] << 16) | (buffer[6] << 8) | buffer[7]);
        if (cookie != MagicCookie) return null;

        // Parse attributes
        var messageLength = (buffer[2] << 8) | buffer[3];
        var offset = 20;

        while (offset + 4 <= 20 + messageLength && offset + 4 <= length) {
            var attrType = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
            var attrLength = (buffer[offset + 2] << 8) | buffer[offset + 3];
            offset += 4;

            if (offset + attrLength > length) break;

            if (attrType == XorMappedAddress && attrLength >= 8) {
                // XOR-MAPPED-ADDRESS
                var family = buffer[offset + 1];
                if (family == 0x01) { // IPv4
                    var xPort = (ushort)((buffer[offset + 2] << 8) | buffer[offset + 3]);
                    var port = xPort ^ (ushort)(MagicCookie >> 16);

                    var xIp = new byte[4];
                    xIp[0] = (byte)(buffer[offset + 4] ^ ((MagicCookie >> 24) & 0xFF));
                    xIp[1] = (byte)(buffer[offset + 5] ^ ((MagicCookie >> 16) & 0xFF));
                    xIp[2] = (byte)(buffer[offset + 6] ^ ((MagicCookie >> 8) & 0xFF));
                    xIp[3] = (byte)(buffer[offset + 7] ^ (MagicCookie & 0xFF));

                    var ip = new IPAddress(xIp).ToString();
                    return (ip, port);
                }
            } else if (attrType == MappedAddress && attrLength >= 8) {
                // MAPPED-ADDRESS (fallback for older servers)
                var family = buffer[offset + 1];
                if (family == 0x01) { // IPv4
                    var port = (buffer[offset + 2] << 8) | buffer[offset + 3];
                    var ip = new IPAddress(new[] { buffer[offset + 4], buffer[offset + 5], buffer[offset + 6], buffer[offset + 7] }).ToString();
                    return (ip, port);
                }
            }

            // Move to next attribute (4-byte aligned)
            offset += attrLength;
            if (attrLength % 4 != 0) {
                offset += 4 - (attrLength % 4);
            }
        }

        return null;
    }
}

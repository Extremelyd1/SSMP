using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using SSMP.Logging;

namespace SSMP.Networking.Matchmaking;

/// <summary>
/// High-performance STUN client for discovering the public IP:Port of a UDP socket.
/// Uses the STUN Binding Request/Response as per RFC 5389.
/// </summary>
/// <remarks>
/// <para>
/// STUN (Session Traversal Utilities for NAT) allows clients behind NAT to discover their
/// public-facing IP address and port. This is essential for peer-to-peer networking and
/// NAT hole-punching.
/// </para>
/// </remarks>
internal static class StunClient {
    /// <summary>
    /// List of public STUN servers to try in order.
    /// Includes Google and Cloudflare STUN servers for redundancy.
    /// </summary>
    private static readonly string[] StunServers = [
        "stun.l.google.com:19302",
        "stun1.l.google.com:19302",
        "stun2.l.google.com:19302",
        "stun.cloudflare.com:3478"
    ];

    /// <summary>
    /// Timeout for STUN server responses in milliseconds.
    /// 3 seconds balances reliability with responsiveness.
    /// </summary>
    private const int TimeoutMs = 3000;
    
    /// <summary>
    /// STUN message type for Binding Request (0x0001).
    /// </summary>
    private const ushort BindingRequest = 0x0001;
    
    /// <summary>
    /// STUN message type for Binding Response (0x0101).
    /// </summary>
    private const ushort BindingResponse = 0x0101;
    
    /// <summary>
    /// STUN attribute type for XOR-MAPPED-ADDRESS (0x0020).
    /// Preferred attribute that XORs the address with magic cookie for obfuscation.
    /// </summary>
    private const ushort XorMappedAddress = 0x0020;
    
    /// <summary>
    /// STUN attribute type for MAPPED-ADDRESS (0x0001).
    /// Legacy attribute with plain address (no XOR).
    /// </summary>
    private const ushort MappedAddress = 0x0001;
    
    /// <summary>
    /// STUN magic cookie (0x2112A442) as defined in RFC 5389.
    /// Used to distinguish STUN packets from other protocols and for XOR operations.
    /// </summary>
    private const uint MagicCookie = 0x2112A442;
    
    /// <summary>
    /// Size of STUN message header in bytes (20 bytes).
    /// </summary>
    private const int StunHeaderSize = 20;
    
    /// <summary>
    /// Buffer size for STUN responses (512 bytes).
    /// Sufficient for typical STUN response with attributes.
    /// </summary>
    private const int StunBufferSize = 512;
    
    /// <summary>
    /// Default STUN server port (3478) when not specified in server address.
    /// </summary>
    private const int DefaultStunPort = 3478;

    /// <summary>
    /// Thread-local request buffer to avoid repeated allocations.
    /// Each thread gets its own buffer for thread-safety without locking.
    /// </summary>
    [ThreadStatic] private static byte[]? _requestBuffer;
    
    /// <summary>
    /// Thread-local response buffer to avoid repeated allocations.
    /// </summary>
    [ThreadStatic] private static byte[]? _responseBuffer;
    
    /// <summary>
    /// Thread-local random number generator for transaction IDs.
    /// Thread-static ensures thread-safety without locking.
    /// </summary>
    [ThreadStatic] private static Random? _random;

    /// <summary>
    /// Optional pre-bound socket for STUN discovery.
    /// When set, this socket is used instead of creating a temporary one.
    /// Useful for reusing the same socket that will be used for actual communication.
    /// </summary>
    public static Socket? PreBoundSocket { get; set; }

    /// <summary>
    /// Discovers the public endpoint (IP and port) visible to STUN servers.
    /// Tries multiple STUN servers until one succeeds.
    /// </summary>
    /// <param name="socket">The socket to use for STUN discovery</param>
    /// <returns>Tuple of (ip, port) if successful, null otherwise</returns>
    private static (string ip, int port)? DiscoverPublicEndpoint(Socket socket) {
        // Try each STUN server in order until one succeeds
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
    /// Discovers the public endpoint using a temporary socket bound to the specified local port.
    /// The temporary socket is disposed after discovery.
    /// </summary>
    /// <param name="localPort">Local port to bind to (0 for any available port)</param>
    /// <returns>Tuple of (ip, port) if successful, null otherwise</returns>
    public static (string ip, int port)? DiscoverPublicEndpoint(int localPort = 0) {
        // Create temporary socket for STUN discovery
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
        return DiscoverPublicEndpoint(socket);
    }

    /// <summary>
    /// Discovers the public endpoint and returns both the endpoint and the socket.
    /// The socket is NOT disposed - caller is responsible for disposal.
    /// Useful when you want to reuse the socket after STUN discovery.
    /// </summary>
    /// <param name="localPort">Local port to bind to (0 for any available port)</param>
    /// <returns>Tuple of (ip, port, socket) if successful, null otherwise</returns>
    public static (string ip, int port, Socket socket)? DiscoverPublicEndpointWithSocket(int localPort = 0) {
        // Create socket that caller will own
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
        
        var result = DiscoverPublicEndpoint(socket);
        if (result == null) {
            socket.Dispose();
            return null;
        }
        
        return (result.Value.ip, result.Value.port, socket);
    }

    /// <summary>
    /// Queries a single STUN server to discover the public endpoint.
    /// Sends a STUN Binding Request and parses the response.
    /// </summary>
    /// <param name="socket">Socket to use for communication</param>
    /// <param name="serverAddress">STUN server address (host:port format)</param>
    /// <returns>Tuple of (ip, port) if successful, null otherwise</returns>
    private static (string ip, int port)? QueryStunServer(Socket socket, string serverAddress) {
        // Parse server address using Span to avoid string allocations
        var colonIndex = serverAddress.IndexOf(':');
        var host = colonIndex >= 0 
            ? serverAddress.AsSpan(0, colonIndex)
            : serverAddress.AsSpan();
        
        // Extract port from address or use default
        var port = colonIndex >= 0 && colonIndex + 1 < serverAddress.Length
            ? int.Parse(serverAddress.AsSpan(colonIndex + 1))
            : DefaultStunPort;

        // Resolve hostname to IP address
        var addresses = Dns.GetHostAddresses(host.ToString());
        
        // Find first IPv4 address (manual loop avoids LINQ allocation)
        IPAddress? ipv4Address = null;
        for (var i = 0; i < addresses.Length; i++) {
            if (addresses[i].AddressFamily == AddressFamily.InterNetwork) {
                ipv4Address = addresses[i];
                break;
            }
        }

        if (ipv4Address == null) return null;

        var serverEndpoint = new IPEndPoint(ipv4Address, port);

        // Get or allocate thread-local buffers (allocated once per thread)
        _requestBuffer ??= new byte[StunHeaderSize];
        _responseBuffer ??= new byte[StunBufferSize];

        // Build STUN Binding Request directly in buffer
        BuildBindingRequest(_requestBuffer);

        // Configure socket timeout and send request
        socket.ReceiveTimeout = TimeoutMs;
        socket.SendTo(_requestBuffer, 0, StunHeaderSize, SocketFlags.None, serverEndpoint);

        // Receive response from STUN server
        EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
        var received = socket.ReceiveFrom(_responseBuffer, ref remoteEp);

        // Parse the response to extract public endpoint
        return ParseBindingResponse(_responseBuffer.AsSpan(0, received));
    }

    /// <summary>
    /// Builds a STUN Binding Request message in the provided buffer.
    /// The request has no attributes, just a header with transaction ID.
    /// </summary>
    /// <param name="request">Span to write the request into (must be at least 20 bytes)</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BuildBindingRequest(Span<byte> request) {
        // Write message type (Binding Request = 0x0001)
        request[0] = 0;
        request[1] = BindingRequest & 0xFF;

        // Write message length (0 = no attributes)
        request[2] = 0;
        request[3] = 0;

        // Write magic cookie in big-endian format
        WriteUInt32BigEndian(request.Slice(4), MagicCookie);

        // Generate random 12-byte transaction ID
        _random ??= new Random();
        for (var i = 8; i < StunHeaderSize; i++) {
            request[i] = (byte)_random.Next(256);
        }
    }

    /// <summary>
    /// Writes a 32-bit unsigned integer to a buffer in big-endian (network) byte order.
    /// </summary>
    /// <param name="buffer">Buffer to write to (must be at least 4 bytes)</param>
    /// <param name="value">Value to write</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteUInt32BigEndian(Span<byte> buffer, uint value) {
        buffer[0] = (byte)(value >> 24);
        buffer[1] = (byte)(value >> 16);
        buffer[2] = (byte)(value >> 8);
        buffer[3] = (byte)value;
    }

    /// <summary>
    /// Reads a 16-bit unsigned integer from a buffer in big-endian (network) byte order.
    /// </summary>
    /// <param name="buffer">Buffer to read from (must be at least 2 bytes)</param>
    /// <returns>The parsed 16-bit value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> buffer) {
        return (ushort)((buffer[0] << 8) | buffer[1]);
    }

    /// <summary>
    /// Reads a 32-bit unsigned integer from a buffer in big-endian (network) byte order.
    /// </summary>
    /// <param name="buffer">Buffer to read from (must be at least 4 bytes)</param>
    /// <returns>The parsed 32-bit value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> buffer) {
        return (uint)((buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3]);
    }

    /// <summary>
    /// Parses a STUN Binding Response message to extract the public endpoint.
    /// Supports both XOR-MAPPED-ADDRESS and MAPPED-ADDRESS attributes.
    /// </summary>
    /// <param name="buffer">Buffer containing the STUN response</param>
    /// <returns>Tuple of (ip, port) if successfully parsed, null otherwise</returns>
    private static (string ip, int port)? ParseBindingResponse(ReadOnlySpan<byte> buffer) {
        // Validate minimum length
        if (buffer.Length < StunHeaderSize) return null;

        // Verify this is a Binding Response message
        var messageType = ReadUInt16BigEndian(buffer);
        if (messageType != BindingResponse) return null;

        // Verify magic cookie to ensure valid STUN message
        var cookie = ReadUInt32BigEndian(buffer[4..]);
        if (cookie != MagicCookie) return null;

        // Get message length (payload after header)
        var messageLength = ReadUInt16BigEndian(buffer[2..]);
        var offset = StunHeaderSize;
        var endOffset = StunHeaderSize + messageLength;

        // Parse attributes in the message
        while (offset + 4 <= endOffset && offset + 4 <= buffer.Length) {
            // Read attribute type and length
            var attrType = ReadUInt16BigEndian(buffer[offset..]);
            var attrLength = ReadUInt16BigEndian(buffer[(offset + 2)..]);
            offset += 4;

            // Validate attribute doesn't exceed buffer
            if (offset + attrLength > buffer.Length) break;

            // Parse XOR-MAPPED-ADDRESS (preferred)
            if (attrType == XorMappedAddress && attrLength >= 8) {
                var result = ParseXorMappedAddress(buffer.Slice(offset, attrLength));
                if (result != null) return result;
            } 
            // Parse MAPPED-ADDRESS (fallback for older servers)
            else if (attrType == MappedAddress && attrLength >= 8) {
                var result = ParseMappedAddress(buffer.Slice(offset, attrLength));
                if (result != null) return result;
            }

            // Move to next attribute (attributes are 4-byte aligned)
            offset += attrLength;
            var padding = (4 - (attrLength % 4)) % 4;
            offset += padding;
        }

        return null;
    }

    /// <summary>
    /// Parses an XOR-MAPPED-ADDRESS attribute to extract the public endpoint.
    /// The address is XORed with the magic cookie for obfuscation.
    /// </summary>
    /// <param name="attr">Buffer containing the attribute value</param>
    /// <returns>Tuple of (ip, port) if successfully parsed, null otherwise</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (string ip, int port)? ParseXorMappedAddress(ReadOnlySpan<byte> attr) {
        // Verify this is IPv4 (family = 0x01)
        var family = attr[1];
        if (family != 0x01) return null;

        // Extract port by XORing with upper 16 bits of magic cookie
        var xPort = ReadUInt16BigEndian(attr[2..]);
        var port = xPort ^ (ushort)(MagicCookie >> 16);

        // Extract IP address by XORing each byte with magic cookie
        Span<byte> ipBytes = stackalloc byte[4];
        ipBytes[0] = (byte)(attr[4] ^ (MagicCookie >> 24));
        ipBytes[1] = (byte)(attr[5] ^ (MagicCookie >> 16));
        ipBytes[2] = (byte)(attr[6] ^ (MagicCookie >> 8));
        ipBytes[3] = (byte)(attr[7] ^ MagicCookie);

        var ip = new IPAddress(ipBytes).ToString();
        return (ip, port);
    }

    /// <summary>
    /// Parses a MAPPED-ADDRESS attribute to extract the public endpoint.
    /// This is the legacy format with no XOR obfuscation.
    /// </summary>
    /// <param name="attr">Buffer containing the attribute value</param>
    /// <returns>Tuple of (ip, port) if successfully parsed, null otherwise</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (string ip, int port)? ParseMappedAddress(ReadOnlySpan<byte> attr) {
        // Verify this is IPv4 (family = 0x01)
        var family = attr[1];
        if (family != 0x01) return null;

        // Extract port directly (no XOR)
        var port = ReadUInt16BigEndian(attr[2..]);
        
        // Extract IP address directly (no XOR)
        Span<byte> ipBytes = stackalloc byte[4];
        attr.Slice(4, 4).CopyTo(ipBytes);

        var ip = new IPAddress(ipBytes).ToString();
        return (ip, port);
    }
}

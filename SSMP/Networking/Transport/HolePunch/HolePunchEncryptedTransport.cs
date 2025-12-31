using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SSMP.Logging;
using SSMP.Networking.Client;
using SSMP.Networking.Transport.Common;
using SSMP.Ui;

namespace SSMP.Networking.Transport.HolePunch;

/// <summary>
/// UDP Hole Punch implementation of <see cref="IEncryptedTransport"/>.
/// Performs NAT traversal before establishing DTLS connection for secure peer-to-peer networking.
/// </summary>
/// <remarks>
/// <para>
/// This transport layer combines NAT hole-punching with DTLS encryption to enable
/// secure peer-to-peer connections between clients behind NAT/firewalls.
/// </para>
/// <para>
/// NAT Hole Punching Process:
/// 1. Client creates a socket and registers with MMS (which sees public endpoint)
/// 2. Client sends "punch" packets to peer's public endpoint
/// 3. These packets open a hole in the local NAT mapping
/// 4. Peer's packets can now reach through the opened NAT hole
/// 5. DTLS handshake proceeds over the established UDP path
/// </para>
/// <para>
/// The transport handles both:
/// - Remote connections: Full hole-punching and DTLS
/// - Local connections: Direct DTLS without hole-punching
/// </para>
/// </remarks>
internal class HolePunchEncryptedTransport : IEncryptedTransport {
    /// <summary>
    /// Maximum UDP packet size to avoid IP fragmentation.
    /// Set to 1200 bytes to safely fit within MTU after IP/UDP/DTLS headers.
    /// </summary>
    private const int UdpMaxPacketSize = 1200;

    /// <summary>
    /// Number of punch packets to send during NAT traversal.
    /// Set to 100 packets (5 seconds total) to cover MMS polling latency.
    /// </summary>
    /// <remarks>
    /// High count ensures:
    /// - NAT mapping stays open long enough for peer to respond
    /// - Covers the time for MMS to notify host of pending client
    /// - Compensates for packet loss during hole-punching
    /// </remarks>
    private const int PunchPacketCount = 100;

    /// <summary>
    /// Delay between consecutive punch packets in milliseconds.
    /// 50ms provides good balance between NAT mapping refresh and network overhead.
    /// </summary>
    private const int PunchPacketDelayMs = 50;

    /// <summary>
    /// The IP address used for self-connecting (host connecting to own server).
    /// Localhost connections bypass hole-punching as no NAT traversal is needed.
    /// </summary>
    private const string LocalhostAddress = "127.0.0.1";

    /// <summary>
    /// Pre-allocated punch packet bytes containing "PUNCH" in UTF-8.
    /// Reused across all punch operations to avoid allocations.
    /// </summary>
    /// <remarks>
    /// The actual content doesn't matter - we just need to send packets
    /// to establish the NAT mapping. "PUNCH" is used for debugging clarity.
    /// </remarks>
    private static readonly byte[] PunchPacket = "PUNCH"u8.ToArray();

    /// <summary>
    /// The underlying DTLS client that provides encrypted communication.
    /// Handles encryption, decryption, and secure handshaking.
    /// </summary>
    private readonly DtlsClient _dtlsClient;

    /// <summary>
    /// Event raised when encrypted data is received from the peer.
    /// Data has already been decrypted by the DTLS layer.
    /// </summary>
    public event Action<byte[], int>? DataReceivedEvent;

    /// <summary>
    /// Indicates whether this transport requires congestion management.
    /// UDP provides no congestion control, so higher layers must implement it.
    /// </summary>
    public bool RequiresCongestionManagement => true;

    /// <summary>
    /// Indicates whether this transport requires reliability mechanisms.
    /// UDP is unreliable, so higher layers must implement retransmission.
    /// </summary>
    public bool RequiresReliability => true;

    /// <summary>
    /// Indicates whether this transport requires sequencing mechanisms.
    /// UDP doesn't guarantee ordering, so higher layers must sequence packets.
    /// </summary>
    public bool RequiresSequencing => true;

    /// <summary>
    /// Gets the maximum packet size that can be safely transmitted.
    /// Limited by MTU considerations to avoid fragmentation.
    /// </summary>
    public int MaxPacketSize => UdpMaxPacketSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="HolePunchEncryptedTransport"/> class.
    /// Sets up the DTLS client and subscribes to its data events.
    /// </summary>
    public HolePunchEncryptedTransport() {
        _dtlsClient = new DtlsClient();

        // Forward decrypted data from DTLS to our event subscribers
        _dtlsClient.DataReceivedEvent += OnDataReceived;
    }

    /// <summary>
    /// Connects to the specified remote endpoint with NAT traversal.
    /// Performs hole-punching for remote connections, direct connection for localhost.
    /// </summary>
    /// <param name="address">The IP address to connect to</param>
    /// <param name="port">The port to connect to</param>
    /// <remarks>
    /// Connection process:
    /// - Localhost: Direct DTLS connection (no hole-punching needed)
    /// - Remote: Hole-punch first, then DTLS connection over punched socket
    /// </remarks>
    public void Connect(string address, int port) {
        // Detect self-connect or LAN scenario
        if (address == LocalhostAddress || IsPrivateIp(address)) {
            Logger.Debug($"HolePunch: Local/LAN connection detected ({address}), using direct DTLS");

            // We don't need the pre-bound socket for LAN, so clean it up
            if (ConnectInterface.HolePunchSocket != null) {
                ConnectInterface.HolePunchSocket.Close();
                ConnectInterface.HolePunchSocket = null;
            }

            // No hole-punching needed for localhost/LAN
            _dtlsClient.Connect(address, port);
            return;
        }

        // Remote connection requires NAT traversal
        Logger.Info($"HolePunch: Starting NAT traversal to {address}:{port}");
        var socket = PerformHolePunch(address, port);

        // Establish DTLS connection using the hole-punched socket
        _dtlsClient.Connect(address, port, socket);
    }

    /// <summary>
    /// Checks if an IP address is a private (LAN) address.
    /// </summary>
    private static bool IsPrivateIp(string ipAddress) {
        if (!IPAddress.TryParse(ipAddress, out var ip)) return false;

        var bytes = ip.GetAddressBytes();

        return bytes[0] switch {
            10 => true, // 10.0.0.0/8
            172 => bytes[1] >= 16 && bytes[1] <= 31, // 172.16.0.0/12
            192 => bytes[1] == 168, // 192.168.0.0/16
            _ => false
        };
    }

    /// <summary>
    /// Sends encrypted data to the connected peer.
    /// Data is encrypted by the DTLS layer before transmission.
    /// </summary>
    /// <param name="buffer">Buffer containing data to send</param>
    /// <param name="offset">Offset in buffer where data begins</param>
    /// <param name="length">Number of bytes to send</param>
    /// <exception cref="InvalidOperationException">Thrown if not connected</exception>
    public void Send(byte[] buffer, int offset, int length) {
        // Ensure DTLS connection is established
        if (_dtlsClient.DtlsTransport == null) {
            throw new InvalidOperationException("Not connected");
        }

        // Delegate to DTLS transport for encryption and transmission
        _dtlsClient.DtlsTransport.Send(buffer, offset, length);
    }

    /// <summary>
    /// Disconnects from the peer and cleans up resources.
    /// Closes the DTLS session and underlying socket.
    /// </summary>
    public void Disconnect() {
        _dtlsClient.Disconnect();
    }

    /// <summary>
    /// Performs UDP hole punching to the specified endpoint.
    /// Opens NAT mapping by sending packets, then returns connected socket for DTLS.
    /// </summary>
    /// <param name="address">Target IP address</param>
    /// <param name="port">Target port</param>
    /// <returns>Connected UDP socket ready for DTLS communication</returns>
    /// <exception cref="InvalidOperationException">Thrown if hole punching fails</exception>
    /// <remarks>
    /// Hole-punching sequence:
    /// 1. Reuse pre-bound socket from STUN discovery (or create new one)
    /// 2. Configure socket to ignore ICMP Port Unreachable errors
    /// 3. Send 100 "PUNCH" packets over 5 seconds to open NAT mapping
    /// 4. Connect socket to peer endpoint
    /// 5. Return socket for DTLS handshake
    /// </remarks>
    private static Socket PerformHolePunch(string address, int port) {
        // Attempt to reuse the socket from ConnectInterface
        // This is important because the NAT mapping was created with this socket
        var socket = ConnectInterface.HolePunchSocket;
        ConnectInterface.HolePunchSocket = null;

        if (socket == null) {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            Logger.Warn("HolePunch: No pre-bound socket, creating new one (NAT traversal may fail)");
        }

        // Suppress ICMP Port Unreachable errors (SIO_UDP_CONNRESET)
        // When we send to a port that's not open yet, we get ICMP errors
        // These would normally cause SocketException, but we want to ignore them
        try {
            const int sioUdpConnReset = -1744830452;
            socket.IOControl(sioUdpConnReset, [0], null);
        } catch {
            // Some platforms don't support this option, continue anyway
            Logger.Warn("HolePunch: Failed to set SioUdpConnReset (ignored platform?)");
        }

        try {
            // Parse target endpoint
            var endpoint = new IPEndPoint(IPAddress.Parse(address), port);

            Logger.Debug($"HolePunch: Sending {PunchPacketCount} punch packets to {endpoint}");

            // Send punch packets to create/maintain NAT mapping
            // Each packet refreshes the NAT timer and increases chance of success
            for (var i = 0; i < PunchPacketCount; i++) {
                socket.SendTo(PunchPacket, endpoint);

                // Wait between packets to spread them over time
                Thread.Sleep(PunchPacketDelayMs);
            }

            // "Connect" the socket to filter incoming packets to only this peer
            // This is important for DTLS which expects point-to-point communication
            socket.Connect(endpoint);

            Logger.Info($"HolePunch: NAT traversal complete, socket connected to {endpoint}");
            return socket;
        } catch (Exception ex) {
            // Clean up socket on failure
            socket.Dispose();
            throw new InvalidOperationException($"Hole punch failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Handles data received from the DTLS client.
    /// Forwards decrypted data to subscribers of <see cref="DataReceivedEvent"/>.
    /// </summary>
    /// <param name="data">Buffer containing received data</param>
    /// <param name="length">Number of valid bytes in buffer</param>
    private void OnDataReceived(byte[] data, int length) {
        // Forward to subscribers
        DataReceivedEvent?.Invoke(data, length);
    }
}

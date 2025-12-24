using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SSMP.Logging;
using SSMP.Networking.Client;
using SSMP.Networking.Matchmaking;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.HolePunch;

/// <summary>
/// UDP Hole Punch implementation of <see cref="IEncryptedTransport"/>.
/// Performs NAT traversal before establishing DTLS connection.
/// </summary>
internal class HolePunchEncryptedTransport : IEncryptedTransport {
    /// <summary>
    /// Maximum UDP packet size to avoid fragmentation.
    /// </summary>
    private const int UdpMaxPacketSize = 1200;

    /// <summary>
    /// Number of punch packets to send.
    /// Increased to 100 (5s) to cover MMS polling latency.
    /// </summary>
    private const int PunchPacketCount = 100;

    /// <summary>
    /// Delay between punch packets in milliseconds.
    /// </summary>
    private const int PunchPacketDelayMs = 50;

    /// <summary>
    /// Timeout for hole punch in milliseconds.
    /// </summary>
    private const int PunchTimeoutMs = 5000;

    /// <summary>
    /// The address used for self-connecting (host connecting to own server).
    /// </summary>
    private const string LocalhostAddress = "127.0.0.1";

    /// <summary>
    /// The underlying DTLS client.
    /// </summary>
    private readonly DtlsClient _dtlsClient;

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceivedEvent;

    /// <inheritdoc />
    public bool RequiresCongestionManagement => true;

    /// <inheritdoc />
    public bool RequiresReliability => true;

    /// <inheritdoc />
    public bool RequiresSequencing => true;

    /// <inheritdoc />
    public int MaxPacketSize => UdpMaxPacketSize;

    public HolePunchEncryptedTransport() {
        _dtlsClient = new DtlsClient();
        _dtlsClient.DataReceivedEvent += OnDataReceived;
    }

    /// <inheritdoc />
    public void Connect(string address, int port) {
        // Self-connect (host connecting to own server) uses direct connection
        if (address == LocalhostAddress) {
            Logger.Debug("HolePunch: Self-connect detected, using direct DTLS");
            _dtlsClient.Connect(address, port);
            return;
        }

        // Perform hole punch for remote connections
        Logger.Info($"HolePunch: Starting NAT traversal to {address}:{port}");
        var socket = PerformHolePunch(address, port);
        
        // Connect DTLS using the punched socket
        _dtlsClient.Connect(address, port, socket);
    }

    /// <inheritdoc />
    public void Send(byte[] buffer, int offset, int length) {
        if (_dtlsClient.DtlsTransport == null) {
            throw new InvalidOperationException("Not connected");
        }

        _dtlsClient.DtlsTransport.Send(buffer, offset, length);
    }

    /// <inheritdoc />
    public void Disconnect() {
        _dtlsClient.Disconnect();
    }

    /// <summary>
    /// Performs UDP hole punching to the specified endpoint.
    /// Uses pre-bound socket from ClientSocketHolder if available.
    /// </summary>
    private Socket PerformHolePunch(string address, int port) {
        // Use pre-bound socket from STUN discovery if available
        var socket = ClientSocketHolder.PreBoundSocket;
        ClientSocketHolder.PreBoundSocket = null; // Consume it
        
        if (socket == null) {
            // Fallback: create new socket (won't work with NAT coordination, but OK for testing)
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            Logger.Warn("HolePunch: No pre-bound socket, creating new one (NAT traversal may fail)");
        }
        
        // Suppress ICMP Port Unreachable (ConnectionReset) errors
        // This is critical for hole punching as early packets often trigger ICMP errors
        try {
            const int SioUdpConnReset = -1744830452; // 0x9800000C
            socket.IOControl(SioUdpConnReset, new byte[] { 0 }, null);
        } catch {
            Logger.Warn("HolePunch: Failed to set SioUdpConnReset (ignored platform?)");
        }
        
        try {
            var endpoint = new IPEndPoint(IPAddress.Parse(address), port);
            var punchPacket = new byte[] { 0x50, 0x55, 0x4E, 0x43, 0x48 }; // "PUNCH"

            Logger.Debug($"HolePunch: Sending {PunchPacketCount} punch packets to {endpoint}");

            // Send punch packets to open our NAT
            for (var i = 0; i < PunchPacketCount; i++) {
                socket.SendTo(punchPacket, endpoint);
                Thread.Sleep(PunchPacketDelayMs);
            }

            // "Connect" the socket to the endpoint for DTLS
            socket.Connect(endpoint);
            
            Logger.Info($"HolePunch: NAT traversal complete, socket connected to {endpoint}");
            return socket;
        } catch (Exception ex) {
            socket.Dispose();
            throw new InvalidOperationException($"Hole punch failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Raises the <see cref="DataReceivedEvent"/> with the given data.
    /// </summary>
    private void OnDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

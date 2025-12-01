using System;
using SSMP.Networking.Client;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.HolePunch;

/// <summary>
/// UDP Hole Punching implementation of <see cref="IEncryptedTransport"/>.
/// Wraps DtlsClient with Master Server NAT traversal coordination.
/// </summary>
internal class HolePunchEncryptedTransport : IEncryptedTransport {
    /// <summary>
    /// Master server address for NAT traversal coordination.
    /// </summary>
    private readonly string _masterServerAddress;
    /// <summary>
    /// The underlying DTLS client that is used once P2P connection has been established.
    /// </summary>
    private DtlsClient? _dtlsClient;

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceivedEvent;

    /// <inheritdoc />
    public bool RequiresCongestionManagement => true;

    /// <summary>
    /// Construct a hole punching transport with the given master server address.
    /// </summary>
    /// <param name="masterServerAddress">Master server address for NAT traversal coordination.</param>
    public HolePunchEncryptedTransport(string masterServerAddress) {
        _masterServerAddress = masterServerAddress;
    }

    /// <summary>
    /// Connect to remote peer via UDP hole punching.
    /// </summary>
    /// <param name="address">LobbyID or PeerID to be resolved via Master Server.</param>
    /// <param name="port">Port parameter (resolved via Master Server).</param>
    public void Connect(string address, int port) {
        // TODO: Implementation steps:
        // 1. Contact Master Server with LobbyID/PeerID to get peer's public IP:Port
        // 2. Perform UDP hole punching (simultaneous send from both sides)
        // 3. Once NAT hole is established, wrap with DtlsClient:
        //    _dtlsClient = new DtlsClient();
        //    _dtlsClient.DataReceivedEvent += OnDataReceived;
        //    _dtlsClient.Connect(resolvedIp, resolvedPort);
        throw new NotImplementedException("UDP Hole Punching transport not yet implemented");
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public void Send(byte[] buffer, int offset, int length) {
        if (_dtlsClient?.DtlsTransport == null) {
            throw new InvalidOperationException("Not connected");
        }

        _dtlsClient.DtlsTransport.Send(buffer, offset, length);
    }

    /// <inheritdoc />
    public int Receive(byte[]? buffer, int offset, int length, int waitMillis) {
        if (_dtlsClient?.DtlsTransport == null) {
            throw new InvalidOperationException("Not connected");
        }

        if (buffer == null) {
            return 0;
        }

        return _dtlsClient.DtlsTransport.Receive(buffer, offset, length, waitMillis);
    }

    /// <inheritdoc />
    public void Disconnect() {
        _dtlsClient?.Disconnect();
        _dtlsClient = null;
    }

    /// <summary>
    /// Raises the <see cref="DataReceivedEvent"/> with the given data.
    /// </summary>
    private void OnDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

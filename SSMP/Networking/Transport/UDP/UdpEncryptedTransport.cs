using System;
using SSMP.Networking.Client;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.UDP;

/// <summary>
/// UDP+DTLS implementation of <see cref="IEncryptedTransport"/> that wraps DtlsClient.
/// </summary>
internal class UdpEncryptedTransport : IEncryptedTransport {
    /// <summary>
    /// The underlying DTLS client.
    /// </summary>
    private readonly DtlsClient _dtlsClient;

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceivedEvent;

    /// <inheritdoc />
    public bool RequiresCongestionManagement => true;

    public UdpEncryptedTransport() {
        _dtlsClient = new DtlsClient();
        _dtlsClient.DataReceivedEvent += OnDataReceived;
    }

    /// <inheritdoc />
    public void Connect(string address, int port) {
        _dtlsClient.Connect(address, port);
    }

    /// <inheritdoc />
    public void Send(byte[] buffer, int offset, int length) {
        if (_dtlsClient.DtlsTransport == null) {
            throw new InvalidOperationException("Not connected");
        }

        _dtlsClient.DtlsTransport.Send(buffer, offset, length);
    }

    /// <inheritdoc />
    public int Receive(byte[] buffer, int offset, int length, int waitMillis) {
        if (_dtlsClient.DtlsTransport == null) {
            throw new InvalidOperationException("Not connected");
        }

        return _dtlsClient.DtlsTransport.Receive(buffer, offset, length, waitMillis);
    }

    /// <inheritdoc />
    public void Disconnect() {
        _dtlsClient.Disconnect();
    }

    /// <summary>
    /// Raises the <see cref="DataReceivedEvent"/> with the given data.
    /// </summary>
    private void OnDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

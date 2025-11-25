using System;
using SSMP.Networking.Client;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.UDP;

/// <summary>
/// UDP+DTLS implementation of IEncryptedTransport that wraps DtlsClient.
/// </summary>
internal class UdpEncryptedTransport : IEncryptedTransport {
    private readonly DtlsClient _dtlsClient;

    public event Action<byte[], int>? DataReceivedEvent;

    public UdpEncryptedTransport() {
        _dtlsClient = new DtlsClient();
        _dtlsClient.DataReceivedEvent += OnDataReceived;
    }

    public void Connect(string address, int port) {
        _dtlsClient.Connect(address, port);
    }

    public int Send(byte[] buffer, int offset, int length) {
        if (_dtlsClient.DtlsTransport == null) {
            throw new InvalidOperationException("Not connected");
        }

        _dtlsClient.DtlsTransport.Send(buffer, offset, length);
        return length;
    }

    public int Receive(byte[] buffer, int offset, int length, int waitMillis) {
        if (_dtlsClient.DtlsTransport == null) {
            throw new InvalidOperationException("Not connected");
        }

        return _dtlsClient.DtlsTransport.Receive(buffer, offset, length, waitMillis);
    }

    public void Disconnect() {
        _dtlsClient.Disconnect();
    }

    private void OnDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

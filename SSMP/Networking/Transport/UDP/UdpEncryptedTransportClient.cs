using System;
using System.Net;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.UDP;

/// <summary>
/// UDP+DTLS implementation of IEncryptedTransportClient.
/// </summary>
internal class UdpEncryptedTransportClient : IEncryptedTransportClient {
    private readonly DtlsServerClient _dtlsServerClient;

    public string ClientIdentifier => _dtlsServerClient.EndPoint.ToString();
    public IPEndPoint EndPoint => _dtlsServerClient.EndPoint;

    public event Action<byte[], int>? DataReceivedEvent;

    public UdpEncryptedTransportClient(DtlsServerClient dtlsServerClient) {
        _dtlsServerClient = dtlsServerClient;
    }

    public void Send(byte[] buffer, int offset, int length) {
        _dtlsServerClient.DtlsTransport.Send(buffer, offset, length);
    }

    /// <summary>
    /// Internal method to raise the DataReceivedEvent from the server.
    /// </summary>
    internal void RaiseDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

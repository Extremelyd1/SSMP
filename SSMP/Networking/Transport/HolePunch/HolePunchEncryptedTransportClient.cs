using System;
using System.Net;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.HolePunch;

/// <summary>
/// UDP Hole Punching implementation of IEncryptedTransportClient.
/// Wraps DtlsServerClient for hole punched connections.
/// </summary>
internal class HolePunchEncryptedTransportClient : IEncryptedTransportClient {
    private readonly DtlsServerClient _dtlsServerClient;

    public string ClientIdentifier => _dtlsServerClient.EndPoint.ToString();
    public IPEndPoint EndPoint => _dtlsServerClient.EndPoint;
    
    public event Action<byte[], int>? DataReceivedEvent;

    public HolePunchEncryptedTransportClient(DtlsServerClient dtlsServerClient) {
        _dtlsServerClient = dtlsServerClient;
    }

    public void Send(byte[] buffer, int offset, int length) {
        _dtlsServerClient.DtlsTransport.Send(buffer, offset, length);
    }

    internal void RaiseDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

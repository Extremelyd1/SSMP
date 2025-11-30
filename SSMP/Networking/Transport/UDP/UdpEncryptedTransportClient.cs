using System;
using System.Net;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.UDP;

/// <summary>
/// UDP+DTLS implementation of <see cref="IEncryptedTransportClient"/>.
/// </summary>
internal class UdpEncryptedTransportClient : IEncryptedTransportClient {
    /// <summary>
    /// The underlying DTLS server client.
    /// </summary>
    private readonly DtlsServerClient _dtlsServerClient;
    
    /// <summary>
    /// The client identifier for this UDP client.
    /// </summary>
    private readonly UdpClientIdentifier _clientIdentifier;

    /// <inheritdoc />
    public IClientIdentifier ClientIdentifier => _clientIdentifier;
    
    /// <summary>
    /// The IP endpoint of the server client.
    /// Provides direct access to the underlying endpoint for UDP-specific operations.
    /// </summary>
    public IPEndPoint EndPoint => _dtlsServerClient.EndPoint;
    
    /// <summary>
    /// Internal access to the underlying DTLS server client for backward compatibility.
    /// </summary>
    internal DtlsServerClient DtlsServerClient => _dtlsServerClient;

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceivedEvent;

    public UdpEncryptedTransportClient(DtlsServerClient dtlsServerClient) {
        _dtlsServerClient = dtlsServerClient;
        _clientIdentifier = new UdpClientIdentifier(dtlsServerClient.EndPoint);
    }

    /// <inheritdoc />
    public void Send(byte[] buffer, int offset, int length, bool reliable = false) {
        _dtlsServerClient.DtlsTransport.Send(buffer, offset, length);
    }

    /// <summary>
    /// Raises the <see cref="DataReceivedEvent"/> with the given data.
    /// </summary>
    internal void RaiseDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

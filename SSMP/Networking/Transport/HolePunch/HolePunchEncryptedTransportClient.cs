using System;
using System.Net;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.HolePunch;

/// <summary>
/// UDP Hole Punching implementation of <see cref="IEncryptedTransportClient"/>.
/// Wraps DtlsServerClient for hole punched connections.
/// </summary>
internal class HolePunchEncryptedTransportClient : IEncryptedTransportClient {
    /// <summary>
    /// The underlying DTLS server client.
    /// </summary>
    private readonly DtlsServerClient _dtlsServerClient;
    
    /// <summary>
    /// The client identifier for this hole-punched client.
    /// </summary>
    private readonly HolePunchClientIdentifier _clientIdentifier;

    /// <inheritdoc />
    public IClientIdentifier ClientIdentifier => _clientIdentifier;
    
    /// <summary>
    /// The IP endpoint of the server client after NAT traversal.
    /// Provides direct access to the underlying endpoint for hole-punch-specific operations.
    /// </summary>
    public IPEndPoint EndPoint => _dtlsServerClient.EndPoint;
    
    /// <summary>
    /// Internal access to the underlying DTLS server client for backward compatibility.
    /// </summary>
    internal DtlsServerClient DtlsServerClient => _dtlsServerClient;
    
    /// <inheritdoc />
    public event Action<byte[], int>? DataReceivedEvent;

    public HolePunchEncryptedTransportClient(DtlsServerClient dtlsServerClient) {
        _dtlsServerClient = dtlsServerClient;
        _clientIdentifier = new HolePunchClientIdentifier(dtlsServerClient.EndPoint);
    }

    /// <inheritdoc />
    public void Send(Packet.Packet packet) {
        var buffer = packet.ToArray();
        _dtlsServerClient.DtlsTransport.Send(buffer, 0, buffer.Length);
    }

    /// <summary>
    /// Raises the <see cref="DataReceivedEvent"/> with the given data.
    /// </summary>
    internal void RaiseDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

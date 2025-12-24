using System;
using System.Net;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.HolePunch;

/// <summary>
/// UDP Hole Punch implementation of <see cref="IEncryptedTransportClient"/>.
/// Wraps DtlsServerClient for hole punched connections.
/// </summary>
internal class HolePunchEncryptedTransportClient : IEncryptedTransportClient {
    /// <summary>
    /// The underlying DTLS server client.
    /// </summary>
    private readonly DtlsServerClient _dtlsServerClient;

    /// <inheritdoc />
    public string ToDisplayString() => "UDP Hole Punch";

    /// <inheritdoc />
    public string GetUniqueIdentifier() => _dtlsServerClient.EndPoint.ToString();
    
    /// <summary>
    /// The IP endpoint of the server client after NAT traversal.
    /// </summary>
    public IPEndPoint EndPoint => _dtlsServerClient.EndPoint;

    /// <inheritdoc />
    IPEndPoint? IEncryptedTransportClient.EndPoint => EndPoint;

    /// <inheritdoc />
    public bool RequiresCongestionManagement => true;

    /// <inheritdoc />
    public bool RequiresReliability => true;

    /// <inheritdoc />
    public bool RequiresSequencing => true;
    
    /// <inheritdoc />
    public event Action<byte[], int>? DataReceivedEvent;

    public HolePunchEncryptedTransportClient(DtlsServerClient dtlsServerClient) {
        _dtlsServerClient = dtlsServerClient;
    }

    /// <inheritdoc />
    public void Send(byte[] buffer, int offset, int length) {
        _dtlsServerClient.DtlsTransport.Send(buffer, offset, length);
    }

    /// <summary>
    /// Raises the <see cref="DataReceivedEvent"/> with the given data.
    /// </summary>
    internal void RaiseDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

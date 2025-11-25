using System;
using System.Collections.Concurrent;
using System.Net;
using Org.BouncyCastle.Tls;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.UDP;

/// <summary>
/// UDP+DTLS implementation of IEncryptedTransportServer that wraps DtlsServer.
/// </summary>
internal class UdpEncryptedTransportServer : IEncryptedTransportServer {
    private readonly DtlsServer _dtlsServer;
    private readonly ConcurrentDictionary<IPEndPoint, UdpEncryptedTransportClient> _clients;

    public event Action<IEncryptedTransportClient>? ClientConnectedEvent;

    public UdpEncryptedTransportServer() {
        _dtlsServer = new DtlsServer();
        _clients = new ConcurrentDictionary<IPEndPoint, UdpEncryptedTransportClient>();
        _dtlsServer.DataReceivedEvent += OnClientDataReceived;
    }

    public void Start(int port) {
        _dtlsServer.Start(port);
    }

    public void Stop() {
        _dtlsServer.Stop();
        _clients.Clear();
    }

    public void DisconnectClient(IEncryptedTransportClient client) {
        if (client is not UdpEncryptedTransportClient udpClient) {
            throw new ArgumentException("Client is not a UDP encrypted transport client", nameof(client));
        }

        _dtlsServer.DisconnectClient(udpClient.EndPoint);
        _clients.TryRemove(udpClient.EndPoint, out _);
    }

    private void OnClientDataReceived(DtlsServerClient dtlsClient, byte[] data, int length) {
        // Get or create the wrapper client
        var client = _clients.GetOrAdd(dtlsClient.EndPoint, endPoint => {
            var newClient = new UdpEncryptedTransportClient(dtlsClient);
            // Notify about new connection
            ClientConnectedEvent?.Invoke(newClient);
            return newClient;
        });

        // Forward the data received event
        client.RaiseDataReceived(data, length);
    }
}

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

    public int Send(byte[] buffer, int offset, int length) {
        _dtlsServerClient.DtlsTransport.Send(buffer, offset, length);
        return length;
    }

    /// <summary>
    /// Internal method to raise the DataReceivedEvent from the server.
    /// </summary>
    internal void RaiseDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

using System;
using System.Collections.Concurrent;
using System.Net;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.UDP;

/// <summary>
/// UDP+DTLS implementation of IEncryptedTransportServer that wraps DtlsServer.
/// </summary>
internal class UdpEncryptedTransportServer : IEncryptedTransportServer<UdpEncryptedTransportClient> {
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

    public void DisconnectClient(UdpEncryptedTransportClient client) {
        _dtlsServer.DisconnectClient(client.EndPoint);
        _clients.TryRemove(client.EndPoint, out _);
    }

    private void OnClientDataReceived(DtlsServerClient dtlsClient, byte[] data, int length) {
        // Get or create the wrapper client
        var client = _clients.GetOrAdd(dtlsClient.EndPoint, _ => {
            var newClient = new UdpEncryptedTransportClient(dtlsClient);
            // Notify about new connection
            ClientConnectedEvent?.Invoke(newClient);
            return newClient;
        });

        // Forward the data received event
        client.RaiseDataReceived(data, length);
    }
}

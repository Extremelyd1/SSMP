using System;
using System.Collections.Concurrent;
using System.Net;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.UDP;

/// <summary>
/// UDP+DTLS implementation of <see cref="IEncryptedTransportServer{TClient}"/> that wraps DtlsServer.
/// </summary>
internal class UdpEncryptedTransportServer : IEncryptedTransportServer<UdpEncryptedTransportClient> {
    /// <summary>
    /// The underlying DTLS server.
    /// </summary>
    private readonly DtlsServer _dtlsServer;
    /// <summary>
    /// Dictionary containing the clients of this server.
    /// </summary>
    private readonly ConcurrentDictionary<IPEndPoint, UdpEncryptedTransportClient> _clients;

    /// <inheritdoc />
    public event Action<IEncryptedTransportClient>? ClientConnectedEvent;

    public UdpEncryptedTransportServer() {
        _dtlsServer = new DtlsServer();
        _clients = new ConcurrentDictionary<IPEndPoint, UdpEncryptedTransportClient>();
        _dtlsServer.DataReceivedEvent += OnClientDataReceived;
    }

    /// <inheritdoc />
    public void Start(int port) {
        _dtlsServer.Start(port);
    }

    /// <inheritdoc />
    public void Stop() {
        _dtlsServer.Stop();
        _clients.Clear();
    }

    /// <inheritdoc />
    public void DisconnectClient(UdpEncryptedTransportClient client) {
        _dtlsServer.DisconnectClient(client.EndPoint);
        _clients.TryRemove(client.EndPoint, out _);
    }

    /// <summary>
    /// Callback method for when data is received from a server client.
    /// </summary>
    /// <param name="dtlsClient">The DTLS server client that the data was received from.</param>
    /// <param name="data">The data as a byte array.</param>
    /// <param name="length">The length of the data.</param>
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

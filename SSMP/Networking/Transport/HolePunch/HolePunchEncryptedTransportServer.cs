using System;
using System.Collections.Concurrent;
using System.Net;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.HolePunch;

/// <summary>
/// UDP Hole Punching implementation of <see cref="IEncryptedTransportServer{TClient}"/>.
/// Wraps DtlsServer with Master Server registration and NAT traversal coordination.
/// </summary>
internal class HolePunchEncryptedTransportServer : IEncryptedTransportServer<HolePunchEncryptedTransportClient> {
    private readonly string _masterServerAddress;
    /// <summary>
    /// The underlying DTLS server.
    /// </summary>
    private DtlsServer? _dtlsServer;
    /// <summary>
    /// Dictionary containing the clients of this server.
    /// </summary>
    private readonly ConcurrentDictionary<IPEndPoint, HolePunchEncryptedTransportClient> _clients;

    /// <inheritdoc />
    public event Action<HolePunchEncryptedTransportClient>? ClientConnectedEvent;

    /// <summary>
    /// Construct a hole punching server with the given master server address.
    /// </summary>
    /// <param name="masterServerAddress">Master server address for NAT traversal coordination</param>
    public HolePunchEncryptedTransportServer(string masterServerAddress) {
        _masterServerAddress = masterServerAddress;
        _clients = new ConcurrentDictionary<IPEndPoint, HolePunchEncryptedTransportClient>();
    }

    /// <summary>
    /// Start listening for hole punched connections.
    /// </summary>
    /// <param name="port">Local port to bind to</param>
    public void Start(int port) {
        // TODO: Implementation steps:
        // 1. Create and start DtlsServer:
        //    _dtlsServer = new DtlsServer();
        //    _dtlsServer.DataReceivedEvent += OnClientDataReceived;
        //    _dtlsServer.Start(port);
        // 2. Register with Master Server (advertise LobbyID + public endpoint)
        // 3. Master Server will coordinate NAT traversal with clients
        // 4. DtlsServer will handle DTLS connections after holes are punched
        throw new NotImplementedException("UDP Hole Punching transport not yet implemented");
    }

    /// <inheritdoc />
    public void Stop() {
        _dtlsServer?.Stop();
        _clients.Clear();
    }

    /// <inheritdoc />
    public void DisconnectClient(HolePunchEncryptedTransportClient client) {
        _dtlsServer?.DisconnectClient(client.EndPoint);
        _clients.TryRemove(client.EndPoint, out _);
    }

    /// <summary>
    /// Callback method for when data is received from a server client.
    /// </summary>
    /// <param name="dtlsClient">The client that the data was received from.</param>
    /// <param name="data">The data as a byte array.</param>
    /// <param name="length">The length of the data.</param>
    private void OnClientDataReceived(DtlsServerClient dtlsClient, byte[] data, int length) {
        // Get or create wrapper client (similar to UdpEncryptedTransportServer)
        var client = _clients.GetOrAdd(dtlsClient.EndPoint, endPoint => {
            var newClient = new HolePunchEncryptedTransportClient(dtlsClient);
            ClientConnectedEvent?.Invoke(newClient);
            return newClient;
        });

        client.RaiseDataReceived(data, length);
    }
}

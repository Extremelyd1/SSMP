using System;
using System.Collections.Concurrent;
using System.Net;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.HolePunch;

/// <summary>
/// UDP Hole Punching implementation of IEncryptedTransportServer.
/// Wraps DtlsServer with Master Server registration and NAT traversal coordination.
/// </summary>
internal class HolePunchEncryptedTransportServer : IEncryptedTransportServer {
    private readonly string _masterServerAddress;
    private DtlsServer? _dtlsServer;
    private readonly ConcurrentDictionary<IPEndPoint, HolePunchEncryptedTransportClient> _clients;

    public event Action<IEncryptedTransportClient>? ClientConnectedEvent;

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

    public void Stop() {
        _dtlsServer?.Stop();
        _clients.Clear();
    }

    public void DisconnectClient(IEncryptedTransportClient client) {
        if (client is not HolePunchEncryptedTransportClient hpClient) {
            throw new ArgumentException("Client is not a hole punch transport client", nameof(client));
        }

        _dtlsServer?.DisconnectClient(hpClient.EndPoint);
        _clients.TryRemove(hpClient.EndPoint, out _);
    }

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

    public int Send(byte[] buffer, int offset, int length) {
        _dtlsServerClient.DtlsTransport.Send(buffer, offset, length);
        return length;
    }

    internal void RaiseDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

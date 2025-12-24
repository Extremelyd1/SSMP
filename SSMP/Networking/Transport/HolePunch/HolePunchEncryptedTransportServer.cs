using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SSMP.Logging;
using SSMP.Networking.Matchmaking;
using SSMP.Networking.Server;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.HolePunch;

/// <summary>
/// UDP Hole Punch implementation of <see cref="IEncryptedTransportServer"/>.
/// Handles hole punch coordination for incoming client connections.
/// </summary>
internal class HolePunchEncryptedTransportServer : IEncryptedTransportServer {
    /// <summary>
    /// Number of punch packets to send per client.
    /// Increased to 100 (5s) to ensure reliability.
    /// </summary>
    private const int PunchPacketCount = 100;

    /// <summary>
    /// Delay between punch packets in milliseconds.
    /// </summary>
    private const int PunchPacketDelayMs = 50;

    /// <summary>
    /// The underlying DTLS server.
    /// </summary>
    private readonly DtlsServer _dtlsServer;

    /// <summary>
    /// Dictionary containing the clients of this server.
    /// </summary>
    private readonly ConcurrentDictionary<IPEndPoint, HolePunchEncryptedTransportClient> _clients;

    /// <inheritdoc />
    public event Action<IEncryptedTransportClient>? ClientConnectedEvent;

    public HolePunchEncryptedTransportServer() {
        _dtlsServer = new DtlsServer();
        _clients = new ConcurrentDictionary<IPEndPoint, HolePunchEncryptedTransportClient>();
        _dtlsServer.DataReceivedEvent += OnClientDataReceived;
    }

    /// <inheritdoc />
    public void Start(int port) {
        Logger.Info($"HolePunch Server: Starting on port {port}");
        
        // Subscribe to punch coordination
        PunchCoordinator.PunchClientRequested += OnPunchClientRequested;
        
        _dtlsServer.Start(port);
    }

    /// <inheritdoc />
    public void Stop() {
        Logger.Info("HolePunch Server: Stopping");
        
        // Unsubscribe from punch coordination
        PunchCoordinator.PunchClientRequested -= OnPunchClientRequested;
        
        _dtlsServer.Stop();
        _clients.Clear();
    }

    /// <summary>
    /// Called when MMS notifies us of a client needing punch-back.
    /// </summary>
    private void OnPunchClientRequested(string clientIp, int clientPort) {
        if (!IPAddress.TryParse(clientIp, out var ip)) {
            Logger.Warn($"HolePunch Server: Invalid client IP: {clientIp}");
            return;
        }
        
        PunchToClient(new IPEndPoint(ip, clientPort));
    }

    /// <inheritdoc />
    public void DisconnectClient(IEncryptedTransportClient client) {
        if (client is not HolePunchEncryptedTransportClient hpClient)
            return;
        
        _dtlsServer.DisconnectClient(hpClient.EndPoint);
        _clients.TryRemove(hpClient.EndPoint, out _);
    }

    /// <summary>
    /// Initiates hole punch to a client that wants to connect.
    /// Uses the DTLS server's socket so the punch comes from the correct port.
    /// </summary>
    /// <param name="clientEndpoint">The client's public endpoint.</param>
    public void PunchToClient(IPEndPoint clientEndpoint) {
        Logger.Debug($"HolePunch Server: Punching to client at {clientEndpoint}");
        
        var punchPacket = new byte[] { 0x50, 0x55, 0x4E, 0x43, 0x48 }; // "PUNCH"
        
        for (var i = 0; i < PunchPacketCount; i++) {
            _dtlsServer.SendRaw(punchPacket, clientEndpoint);
            Thread.Sleep(PunchPacketDelayMs);
        }
        
        Logger.Info($"HolePunch Server: Punch packets sent to {clientEndpoint}");
    }

    /// <summary>
    /// Callback method for when data is received from a server client.
    /// </summary>
    private void OnClientDataReceived(DtlsServerClient dtlsClient, byte[] data, int length) {
        var client = _clients.GetOrAdd(dtlsClient.EndPoint, _ => {
            var newClient = new HolePunchEncryptedTransportClient(dtlsClient);
            ClientConnectedEvent?.Invoke(newClient);
            return newClient;
        });

        client.RaiseDataReceived(data, length);
    }
}

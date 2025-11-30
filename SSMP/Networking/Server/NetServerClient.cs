using System;
using System.Collections.Concurrent;
using System.Net;
using SSMP.Networking.Chunk;
using SSMP.Networking.Packet;
using SSMP.Networking.Transport.Common;
using SSMP.Networking.Transport.HolePunch;
using SSMP.Networking.Transport.UDP;

namespace SSMP.Networking.Server;

/// <summary>
/// A client managed by the server. This is only used for communication from server to client.
/// </summary>
internal class NetServerClient {
    /// <summary>
    /// Concurrent dictionary for the set of IDs that are used. We use a dictionary because there is no
    /// standard implementation for a concurrent set.
    /// </summary>
    private static readonly ConcurrentDictionary<ushort, byte> UsedIds = new();

    /// <summary>
    /// The last ID that was assigned.
    /// </summary>
    private static ushort _lastId;

    /// <summary>
    /// The ID of this client.
    /// </summary>
    public ushort Id { get; }

    /// <summary>
    /// Whether the client is registered.
    /// </summary>
    public bool IsRegistered { get; set; }
    
    /// <summary>
    /// The update manager for the client.
    /// </summary>
    public ServerUpdateManager UpdateManager { get; }
    
    /// <summary>
    /// The chunk sender instance for sending large amounts of data.
    /// </summary>
    public ServerChunkSender ChunkSender { get; }
    /// <summary>
    /// The chunk receiver instance for receiving large amounts of data.
    /// </summary>
    public ServerChunkReceiver ChunkReceiver { get; }
    
    /// <summary>
    /// The connection manager for the client.
    /// </summary>
    public ServerConnectionManager ConnectionManager { get; }

    /// <summary>
    /// The transport client for this server client.
    /// </summary>
    public IEncryptedTransportClient TransportClient { get; }
    
    /// <summary>
    /// The client identifier for this client.
    /// </summary>
    public IClientIdentifier ClientIdentifier => TransportClient.ClientIdentifier;

    /// <summary>
    /// The endpoint of the client (for UDP transports only, backward compatibility).
    /// </summary>
    public IPEndPoint? EndPoint {
        get {
            // For UDP-based transports, extract the IPEndPoint
            return TransportClient.ClientIdentifier switch
            {
                UdpClientIdentifier udp      => udp.EndPoint,
                HolePunchClientIdentifier hp => hp.EndPoint,
                _ => null
            };
        }
    }

    /// <summary>
    /// Construct the client with the given transport client.
    /// </summary>
    /// <param name="transportClient">The encrypted transport client.</param>
    /// <param name="packetManager">The packet manager used on the server.</param>
    public NetServerClient(IEncryptedTransportClient transportClient, PacketManager packetManager) {
        TransportClient = transportClient;

        Id = GetId();
        
        // Disable congestion management for transports that have built-in congestion handling.
        // Steam P2P returns null for ThrottleKey to indicate it should skip application-level
        // throttling and congestion management.
        var enableCongestionManagement = transportClient.ClientIdentifier.ThrottleKey != null;
        
        UpdateManager = new ServerUpdateManager(enableCongestionManagement);
        
        // Wrap the transport client in an adapter for the update manager
        UpdateManager.Transport = new EncryptedTransportClientAdapter(transportClient);
        ChunkSender = new ServerChunkSender(UpdateManager);
        ChunkReceiver = new ServerChunkReceiver(UpdateManager);
        ConnectionManager = new ServerConnectionManager(packetManager, ChunkSender, ChunkReceiver, Id);
    }

    /// <summary>
    /// Disconnect the client from the server.
    /// </summary>
    public void Disconnect() {
        UsedIds.TryRemove(Id, out _);

        UpdateManager.StopUpdates();
        ChunkSender.Stop();
        // Reset chunk receiver state to prevent stale _chunkId on reconnect
        ChunkReceiver.Reset();
        ConnectionManager.StopAcceptingConnection();
    }

    /// <summary>
    /// Get a new ID that is not in use by another client.
    /// </summary>
    /// <returns>An unused ID.</returns>
    private static ushort GetId() {
        ushort newId;
        do {
            newId = _lastId++;
        } while (UsedIds.ContainsKey(newId));

        UsedIds[newId] = 0;
        return newId;
    }
}


/// <summary>
/// Adapter to adapt IEncryptedTransportClient to IEncryptedTransport for UdpUpdateManager.
/// </summary>
internal class EncryptedTransportClientAdapter : IEncryptedTransport {
    private readonly IEncryptedTransportClient _client;

    public EncryptedTransportClientAdapter(IEncryptedTransportClient client) {
        _client = client;
    }

    public event Action<byte[], int>? DataReceivedEvent {
        add => _client.DataReceivedEvent += value;
        remove => _client.DataReceivedEvent -= value;
    }

    public void Connect(string address, int port) => throw new NotSupportedException();
    public void Disconnect() => throw new NotSupportedException();

    public void Send(byte[] buffer, int offset, int length) {
        _client.Send(buffer, offset, length);
    }

    public int Receive(byte[] buffer, int offset, int length, int waitMillis) => throw new NotSupportedException();
}

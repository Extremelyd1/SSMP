/*
using System;
using System.Collections.Generic;
using SSMP.Logging;

namespace SSMP.Networking.Transport.Common;

/// <summary>
/// Multi-transport server that aggregates multiple transport server implementations,
/// allowing NetServer to accept connections from different transports simultaneously
/// (e.g., UDP, Steam P2P, UDP Hole Punching).
/// </summary>
internal class MultiTransportServer : IEncryptedTransportServer<IEncryptedTransportClient> {
    /// <summary>
    /// List of registered transport servers (using dynamic to handle different generic types).
    /// </summary>
    private readonly List<object> _transportServers = new();
    
    /// <inheritdoc />
    public event Action<IEncryptedTransportClient>? ClientConnectedEvent;

    /// <summary>
    /// Adds a transport server to the multi-transport aggregator.
    /// </summary>
    /// <typeparam name="TClient">The specific client type of the transport.</typeparam>
    /// <param name="transportServer">The transport server to add.</param>
    public void AddTransport<TClient>(IEncryptedTransportServer<TClient> transportServer) 
        where TClient : IEncryptedTransportClient {
        if (transportServer == null) {
            throw new ArgumentNullException(nameof(transportServer));
        }

        // Forward client connected events from this transport to our unified event
        transportServer.ClientConnectedEvent += (client) => {
            Logger.Debug($"Client connected via transport: {client.ClientIdentifier.ToDisplayString()}");
            ClientConnectedEvent?.Invoke(client);
        };
        
        _transportServers.Add(transportServer);
        Logger.Info($"Added transport: {transportServer.GetType().Name}");
    }

    /// <inheritdoc />
    public void Start(int port) {
        Logger.Info($"Starting multi-transport server on port {port}");
        
        foreach (var server in _transportServers) {
            try {
                // Use reflection to call Start on the dynamic transport server
                var startMethod = server.GetType().GetMethod("Start");
                startMethod?.Invoke(server, new object[] { port });
                Logger.Debug($"Started transport: {server.GetType().Name}");
            } catch (Exception e) {
                Logger.Error($"Failed to start transport {server.GetType().Name}: {e}");
                throw;
            }
        }
    }

    /// <inheritdoc />
    public void Stop() {
        Logger.Info("Stopping multi-transport server");
        
        foreach (var server in _transportServers) {
            try {
                var stopMethod = server.GetType().GetMethod("Stop");
                stopMethod?.Invoke(server, null);
                Logger.Debug($"Stopped transport: {server.GetType().Name}");
            } catch (Exception e) {
                Logger.Error($"Error stopping transport {server.GetType().Name}: {e}");
            }
        }
    }

    /// <inheritdoc />
    public void DisconnectClient(IEncryptedTransportClient client) {
        // Try to disconnect from all transports
        // Only the transport that owns this client will succeed
        foreach (var server in _transportServers) {
            try {
                var disconnectMethod = server.GetType().GetMethod("DisconnectClient");
                disconnectMethod?.Invoke(server, new object[] { client });
                return; // Successfully disconnected, stop trying other transports
            } catch (ArgumentException) {
                // Expected - this transport doesn't own this client
                continue;
            } catch (Exception e) {
                Logger.Error($"Error disconnecting client from {server.GetType().Name}: {e}");
            }
        }
    }
}
*/

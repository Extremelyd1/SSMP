using System;

namespace SSMP.Networking.Transport.Common;

/// <summary>
/// Interface for a server-side encrypted transport for starting/stopping and managing client (dis)connect.
/// </summary>
/// <typeparam name="TClient"></typeparam>
internal interface IEncryptedTransportServer<in TClient> where TClient : IEncryptedTransportClient {
    /// <summary>
    /// Event raised when a client connects to the server.
    /// </summary>
    event Action<IEncryptedTransportClient>? ClientConnectedEvent;
    
    /// <summary>
    /// Start listening for connections.
    /// </summary>
    /// <param name="port">Port to listen on (if applicable).</param>
    void Start(int port);
    
    /// <summary>
    /// Stop listening and disconnect all clients.
    /// </summary>
    void Stop();

    /// <summary>
    /// Disconnect a specific client.
    /// </summary>
    /// <param name="client">The client to disconnect.</param>
    void DisconnectClient(TClient client);
}

using System;

namespace SSMP.Networking.Transport.Common;

internal interface IEncryptedTransportServer<in TClient> where TClient : IEncryptedTransportClient {
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

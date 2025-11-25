using System;

namespace SSMP.Networking.Transport.Common;

internal interface IEncryptedTransportServer {
    event Action<IEncryptedTransportClient>? ClientConnectedEvent;
    
    /// <summary>
    /// Start listening.
    /// - UDP: Binds to port
    /// - Steam: Opens channel
    /// - HolePunch: Registers with Master Server
    /// </summary>
    void Start(int port);
    
    void Stop();
    void DisconnectClient(IEncryptedTransportClient client);
}

internal interface IEncryptedTransportClient {
    string ClientIdentifier { get; }
    event Action<byte[], int>? DataReceivedEvent;
    int Send(byte[] buffer, int offset, int length);
}

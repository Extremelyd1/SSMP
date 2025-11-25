using System;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of IEncryptedTransportServer.
/// TODO: Implement using Steamworks.NET SteamNetworking API.
/// </summary>
internal class SteamEncryptedTransportServer : IEncryptedTransportServer {
    public event Action<IEncryptedTransportClient>? ClientConnectedEvent;

    /// <summary>
    /// Start listening for Steam P2P connections.
    /// </summary>
    /// <param name="port">Port parameter (unused for Steam P2P, opens P2P channel)</param>
    public void Start(int port) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }

    public void Stop() {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }

    public void DisconnectClient(IEncryptedTransportClient client) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }
}

/// <summary>
/// Steam P2P implementation of IEncryptedTransportClient.
/// TODO: Implement using Steamworks.NET.
/// </summary>
internal class SteamEncryptedTransportClient : IEncryptedTransportClient {
    public string ClientIdentifier => throw new NotImplementedException("Steam P2P transport not yet implemented");
    
    public event Action<byte[], int>? DataReceivedEvent;

    public int Send(byte[] buffer, int offset, int length) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }
}

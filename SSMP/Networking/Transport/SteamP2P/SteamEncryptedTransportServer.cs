using System;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of <see cref="IEncryptedTransportServer{TClient}"/>.
/// </summary>
/// TODO: Implement using Steamworks.NET SteamNetworking API.
internal class SteamEncryptedTransportServer : IEncryptedTransportServer<SteamEncryptedTransportClient> {
    /// <inheritdoc />
    public event Action<SteamEncryptedTransportClient>? ClientConnectedEvent;

    /// <summary>
    /// Start listening for Steam P2P connections.
    /// </summary>
    /// <param name="port">Port parameter (unused for Steam P2P, opens P2P channel)</param>
    public void Start(int port) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }

    /// <inheritdoc />
    public void Stop() {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }

    /// <inheritdoc />
    public void DisconnectClient(SteamEncryptedTransportClient client) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }
}

using System;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of <see cref="IEncryptedTransport"/>.
/// </summary>
/// TODO: Implement using Steamworks.NET SteamNetworking API.
internal class SteamEncryptedTransport : IEncryptedTransport {
    /// <inheritdoc />
    public event Action<byte[], int>? DataReceivedEvent;

    /// <summary>
    /// Connect to remote peer via Steam P2P.
    /// </summary>
    /// <param name="address">SteamID as string (e.g., "76561198...")</param>
    /// <param name="port">Port parameter (unused for Steam P2P)</param>
    public void Connect(string address, int port) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }

    /// <inheritdoc />
    public void Send(byte[] buffer, int offset, int length) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }

    /// <inheritdoc />
    public int Receive(byte[] buffer, int offset, int length, int waitMillis) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }

    /// <inheritdoc />
    public void Disconnect() {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }
}

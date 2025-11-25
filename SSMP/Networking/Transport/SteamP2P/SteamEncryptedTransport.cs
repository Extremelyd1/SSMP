using System;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of IEncryptedTransport.
/// TODO: Implement using Steamworks.NET SteamNetworking API.
/// </summary>
internal class SteamEncryptedTransport : IEncryptedTransport {
    public event Action<byte[], int>? DataReceivedEvent;

    /// <summary>
    /// Connect to remote peer via Steam P2P.
    /// </summary>
    /// <param name="address">SteamID as string (e.g., "76561198...")</param>
    /// <param name="port">Port parameter (unused for Steam P2P)</param>
    public void Connect(string address, int port) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }

    public int Send(byte[] buffer, int offset, int length) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }

    public int Receive(byte[] buffer, int offset, int length, int waitMillis) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }

    public void Disconnect() {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }
}

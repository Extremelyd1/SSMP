using System;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of IEncryptedTransportClient.
/// TODO: Implement using Steamworks.NET.
/// </summary>
internal class SteamEncryptedTransportClient : IEncryptedTransportClient {
    public string ClientIdentifier => throw new NotImplementedException("Steam P2P transport not yet implemented");
    
    public event Action<byte[], int>? DataReceivedEvent;

    public void Send(byte[] buffer, int offset, int length) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }
}

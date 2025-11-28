using System;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of <see cref="IEncryptedTransportClient"/>.
/// </summary>
/// TODO: Implement using Steamworks.NET.
internal class SteamEncryptedTransportClient : IEncryptedTransportClient {
    /// <inheritdoc />
    public string ClientIdentifier => throw new NotImplementedException("Steam P2P transport not yet implemented");

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceivedEvent;

    /// <inheritdoc />
    public void Send(byte[] buffer, int offset, int length) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }
}

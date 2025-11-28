using System;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of <see cref="IEncryptedTransportClient"/>.
/// </summary>
/// TODO: Implement using Steamworks.NET when Steam P2P support is added.
internal class SteamEncryptedTransportClient : IEncryptedTransportClient {
    /// <summary>
    /// The client identifier for this Steam client.
    /// </summary>
    private readonly SteamClientIdentifier _clientIdentifier;
    
    /// <inheritdoc />
    public IClientIdentifier ClientIdentifier => _clientIdentifier;
    
    /// <summary>
    /// The Steam ID of the client.
    /// Provides direct access to the underlying Steam ID for Steam-specific operations.
    /// </summary>
    public ulong SteamId => _clientIdentifier.SteamId;

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceivedEvent;

    /// <summary>
    /// Constructs a Steam P2P transport client.
    /// </summary>
    /// <param name="steamId">The Steam ID of the client.</param>
    public SteamEncryptedTransportClient(ulong steamId) {
        _clientIdentifier = new SteamClientIdentifier(steamId);
    }

    /// <inheritdoc />
    public void Send(byte[] buffer, int offset, int length) {
        throw new NotImplementedException("Steam P2P transport not yet implemented");
    }
    
    /// <summary>
    /// Raises the <see cref="DataReceivedEvent"/> with the given data.
    /// </summary>
    internal void RaiseDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

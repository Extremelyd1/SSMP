using System;
using System.Net;
using SSMP.Game;
using SSMP.Logging;
using SSMP.Networking.Transport.Common;
using Steamworks;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of <see cref="IEncryptedTransportClient"/>.
/// Represents a connected client from the server's perspective.
/// </summary>
internal class SteamEncryptedTransportClient : IReliableTransportClient {
    /// <summary>
    /// P2P channel for communication.
    /// </summary>
    private const int P2P_CHANNEL = 0;

    /// <summary>
    /// The Steam ID of the client.
    /// </summary>
    private readonly ulong _steamId;

    /// <summary>
    /// Cached Steam ID struct to avoid repeated allocations.
    /// </summary>
    private readonly CSteamID _steamIdStruct;

    /// <inheritdoc />
    public string ToDisplayString() => "SteamP2P";
    
    /// <inheritdoc />
    public string GetUniqueIdentifier() => _steamId.ToString();
    
    /// <inheritdoc />
    public IPEndPoint? EndPoint => null; // Steam doesn't need throttling

    /// <summary>
    /// The Steam ID of the client.
    /// Provides direct access to the underlying Steam ID for Steam-specific operations.
    /// </summary>
    public ulong SteamId => _steamId;

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceivedEvent;

    /// <summary>
    /// Constructs a Steam P2P transport client.
    /// </summary>
    /// <param name="steamId">The Steam ID of the client.</param>
    public SteamEncryptedTransportClient(ulong steamId) {
        _steamId = steamId;
        _steamIdStruct = new CSteamID(steamId);
    }

    /// <inheritdoc/>
    public void Send(byte[] buffer, int offset, int length) {
        SendInternal(buffer, offset, length, EP2PSend.k_EP2PSendUnreliableNoDelay);
    }

    /// <inheritdoc/>
    public void SendReliable(byte[] buffer, int offset, int length) {
        SendInternal(buffer, offset, length, EP2PSend.k_EP2PSendReliable);
    }

    /// <summary>
    /// Internal helper to send data with a specific P2P send type.
    /// </summary>
    private void SendInternal(byte[] buffer, int offset, int length, EP2PSend sendType) {
        if (sendType == EP2PSend.k_EP2PSendReliable) {
            Logger.Debug($"Steam P2P: Sending RELIABLE packet to {SteamId} of length {length}");
        }
        if (!SteamManager.IsInitialized) {
            Logger.Warn($"Steam P2P: Cannot send to client {SteamId}, Steam not initialized");
            return;
        }

        // Check for loopback
        if (_steamIdStruct == SteamUser.GetSteamID()) {
            SteamLoopbackChannel.SendToClient(buffer, offset, length);
            return;
        }

        if (!SteamNetworking.SendP2PPacket(_steamIdStruct, buffer, (uint) length, sendType, P2P_CHANNEL)) {
            Logger.Warn($"Steam P2P: Failed to send packet to client {SteamId}");
        }
    }

    /// <summary>
    /// Raises the <see cref="DataReceivedEvent"/> with the given data.
    /// Called by the server when it receives packets from this client.
    /// </summary>
    internal void RaiseDataReceived(byte[] data, int length) {
        DataReceivedEvent?.Invoke(data, length);
    }
}

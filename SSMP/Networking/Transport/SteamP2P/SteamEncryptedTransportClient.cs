using System;
using System.Buffers;
using SSMP.Game;
using SSMP.Logging;
using SSMP.Networking.Transport.Common;
using Steamworks;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of <see cref="IEncryptedTransportClient"/>.
/// Represents a connected client from the server's perspective.
/// </summary>
internal class SteamEncryptedTransportClient : IEncryptedTransportClient {
    /// <summary>
    /// P2P channel for communication.
    /// </summary>
    private const int P2P_CHANNEL = 0;

    /// <summary>
    /// The client identifier for this Steam client.
    /// </summary>
    private readonly SteamClientIdentifier _clientIdentifier;
    
    /// <summary>
    /// Cached Steam ID struct to avoid repeated allocations.
    /// </summary>
    private readonly CSteamID _steamIdStruct;
    
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
        _steamIdStruct = new CSteamID(steamId);
    }

    /// <inheritdoc/>
    public void Send(byte[] buffer, int offset, int length, bool reliable = false) {
        if (!SteamManager.IsInitialized) {
            Logger.Warn($"Steam P2P: Cannot send to client {SteamId}, Steam not initialized");
            return;
        }

        // Check for loopback
        if (_steamIdStruct == SteamUser.GetSteamID()) {
            // For loopback with offset, we need to create a proper slice
            if (offset > 0) {
                var temp = ArrayPool<byte>.Shared.Rent(length);
                try {
                    Buffer.BlockCopy(buffer, offset, temp, 0, length);
                    SteamLoopbackChannel.SendToClient(temp, length);
                } finally {
                    ArrayPool<byte>.Shared.Return(temp);
                }
            } else {
                SteamLoopbackChannel.SendToClient(buffer, length);
            }
            return;
        }

        byte[] dataToSend = buffer;
        bool rentedArray = false;

        // Copy data to send buffer if offset is used (avoid allocation when offset is 0)
        if (offset > 0) {
            dataToSend = ArrayPool<byte>.Shared.Rent(length);
            rentedArray = true;
            Buffer.BlockCopy(buffer, offset, dataToSend, 0, length);
        }

        try {
            // Send packet to this specific client
            var sendType = reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliableNoDelay;
            if (!SteamNetworking.SendP2PPacket(_steamIdStruct, dataToSend, (uint)length, sendType, P2P_CHANNEL)) {
                Logger.Warn($"Steam P2P: Failed to send packet to client {SteamId}");
            }
        } finally {
            if (rentedArray) {
                ArrayPool<byte>.Shared.Return(dataToSend);
            }
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

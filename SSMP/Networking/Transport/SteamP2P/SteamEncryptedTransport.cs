using System;
using System.Buffers;
using System.Threading;
using SSMP.Game;
using SSMP.Logging;
using SSMP.Networking.Transport.Common;
using Steamworks;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of <see cref="IEncryptedTransport"/>.
/// Used by clients to connect to a server via Steam P2P networking.
/// </summary>
internal class SteamEncryptedTransport : IEncryptedTransport {
    /// <summary>
    /// P2P channel to use for all communication (bidirectional).
    /// </summary>
    private const int P2P_CHANNEL = 0;

    /// <summary>
    /// Maximum Steam P2P packet size.
    /// </summary>
    private const int MAX_PACKET_SIZE = 1200;

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceivedEvent;

    /// <inheritdoc />
    public bool RequiresCongestionManagement => false;

    /// <summary>
    /// The Steam ID of the remote peer we're connected to.
    /// </summary>
    private CSteamID _remoteSteamId;

    /// <summary>
    /// Cached local Steam ID to avoid repeated API calls.
    /// </summary>
    private CSteamID _localSteamId;

    /// <summary>
    /// Whether this transport is currently connected.
    /// </summary>
    private volatile bool _isConnected;

    /// <summary>
    /// Buffer for receiving P2P packets.
    /// </summary>
    private readonly byte[] _receiveBuffer = new byte[MAX_PACKET_SIZE];
    
    /// <summary>
    /// Token source for cancelling the receive loop.
    /// </summary>
    private CancellationTokenSource? _receiveTokenSource;

    /// <summary>
    /// Thread for receiving P2P packets.
    /// </summary>
    private Thread? _receiveThread;

    /// <summary>
    /// Connect to remote peer via Steam P2P.
    /// </summary>
    /// <param name="address">SteamID as string (e.g., "76561198...")</param>
    /// <param name="port">Port parameter (unused for Steam P2P)</param>
    /// <exception cref="InvalidOperationException">Thrown if Steam is not initialized.</exception>
    /// <exception cref="ArgumentException">Thrown if address is not a valid Steam ID.</exception>
    public void Connect(string address, int port) {
        if (!SteamManager.IsInitialized) {
            throw new InvalidOperationException("Cannot connect via Steam P2P: Steam is not initialized");
        }

        if (!ulong.TryParse(address, out var steamId64)) {
            throw new ArgumentException($"Invalid Steam ID format: {address}", nameof(address));
        }

        _remoteSteamId = new CSteamID(steamId64);
        _localSteamId = SteamUser.GetSteamID();
        _isConnected = true;

        Logger.Info($"Steam P2P: Connecting to {_remoteSteamId}");

        SteamNetworking.AllowP2PPacketRelay(true);

        if (_remoteSteamId == _localSteamId) {
            Logger.Info("Steam P2P: Connecting to self, using loopback channel");
            SteamLoopbackChannel.RegisterClient(this);
        }

        _receiveTokenSource = new CancellationTokenSource();
        _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        _receiveThread.Start();
    }

    /// <inheritdoc />
    public void Send(byte[] buffer, int offset, int length, bool reliable = false) {
        if (!_isConnected) {
            throw new InvalidOperationException("Cannot send: not connected");
        }

        if (!SteamManager.IsInitialized) {
            throw new InvalidOperationException("Cannot send: Steam is not initialized");
        }

        if (_remoteSteamId == _localSteamId) {
            if (offset > 0) {
                var temp = ArrayPool<byte>.Shared.Rent(length);
                try {
                    Buffer.BlockCopy(buffer, offset, temp, 0, length);
                    SteamLoopbackChannel.SendToServer(temp, length);
                } finally {
                    ArrayPool<byte>.Shared.Return(temp);
                }
            } else {
                SteamLoopbackChannel.SendToServer(buffer, length);
            }
            return;
        }

        byte[] dataToSend = buffer;
        bool rentedArray = false;

        if (offset > 0) {
            dataToSend = ArrayPool<byte>.Shared.Rent(length);
            rentedArray = true;
            Buffer.BlockCopy(buffer, offset, dataToSend, 0, length);
        }

        try {
            var sendType = reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliableNoDelay;
            if (!SteamNetworking.SendP2PPacket(_remoteSteamId, dataToSend, (uint)length, sendType, P2P_CHANNEL)) {
                Logger.Warn($"Steam P2P: Failed to send packet to {_remoteSteamId}");
            }
        } finally {
            if (rentedArray) {
                ArrayPool<byte>.Shared.Return(dataToSend);
            }
        }
    }

    /// <inheritdoc />
    public int Receive(byte[]? buffer, int offset, int length, int waitMillis) {
        if (buffer == null) {
            return ReceiveAndFireEvent();
        }

        if (!_isConnected || !SteamManager.IsInitialized) return 0;

        if (!SteamNetworking.IsP2PPacketAvailable(out uint packetSize, P2P_CHANNEL)) return 0;

        if (!SteamNetworking.ReadP2PPacket(_receiveBuffer, MAX_PACKET_SIZE, out packetSize, out CSteamID remoteSteamId, P2P_CHANNEL)) return 0;

        if (remoteSteamId != _remoteSteamId) {
            Logger.Warn($"Steam P2P: Received packet from unexpected peer {remoteSteamId}, expected {_remoteSteamId}");
            return 0;
        }

        var size = (int)packetSize;

        var data = new byte[size];
        Buffer.BlockCopy(_receiveBuffer, 0, data, 0, size);
        DataReceivedEvent?.Invoke(data, size);

        var bytesToCopy = System.Math.Min(size, length);
        Buffer.BlockCopy(_receiveBuffer, 0, buffer, offset, bytesToCopy);
        return bytesToCopy;
    }

    /// <summary>
    /// Receives packets and fires events without copying to a buffer.
    /// Used by the internal receive loop.
    /// </summary>
    private int ReceiveAndFireEvent() {
        if (!_isConnected || !SteamManager.IsInitialized) return 0;

        if (!SteamNetworking.IsP2PPacketAvailable(out uint packetSize, P2P_CHANNEL)) return 0;

        if (!SteamNetworking.ReadP2PPacket(_receiveBuffer, MAX_PACKET_SIZE, out packetSize, out CSteamID remoteSteamId, P2P_CHANNEL)) return 0;

        if (remoteSteamId != _remoteSteamId) {
            Logger.Warn($"Steam P2P: Received packet from unexpected peer {remoteSteamId}, expected {_remoteSteamId}");
            return 0;
        }

        var size = (int)packetSize;

        var data = new byte[size];
        Buffer.BlockCopy(_receiveBuffer, 0, data, 0, size);
        DataReceivedEvent?.Invoke(data, size);

        return size;
    }

    /// <inheritdoc />
    public void Disconnect() {
        if (!_isConnected) return;

        SteamLoopbackChannel.UnregisterClient();

        Logger.Info($"Steam P2P: Disconnecting from {_remoteSteamId}");

        _isConnected = false;

        _receiveTokenSource?.Cancel();

        if (SteamManager.IsInitialized) {
            SteamNetworking.CloseP2PSessionWithUser(_remoteSteamId);
        }

        _remoteSteamId = CSteamID.Nil;

        if (_receiveThread != null) {
            if (!_receiveThread.Join(5000)) {
                Logger.Warn("Steam P2P: Receive thread did not terminate within 5 seconds");
            }
            _receiveThread = null;
        }

        _receiveTokenSource?.Dispose();
        _receiveTokenSource = null;
    }

    /// <summary>
    /// Continuously polls for incoming P2P packets.
    /// Steam API limitation: no blocking receive or callback available, must poll.
    /// </summary>
    private void ReceiveLoop() {
        var token = _receiveTokenSource;
        if (token == null) return;

        while (_isConnected && !token.IsCancellationRequested) {
            try {
                Receive(null, 0, 0, 0);
                
                Thread.Sleep(1);
            } catch (Exception e) {
                Logger.Error($"Steam P2P: Error in receive loop: {e}");
            }
        }

        Logger.Info("Steam P2P: Receive loop exited cleanly");
    }

    /// <summary>
    /// Receives a packet from the loopback channel.
    /// </summary>
    public void ReceiveLoopbackPacket(byte[] data, int length) {
        if (!_isConnected) return;
        DataReceivedEvent?.Invoke(data, length);
    }
}

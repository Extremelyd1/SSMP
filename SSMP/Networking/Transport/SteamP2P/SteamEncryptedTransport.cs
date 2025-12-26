using System;
using System.Runtime.CompilerServices;
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
internal class SteamEncryptedTransport : IReliableTransport {
    /// <summary>
    /// Maximum Steam P2P packet size.
    /// </summary>
    private const int SteamMaxPacketSize = 1200;

    /// <summary>
    /// Polling interval in milliseconds for Steam P2P packet receive loop.
    /// 17.2ms achieves ~58Hz polling rate to balance responsiveness and CPU usage.
    /// </summary>
    private const double PollIntervalMS = 17.2;

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceivedEvent;

    /// <inheritdoc />
    public bool RequiresCongestionManagement => false;

    /// <inheritdoc />
    public bool RequiresReliability => false;

    /// <inheritdoc />
    public bool RequiresSequencing => false;

    /// <inheritdoc />
    public int MaxPacketSize => SteamMaxPacketSize;

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
    private readonly byte[] _receiveBuffer = new byte[SteamMaxPacketSize];

    /// <summary>
    /// Token source for cancelling the receive loop.
    /// </summary>
    private CancellationTokenSource? _receiveTokenSource;

    /// <summary>
    /// Thread for receiving P2P packets.
    /// </summary>
    private Thread? _receiveThread;

    /// <summary>
    /// Cached CSteamID.Nil to avoid allocation on hot path.
    /// </summary>
    private static readonly CSteamID NilSteamId = CSteamID.Nil;

    /// <summary>
    /// Cached loopback channel instance to avoid GetOrCreate() overhead.
    /// </summary>
    private SteamLoopbackChannel? _cachedLoopbackChannel;

    /// <summary>
    /// Flag indicating if we're in loopback mode (connecting to self).
    /// </summary>
    private bool _isLoopback;

    /// <summary>
    /// Cached Steam initialized check to reduce property access overhead.
    /// </summary>
    private bool _steamInitialized;

    /// <summary>
    /// Connect to remote peer via Steam P2P.
    /// </summary>
    /// <param name="address">SteamID as string (e.g., "76561198...")</param>
    /// <param name="port">Port parameter (unused for Steam P2P)</param>
    /// <exception cref="InvalidOperationException">Thrown if Steam is not initialized.</exception>
    /// <exception cref="ArgumentException">Thrown if address is not a valid Steam ID.</exception>
    public void Connect(string address, int port) {
        _steamInitialized = SteamManager.IsInitialized;

        if (!_steamInitialized) {
            throw new InvalidOperationException("Cannot connect via Steam P2P: Steam is not initialized");
        }

        if (!ulong.TryParse(address, out var steamId64)) {
            throw new ArgumentException($"Invalid Steam ID format: {address}", nameof(address));
        }

        _remoteSteamId = new CSteamID(steamId64);
        _localSteamId = SteamUser.GetSteamID();
        _isLoopback = _remoteSteamId == _localSteamId;
        _isConnected = true;

        Logger.Info($"Steam P2P: Connecting to {_remoteSteamId}");

        SteamNetworking.AllowP2PPacketRelay(true);

        if (_isLoopback) {
            Logger.Info("Steam P2P: Connecting to self, using loopback channel");
            _cachedLoopbackChannel = SteamLoopbackChannel.GetOrCreate();
            _cachedLoopbackChannel.RegisterClient(this);
        }

        _receiveTokenSource = new CancellationTokenSource();
        _receiveThread = new Thread(ReceiveLoop) {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal // Higher priority for network thread
        };
        _receiveThread.Start();
    }

    /// <inheritdoc />
    public void Send(byte[] buffer, int offset, int length) {
        SendInternal(buffer, offset, length, EP2PSend.k_EP2PSendUnreliableNoDelay);
    }

    /// <inheritdoc />
    public void SendReliable(byte[] buffer, int offset, int length) {
        SendInternal(buffer, offset, length, EP2PSend.k_EP2PSendReliable);
    }

    /// <summary>
    /// Internal helper to send data with a specific P2P send type.
    /// </summary>
    private void SendInternal(byte[] buffer, int offset, int length, EP2PSend sendType) {
        if (!_isConnected) {
            throw new InvalidOperationException("Cannot send: not connected");
        }

        if (!_steamInitialized) {
            throw new InvalidOperationException("Cannot send: Steam is not initialized");
        }

        if (_isLoopback) {
            // Use cached loopback channel
            _cachedLoopbackChannel!.SendToServer(buffer, offset, length);
            return;
        }

        // Client sends to server on Channel 0
        if (!SteamNetworking.SendP2PPacket(_remoteSteamId, buffer, (uint) length, sendType)) {
            Logger.Warn($"Steam P2P: Failed to send packet to {_remoteSteamId}");
        }
    }

    /// <summary>
    /// Process all available incoming P2P packets.
    /// Drains the entire queue to prevent packet buildup when polling.
    /// </summary>
    private void Receive(byte[]? buffer, int offset, int length) {
        if (!_isConnected || !_steamInitialized) return;

        // Cache event delegate to avoid repeated field access
        var dataReceived = DataReceivedEvent;
        if (dataReceived == null) return;

        // Drain ALL available packets (matches server-side behavior)
        while (SteamNetworking.IsP2PPacketAvailable(out var packetSize, 1)) {
            // Client listens for server packets on Channel 1 (to differentiate from server traffic on Channel 0)
            if (!SteamNetworking.ReadP2PPacket(
                    _receiveBuffer,
                    SteamMaxPacketSize,
                    out packetSize,
                    out var remoteSteamId,
                    1 // Channel 1: Server -> Client
                )) {
                continue;
            }

            if (remoteSteamId != _remoteSteamId) {
                Logger.Warn(
                    $"Steam P2P: Received packet from unexpected peer {remoteSteamId}, expected {_remoteSteamId}"
                );
                continue;
            }

            var size = (int) packetSize;

            // Always fire the event - avoid extra allocation by reusing receiveBuffer when possible
            if (buffer != null) {
                var bytesToCopy = System.Math.Min(size, length);
                Buffer.BlockCopy(_receiveBuffer, 0, buffer, offset, bytesToCopy);
                dataReceived(buffer, bytesToCopy);
                buffer = null; // Only copy the first packet to buffer
            } else {
                // Only allocate new array when necessary
                var data = new byte[size];
                Buffer.BlockCopy(_receiveBuffer, 0, data, 0, size);
                dataReceived(data, size);
            }
        }
    }

    /// <inheritdoc />
    public void Disconnect() {
        if (!_isConnected) return;

        if (_cachedLoopbackChannel != null) {
            _cachedLoopbackChannel.UnregisterClient();
            SteamLoopbackChannel.ReleaseIfEmpty();
            _cachedLoopbackChannel = null;
        }

        Logger.Info($"Steam P2P: Disconnecting from {_remoteSteamId}");

        _receiveTokenSource?.Cancel();

        if (_steamInitialized) {
            SteamNetworking.CloseP2PSessionWithUser(_remoteSteamId);
        }

        _remoteSteamId = NilSteamId;

        if (_receiveThread != null) {
            if (!_receiveThread.Join(5000)) {
                Logger.Warn("Steam P2P: Receive thread did not terminate within 5 seconds");
            }

            _receiveThread = null;
        }

        _isConnected = false;
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

        var cancellationToken = token.Token;
        var spinWait = new SpinWait();

        // Pre-calculate high-resolution sleep time
        var sleepMs = (int) PollIntervalMS;
        var remainingMicroseconds = (int) ((PollIntervalMS - sleepMs) * 1000);

        while (_isConnected && !cancellationToken.IsCancellationRequested) {
            try {
                // Exit cleanly if Steam shuts down (e.g., during forceful game closure)
                if (!SteamManager.IsInitialized) {
                    _steamInitialized = false;
                    Logger.Info("Steam P2P: Steam shut down, exiting receive loop");
                    break;
                }

                Receive(null, 0, 0);

                // Steam API does not provide a blocking receive or callback for P2P packets,
                // so we must poll. Sleep interval is tuned to achieve ~58Hz polling rate.
                // Hybrid approach: coarse sleep + fine-grained spin for precision
                Thread.Sleep(sleepMs);

                // SpinWait for sub-millisecond precision without busy-wait overhead
                if (remainingMicroseconds > 0) {
                    spinWait.Reset();
                    for (var i = 0; i < remainingMicroseconds; i++) {
                        spinWait.SpinOnce();
                    }
                }
            } catch (InvalidOperationException ex) when (ex.Message.Contains("Steamworks is not initialized")) {
                // Steam shut down during operation - exit gracefully
                _steamInitialized = false;
                Logger.Info("Steam P2P: Steamworks shut down during receive, exiting loop");
                break;
            } catch (ThreadAbortException) {
                // Thread is being aborted during shutdown - exit gracefully
                Logger.Info("Steam P2P: Receive thread aborted, exiting loop");
                break;
            } catch (Exception e) {
                Logger.Error($"Steam P2P: Error in receive loop: {e}");
            }
        }

        Logger.Info("Steam P2P: Receive loop exited cleanly");
    }

    /// <summary>
    /// Receives a packet from the loopback channel.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReceiveLoopbackPacket(byte[] data, int length) {
        if (!_isConnected) return;
        DataReceivedEvent?.Invoke(data, length);
    }
}

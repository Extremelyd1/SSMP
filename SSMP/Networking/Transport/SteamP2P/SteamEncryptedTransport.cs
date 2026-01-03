using System;
using System.Diagnostics;
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
/// Optimized for maximum performance with zero-allocation hot paths.
/// </summary>
internal sealed class SteamEncryptedTransport : IReliableTransport {
    /// <summary>
    /// Maximum Steam P2P packet size.
    /// </summary>
    private const int SteamMaxPacketSize = 1200;

    /// <summary>
    /// Polling interval in milliseconds for Steam P2P packet receive loop.
    /// 17.2ms achieves ~58Hz polling rate to balance responsiveness and CPU usage.
    /// </summary>
    private const double PollIntervalMS = 17.2;

    /// <summary>
    /// Maximum lag threshold before resetting timing to prevent spiral-of-death (ms).
    /// </summary>
    private const long MaxLagMS = 500;

    /// <summary>
    /// Channel ID for server->client communication.
    /// </summary>
    private const int ServerChannel = 1;



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
    /// Buffer for receiving P2P packets (aligned for better cache performance).
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
    /// Cached send types to avoid boxing and allocation.
    /// </summary>
    private static readonly EP2PSend UnreliableSendType = EP2PSend.k_EP2PSendUnreliableNoDelay;
    private static readonly EP2PSend ReliableSendType = EP2PSend.k_EP2PSendReliable;

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
            Priority = ThreadPriority.AboveNormal,
            Name = "Steam P2P Receive"
        };
        _receiveThread.Start();
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Send(byte[] buffer, int offset, int length) {
        SendInternal(buffer, offset, length, UnreliableSendType);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SendReliable(byte[] buffer, int offset, int length) {
        SendInternal(buffer, offset, length, ReliableSendType);
    }

    /// <summary>
    /// Internal helper to send data with a specific P2P send type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SendInternal(byte[] buffer, int offset, int length, EP2PSend sendType) {
        // Fast-path validation (likely branches first)
        if (!_isConnected | !_steamInitialized) {
            ThrowNotConnected();
        }

        if (_isLoopback) {
            // Use cached loopback channel (no null check needed - set during Connect)
            _cachedLoopbackChannel!.SendToServer(buffer, offset, length);
            return;
        }

        // Client sends to server on Channel 0
        if (!SteamNetworking.SendP2PPacket(_remoteSteamId, buffer, (uint)length, sendType)) {
            Logger.Warn($"Steam P2P: Failed to send packet to {_remoteSteamId}");
        }
    }

    /// <summary>
    /// Cold path for throwing connection exceptions (keeps hot path clean).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNotConnected() {
        throw new InvalidOperationException("Cannot send: not connected or Steam not initialized");
    }

    /// <summary>
    /// Process all available incoming P2P packets.
    /// Drains the entire queue to prevent packet buildup when polling.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReceivePackets() {
        // Early exit checks (use bitwise OR to avoid branch prediction penalty)
        if (!_isConnected | !_steamInitialized) return;

        // Cache event delegate to avoid repeated volatile field access
        var dataReceived = DataReceivedEvent;
        if (dataReceived == null) return;

        var receiveBuffer = _receiveBuffer;
        var remoteSteamId = _remoteSteamId;

        // Drain ALL available packets (matches server-side behavior)
        while (SteamNetworking.IsP2PPacketAvailable(out var packetSize, ServerChannel)) {
            // Client listens for server packets on Channel 1
            if (!SteamNetworking.ReadP2PPacket(
                    receiveBuffer,
                    SteamMaxPacketSize,
                    out packetSize,
                    out var senderSteamId,
                    ServerChannel
                )) {
                continue;
            }

            // Validate sender (security check)
            if (senderSteamId != remoteSteamId) {
                Logger.Warn(
                    $"Steam P2P: Received packet from unexpected peer {senderSteamId}, expected {remoteSteamId}"
                );
                continue;
            }

            var size = (int)packetSize;

            // Allocate a copy for safety - handlers may hold references
            var data = new byte[size];
            Array.Copy(receiveBuffer, 0, data, 0, size);
            dataReceived(data, size);
        }
    }

    /// <inheritdoc />
    public void Disconnect() {
        if (!_isConnected) return;

        // Signal shutdown first
        _isConnected = false;
        _receiveTokenSource?.Cancel();

        if (_cachedLoopbackChannel != null) {
            _cachedLoopbackChannel.UnregisterClient();
            SteamLoopbackChannel.ReleaseIfEmpty();
            _cachedLoopbackChannel = null;
        }

        Logger.Info($"Steam P2P: Disconnecting from {_remoteSteamId}");

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

        _receiveTokenSource?.Dispose();
        _receiveTokenSource = null;
    }

    /// <summary>
    /// Continuously polls for incoming P2P packets with precise timing.
    /// Uses Stopwatch + hybrid spin/sleep for consistent ~58Hz polling rate.
    /// Steam API limitation: no blocking receive or callback available, must poll.
    /// </summary>
    private void ReceiveLoop() {
        var token = _receiveTokenSource;
        if (token == null) return;

        var cancellationToken = token.Token;
        var stopwatch = Stopwatch.StartNew();
        var nextPollTime = stopwatch.ElapsedMilliseconds;
        const long pollInterval = (long)PollIntervalMS;

        using var waitHandle = new ManualResetEventSlim(false);

        while (_isConnected) {
            try {
                // Fast cancellation check without allocation
                if (cancellationToken.IsCancellationRequested) break;

                // Exit cleanly if Steam shuts down (e.g., during forceful game closure)
                if (!SteamManager.IsInitialized) {
                    _steamInitialized = false;
                    Logger.Info("Steam P2P: Steam shut down, exiting receive loop");
                    break;
                }

                var currentTime = stopwatch.ElapsedMilliseconds;

                // Lag spike recovery: if we're too far behind, reset to prevent spiral-of-death
                if (currentTime > nextPollTime + MaxLagMS) {
                    nextPollTime = currentTime;
                }

                var waitTime = nextPollTime - currentTime;
                if (waitTime > 0) {
                    try {
                        waitHandle.Wait((int)waitTime, cancellationToken);
                    } catch (OperationCanceledException) {
                        break;
                    }
                }

                // Poll for available packets (hot path)
                ReceivePackets();

                // Schedule next poll
                nextPollTime += pollInterval;
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
                // Continue polling on error, reset timing
                nextPollTime = stopwatch.ElapsedMilliseconds + pollInterval;
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

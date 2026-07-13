// ReSharper disable InconsistentlySynchronizedField

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using SSMP.Logging;
using SSMP.Networking.Packet.Data;

namespace SSMP.Networking.Chunk;

/// <summary>
/// Delegate for setting slice data in an update manager.
/// </summary>
/// <param name="chunkId">The ID of the chunk.</param>
/// <param name="sliceId">The ID of the slice.</param>
/// <param name="numSlices">The number of slices in the chunk.</param>
/// <param name="data">The slice data.</param>
internal delegate void SetSliceDataDelegate(byte chunkId, ushort sliceId, ushort numSlices, byte[] data);

/// <summary>
/// Class that processes and manages chunks by sending slices of those chunks and receiving acknowledgements for those
/// slices. Uses delegate injection instead of inheritance for flexibility.
/// </summary>
internal sealed class ChunkSender : IDisposable {
    /// <summary>
    /// The number of milliseconds to wait before re-sending a slice.
    /// </summary>
    private const int WaitMillisResendSlice = 100;

    /// <summary>
    /// The maximum number of slices that may be in-flight for a chunk at once.
    /// </summary>
    private const int MaxInFlightSlices = 64;

    /// <summary>
    /// The maximum number of chunk bytes that may be in-flight at once.
    /// </summary>
    private const int MaxInFlightBytes = 64 * 1024;

    /// <summary>
    /// Blocking collection of packets that need to be sent as chunks.
    /// </summary>
    private readonly BlockingCollection<Packet.Packet> _toSendPackets;

    /// <summary>
    /// Lock object for synchronizing access to shared sender state.
    /// </summary>
    private readonly object _stateLock = new();

    /// <summary>
    /// Lock object for synchronizing thread and cancellation token lifecycle (Start/Stop).
    /// </summary>
    private readonly object _lifecycleLock = new();

    /// <summary>
    /// Boolean array where each value indicates whether the slice of the same index was acknowledged.
    /// Allocated dynamically for the current chunk.
    /// </summary>
    private bool[]? _acked;

    /// <summary>
    /// Reference to the active chunk's bytes. Stored only during active sending.
    /// </summary>
    private byte[]? _currentChunkData;

    /// <summary>
    /// Manual reset event that is used for its wait handle to time when to send the next slice.
    /// </summary>
    private readonly ManualResetEventSlim _sliceWaitHandle;

    /// <summary>
    /// Whether we are currently sending a chunk. If we are not sending anything, we ignore incoming chunk
    /// acknowledgements.
    /// </summary>
    private bool _isSending;

    /// <summary>
    /// The ID of the chunk we are currently sending.
    /// </summary>
    private byte _chunkId;

    /// <summary>
    /// The size of the chunk we are currently sending.
    /// </summary>
    private int _chunkSize;

    /// <summary>
    /// The number of slices of the chunk we are currently sending.
    /// </summary>
    private int _numSlices;

    /// <summary>
    /// The number of acknowledged slices in the currently sending chunk.
    /// </summary>
    private int _numAckedSlices;

    /// <summary>
    /// The number of packets enqueued for sending. Synchronized under stateLock to prevent unsynchronized concurrent
    /// collection warning.
    /// </summary>
    private int _queuedPacketsCount;

    /// <summary>
    /// Flag indicating whether this chunk sender has been disposed.
    /// </summary>
    private bool _isDisposed;

    /// <summary>
    /// The ID of the slice we are currently sending.
    /// </summary>
    private int _currentSliceId;

    /// <summary>
    /// Array of millisecond timestamps (using Environment.TickCount64) representing when the slice of the same index
    /// was last sent. Used to enforce resend pacing.
    /// Allocated dynamically for the current chunk.
    /// </summary>
    private long[]? _sliceLastSentTicks;

    /// <summary>
    /// Boolean array indicating whether a slice is currently in-flight.
    /// </summary>
    private bool[]? _sliceInFlight;

    /// <summary>
    /// Lengths of all slices in the current chunk.
    /// </summary>
    private int[]? _sliceLengths;

    /// <summary>
    /// Number of slices currently in-flight.
    /// </summary>
    private int _numInFlightSlices;

    /// <summary>
    /// Number of bytes currently in-flight.
    /// </summary>
    private int _numInFlightBytes;

    /// <summary>
    /// Cancellation token source for cancelling the send task.
    /// </summary>
    private CancellationTokenSource? _sendTaskTokenSource;

    /// <summary>
    /// Reference to the active sender thread.
    /// </summary>
    private Thread? _senderThread;

    /// <summary>
    /// Event that is called when we finish sending data. This is registered internally when the
    /// <see cref="FinishSendingData"/> method is called, and we are waiting for the current chunk to finish sending.
    /// </summary>
    private event Action? FinishSendingDataEvent;

    /// <summary>
    /// Delegate for setting slice data in the update manager.
    /// </summary>
    private readonly SetSliceDataDelegate _setSliceData;

    /// <summary>
    /// Construct the chunk sender with the delegate for setting slice data.
    /// </summary>
    /// <param name="setSliceData">Delegate to call when sending slice data.</param>
    public ChunkSender(SetSliceDataDelegate setSliceData) {
        _setSliceData = setSliceData ?? throw new ArgumentNullException(nameof(setSliceData));

        _toSendPackets = new BlockingCollection<Packet.Packet>();
        _sliceWaitHandle = new ManualResetEventSlim();
    }

    /// <summary>
    /// Start the chunk sender by starting the thread that manages the chunk sending.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the ChunkSender has been disposed.</exception>
    public void Start() {
        lock (_lifecycleLock) {
            lock (_stateLock) {
                if (_isDisposed) {
                    throw new ObjectDisposedException(
                        nameof(ChunkSender), "Cannot start a disposed ChunkSender."
                    );
                }
            }

            _sendTaskTokenSource?.Cancel();
            if (_senderThread is { IsAlive: true } && Thread.CurrentThread != _senderThread) {
                _senderThread.Join();
            }

            _sendTaskTokenSource?.Dispose();
            _sendTaskTokenSource = new CancellationTokenSource();

            Reset();

            var token = _sendTaskTokenSource.Token;
            _senderThread = new Thread(() => StartSends(token)) {
                IsBackground = true,
                Name = "SSMP Chunk Sender Thread"
            };
            _senderThread.Start();
        }
    }

    /// <summary>
    /// Stop the chunk sender by cancelling the send task.
    /// </summary>
    public void Stop() {
        lock (_lifecycleLock) {
            _sendTaskTokenSource?.Cancel();
            if (_senderThread is { IsAlive: true } && Thread.CurrentThread != _senderThread) {
                _senderThread.Join();
            }

            _sendTaskTokenSource?.Dispose();
            _sendTaskTokenSource = null;

            // Reset state to ensure clean slate on disconnect/stop
            Reset();
        }
    }

    /// <summary>
    /// Dispose of the chunk sender and its owned disposable resources.
    /// </summary>
    public void Dispose() {
        Stop();
        lock (_stateLock) {
            _isDisposed = true;
        }

        _toSendPackets.Dispose();
        _sliceWaitHandle.Dispose();
    }

    /// <summary>
    /// Reset the chunk sender variables to their default values.
    /// </summary>
    private void Reset() {
        lock (_stateLock) {
            _isSending = false;
            _chunkId = 0;
            _chunkSize = 0;
            _numSlices = 0;
            _numAckedSlices = 0;
            _numInFlightSlices = 0;
            _numInFlightBytes = 0;
            _queuedPacketsCount = 0;
            _currentSliceId = 0;

            ReleaseArrays();
            _currentChunkData = null;
            FinishSendingDataEvent = null;

            // Clear the blocking collection under stateLock to prevent concurrent enqueue desynchronization
            while (_toSendPackets.TryTake(out _)) {
            }
        }
    }

    /// <summary>
    /// Finish sending data and call the given callback whenever the data is finished sending.
    /// </summary>
    /// <param name="callback">The callback to invoke.</param>
    public void FinishSendingData(Action callback) {
        // If we aren't currently sending and the queue does not contain any packets to send, we immediately invoke
        // the callback and return
        var executeImmediately = false;
        lock (_stateLock) {
            if (!_isSending && _queuedPacketsCount == 0) {
                executeImmediately = true;
            } else {
                // Otherwise, we register the event
                // We do it like this so we can deregister the event immediately after it is called, so it doesn't
                // trigger
                // more than once
                Action? lambda = null;
                lambda = () => {
                    callback.Invoke();
                    lock (_stateLock) {
                        FinishSendingDataEvent -= lambda;
                    }
                };
                FinishSendingDataEvent += lambda;
            }
        }

        if (executeImmediately) {
            callback.Invoke();
        }
    }

    /// <summary>
    /// Enqueue a packet to be sent as a chunk.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <exception cref="ArgumentNullException">Thrown if the packet is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the ChunkSender has been disposed.</exception>
    public void EnqueuePacket(Packet.Packet packet) {
        if (packet == null) {
            throw new ArgumentNullException(nameof(packet), "Cannot enqueue a null packet.");
        }

        lock (_stateLock) {
            if (_isDisposed) {
                throw new ObjectDisposedException(
                    nameof(ChunkSender), "Cannot enqueue packets to a disposed ChunkSender."
                );
            }

            _queuedPacketsCount++;
            _toSendPackets.Add(packet);
        }
    }

    /// <summary>
    /// Process received slice acknowledgement data. First does sanity checks to see if we are actually sending a
    /// chunk, whether the received chunk ID matches the currently sending chunk ID, and whether the number of slices
    /// matches. Then for each of the slice indices in the acknowledgement array, it checks whether this is a newly
    /// acknowledged slice and locally marks it as acknowledged.
    /// </summary>
    /// <param name="sliceAckData">The received slice acknowledgement data.</param>
    public void ProcessReceivedData(SliceAckData sliceAckData) {
        //Logger.Debug($"Received slice ack packet: {sliceAckData.ChunkId}, {sliceAckData.NumSlices}");

        lock (_stateLock) {
            if (!_isSending || _acked == null) {
                //Logger.Debug("Not sending a chunk, ignoring ack packet");
                return;
            }

            if (_chunkId != sliceAckData.ChunkId) {
                //Logger.Debug("Chunk ID of received ack packet does not correspond with currently sending chunk");
                return;
            }

            if (_numSlices != sliceAckData.NumSlices) {
                //Logger.Debug("Number of slices in ack packet does not correspond with local number of slices");
                return;
            }

            var newAckProcessed = false;
            for (var i = 0; i < _numSlices; i++) {
                if (!sliceAckData.Acked[i] || _acked[i]) {
                    continue;
                }

                _acked[i] = true;
                _numAckedSlices += 1;
                if (_sliceInFlight != null && _sliceInFlight[i]) {
                    _sliceInFlight[i] = false;
                    _numInFlightSlices -= 1;
                    if (_sliceLengths != null) {
                        _numInFlightBytes -= _sliceLengths[i];
                    }
                }

                newAckProcessed = true;

                //Logger.Debug($"Received acknowledgement for slice {i}, total acked: {_numAckedSlices}");
            }

            if (newAckProcessed) {
                _sliceWaitHandle.Set();
            }
        }
    }

    /// <summary>
    /// Start the sending process with the given cancellation token.
    /// We block on the collection to take a new packet to start sending. Once a packet is taken from the collection,
    /// we calculate the chunk size and number of slices that we need to send. Then, wee go over the slices in
    /// ascending order and send one with a given delay between each slice. Each slice that is acknowledged already
    /// is skipped in the sending order. If we have already sent a given slice less than a certain threshold ago, we
    /// also skip sending it. Once all slices have been acknowledged, we go back to blocking on the collection to wait
    /// for a new chunk to send.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for cancelling the sending process.</param>
    private void StartSends(CancellationToken cancellationToken) {
        try {
            while (!cancellationToken.IsCancellationRequested) {
                InvokeFinishCallbackIfIdle();

                if (!TryTakeNextPacket(cancellationToken, out var packet)) {
                    continue;
                }

                //Logger.Debug("Successfully taken new packet from blocking collection, starting networking chunk");

                if (!TryPrepareChunkForSending(packet)) {
                    continue;
                }

                RunSliceSendLoop(cancellationToken);

                if (cancellationToken.IsCancellationRequested) {
                    break;
                }

                AdvanceToNextChunk();
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            Logger.Error($"Fatal error in SSMP Chunk Sender background thread: {ex.Message}\n{ex.StackTrace}");
        } finally {
            lock (_stateLock) {
                ReleaseArrays();
            }
        }
    }

    /// <summary>
    /// If there are no packets currently queued, captures and invokes the registered
    /// <see cref="FinishSendingDataEvent"/> so subscribers are notified that the sender has gone idle.
    /// </summary>
    private void InvokeFinishCallbackIfIdle() {
        Action? eventToInvoke = null;
        lock (_stateLock) {
            if (_queuedPacketsCount == 0) {
                eventToInvoke = FinishSendingDataEvent;
            }
        }

        eventToInvoke?.Invoke();
    }

    /// <summary>
    /// Blocks on <see cref="_toSendPackets"/> until a packet is available or the token is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the blocking take.</param>
    /// <param name="packet">The packet taken from the queue, if successful.</param>
    /// <returns>False if the take was cancelled before a packet became available.</returns>
    private bool TryTakeNextPacket(CancellationToken cancellationToken, out Packet.Packet packet) {
        try {
            packet = _toSendPackets.Take(cancellationToken);
            return true;
        } catch (OperationCanceledException) {
            packet = null!;
            return false;
        }
    }

    /// <summary>
    /// Serializes the packet and initializes all per-chunk sending state (slice count, rented pacing/ack arrays,
    /// slice length table). On any validation failure or exception, cleans up after itself and decrements the
    /// queued-packet count exactly once.
    /// </summary>
    /// <param name="packet">The packet to prepare for chunked sending.</param>
    /// <returns>True if the chunk is ready to send; false if it was rejected or failed to serialize.</returns>
    private bool TryPrepareChunkForSending(Packet.Packet packet) {
        var decremented = false;
        try {
            var packetBytes = packet.ToArray();

            switch (packetBytes.Length) {
                // Skip over chunks that exceed the maximum size that our system can handle
                case > ConnectionManager.MaxChunkSize: {
                    Logger.Error($"Could not send packet that exceeds max chunk size: {packetBytes.Length}");
                    lock (_stateLock) {
                        _queuedPacketsCount--;
                        decremented = true;
                    }

                    return false;
                }
                case 0: {
                    Logger.Error("Cannot send an empty chunk packet.");
                    lock (_stateLock) {
                        _queuedPacketsCount--;
                        decremented = true;
                    }

                    return false;
                }
            }

            lock (_stateLock) {
                _queuedPacketsCount--;
                decremented = true;
                _chunkSize = packetBytes.Length;
                _numSlices = _chunkSize / ConnectionManager.MaxSliceSize;
                if (_chunkSize % ConnectionManager.MaxSliceSize != 0) {
                    _numSlices += 1;
                }

                _isSending = true;
                _acked = ArrayPool<bool>.Shared.Rent(_numSlices);
                _sliceLastSentTicks = ArrayPool<long>.Shared.Rent(_numSlices);
                _sliceInFlight = ArrayPool<bool>.Shared.Rent(_numSlices);
                _sliceLengths = ArrayPool<int>.Shared.Rent(_numSlices);
                Array.Clear(_acked, 0, _numSlices);
                Array.Clear(_sliceLastSentTicks, 0, _numSlices);
                Array.Clear(_sliceInFlight, 0, _numSlices);
                _numAckedSlices = 0;
                _numInFlightSlices = 0;
                _numInFlightBytes = 0;
                _currentSliceId = 0;

                for (var sliceId = 0; sliceId < _numSlices; sliceId++) {
                    var startIndex = sliceId * ConnectionManager.MaxSliceSize;
                    _sliceLengths[sliceId] = System.Math.Min(
                        ConnectionManager.MaxSliceSize,
                        _chunkSize - startIndex
                    );
                }

                // Reference the raw bytes from the packet dynamically
                _currentChunkData = packetBytes;
            }

            return true;
        } catch (Exception ex) {
            Logger.Error($"Error serializing packet for chunk sending: {ex.Message}");
            lock (_stateLock) {
                if (!decremented) {
                    _queuedPacketsCount--;
                }

                ReleaseArrays();
                _currentChunkData = null;
                _isSending = false;
            }

            return false;
        }
    }

    /// <summary>
    /// Repeatedly sends the next due slice of the chunk currently being prepared, pacing resends and waiting
    /// between attempts, until every slice has been acknowledged or the token is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel sending and the inter-slice wait.</param>
    private void RunSliceSendLoop(CancellationToken cancellationToken) {
        do {
            _sliceWaitHandle.Reset();

            if (TryGetNextSliceToSend(out var waitMillisNextSlice, out var completed)) {
                SendNextSlice();
                continue;
            }

            if (completed) {
                break;
            }

            try {
                _sliceWaitHandle.Wait(waitMillisNextSlice, cancellationToken);
            } catch (OperationCanceledException) {
                break;
            }
        } while (!cancellationToken.IsCancellationRequested);
    }

    /// <summary>
    /// Advances to the next chunk ID and releases all per-chunk sending state now that the current chunk has
    /// finished sending (fully acknowledged). Must only be called when not cancelled.
    /// </summary>
    private void AdvanceToNextChunk() {
        //Logger.Debug($"Incrementing chunk ID to: {_chunkId + 1}");
        lock (_stateLock) {
            _chunkId += 1;
            ReleaseArrays();
            _currentChunkData = null;
            _isSending = false;
        }
    }

    /// <summary>
    /// Send the next slice, whose ID is <see cref="_currentSliceId"/>. This will figure out the start index of the
    /// data in the array and copy the data into a new array for adding to the update packet.
    /// </summary>
    private void SendNextSlice() {
        byte[]? currentChunkData;
        int currentSliceId;
        int numSlices;
        byte chunkId;
        int chunkSize;

        lock (_stateLock) {
            if (_currentChunkData == null) return;
            currentChunkData = _currentChunkData;
            currentSliceId = _currentSliceId;
            numSlices = _numSlices;
            chunkId = _chunkId;
            chunkSize = _chunkSize;
        }

        var startIndex = currentSliceId * ConnectionManager.MaxSliceSize;

        int sliceLength;
        // Figure out if the start index for the next slice would exceed the chunk size. If so, the length of the slice
        // is less than the maximum slice size, which we need to calculate
        if ((currentSliceId + 1) * ConnectionManager.MaxSliceSize > chunkSize) {
            sliceLength = chunkSize - startIndex;
        } else {
            sliceLength = ConnectionManager.MaxSliceSize;
        }

        var sliceBytes = new byte[sliceLength];
        Array.Copy(currentChunkData, startIndex, sliceBytes, 0, sliceLength);

        lock (_stateLock) {
            if (_sliceInFlight != null && !_sliceInFlight[currentSliceId]) {
                _sliceInFlight[currentSliceId] = true;
                _numInFlightSlices += 1;
                _numInFlightBytes += sliceLength;
            }

            _sliceLastSentTicks?[currentSliceId] = Stopwatch.GetTimestamp();
        }

        _setSliceData.Invoke(chunkId, (ushort) currentSliceId, (ushort) numSlices, sliceBytes);
    }

    /// <summary>
    /// Try to get the next slice ID that we need to send. We simply iterate in ascending order over slice IDs until
    /// we find one that is not yet acknowledged. Each iteration we check whether the number of acknowledged slices
    /// equals the number of slices in the chunk, so we don't end up in an infinite loop.
    /// </summary>
    /// <returns>True if a next slice could be found, false if all slices are acknowledged.</returns>
    private bool TryGetNextSliceToSend(out int waitMillis, out bool completed) {
        lock (_stateLock) {
            waitMillis = Timeout.Infinite;
            completed = false;

            if (_acked == null || _sliceLengths == null || _sliceInFlight == null) {
                return false;
            }

            if (_numAckedSlices == _numSlices) {
                completed = true;
                return false;
            }

            for (var offset = 1; offset <= _numSlices; offset++) {
                var candidateSliceId = (_currentSliceId + offset) % _numSlices;
                if (_acked[candidateSliceId]) {
                    continue;
                }

                var sliceLength = _sliceLengths[candidateSliceId];
                if (_sliceInFlight[candidateSliceId]) {
                    if (_sliceLastSentTicks == null) {
                        continue;
                    }

                    var lastSent = _sliceLastSentTicks[candidateSliceId];
                    var elapsedMillis = lastSent == 0
                        ? WaitMillisResendSlice
                        : (Stopwatch.GetTimestamp() - lastSent) * 1000 / Stopwatch.Frequency;
                    var remainingMillis = WaitMillisResendSlice - (int) elapsedMillis;
                    if (remainingMillis <= 0) {
                        _currentSliceId = candidateSliceId;
                        return true;
                    }

                    waitMillis = waitMillis == Timeout.Infinite
                        ? remainingMillis
                        : System.Math.Min(waitMillis, remainingMillis);
                    continue;
                }

                if (!HasWindowCapacity(sliceLength)) {
                    continue;
                }

                _currentSliceId = candidateSliceId;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Return the rented ack and pacing arrays to the Shared ArrayPool.
    /// </summary>
    private void ReleaseArrays() {
        if (_acked != null) {
            ArrayPool<bool>.Shared.Return(_acked);
            _acked = null;
        }

        if (_sliceLastSentTicks != null) {
            ArrayPool<long>.Shared.Return(_sliceLastSentTicks);
            _sliceLastSentTicks = null;
        }

        if (_sliceInFlight != null) {
            ArrayPool<bool>.Shared.Return(_sliceInFlight);
            _sliceInFlight = null;
        }

        if (_sliceLengths != null) {
            ArrayPool<int>.Shared.Return(_sliceLengths);
            _sliceLengths = null;
        }

        _numInFlightSlices = 0;
        _numInFlightBytes = 0;
    }

    /// <summary>
    /// Checks whether the sender can emit a new slice without exceeding the in-flight window.
    /// </summary>
    private bool HasWindowCapacity(int sliceLength) {
        return _numInFlightSlices < MaxInFlightSlices &&
               _numInFlightBytes + sliceLength <= MaxInFlightBytes;
    }
}

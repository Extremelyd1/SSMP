using System;
using System.Diagnostics;
using SSMP.Logging;
using SSMP.Networking.Packet.Data;

namespace SSMP.Networking.Chunk;

/// <summary>
/// Delegate for setting slice acknowledgement data in an update manager.
/// </summary>
/// <param name="chunkId">The ID of the chunk.</param>
/// <param name="numSlices">The number of slices in the chunk.</param>
/// <param name="acked">The acknowledgement array.</param>
internal delegate void SetSliceAckDataDelegate(byte chunkId, ushort numSlices, bool[] acked);

/// <summary>
/// Class that processes and manages chunks by receiving slices of those chunks and sending acknowledgements for those
/// slices. Uses delegate injection instead of inheritance for flexibility.
/// </summary>
internal sealed class ChunkReceiver {
    /// <summary>
    /// The number of milliseconds after which an incomplete chunk is considered stale and reset.
    /// </summary>
    private const int ReceiveTimeoutMillis = 5000;

    /// <summary>
    /// Lock object for synchronizing access to shared receiver state.
    /// </summary>
    private readonly object _stateLock = new();

    /// <summary>
    /// Boolean array where each value indicates whether the slice of the same index was received.
    /// Allocated dynamically for the current chunk.
    /// </summary>
    private bool[]? _received;

    /// <summary>
    /// Array of byte array segments representing the received slices of the current chunk.
    /// Allocated dynamically for the current chunk.
    /// </summary>
    private byte[][]? _sliceSegments;

    /// <summary>
    /// Whether we are currently receiving a chunk. If not, receiving a slice containing a chunk ID that is one higher
    /// that the last received chunk will start the reception process again.
    /// </summary>
    private bool _isReceiving;

    /// <summary>
    /// The currently (if receiving) or last received (when not receiving) chunk ID.
    /// Null if no chunk has been received yet.
    /// </summary>
    private byte? _chunkId;

    /// <summary>
    /// The size of the chunk that we are currently receiving. Only calculated when the last slice is received, since
    /// that is the only slice with a different slice size.
    /// </summary>
    private int _chunkSize;

    /// <summary>
    /// The number of slices that the chunk we are currently receiving contains. Set whenever we receive the first
    /// slice in a chunk.
    /// </summary>
    private int _numSlices;

    /// <summary>
    /// The number of slices we have received so far. Used to keep track when all slices are received.
    /// </summary>
    private int _numReceivedSlices;

    /// <summary>
    /// Timestamp of when the last slice for the current chunk was received.
    /// </summary>
    private long _lastReceiveTimestamp;

    /// <summary>
    /// The maximum chunk size currently accepted by this receiver.
    /// </summary>
    public int MaxAllowedChunkSize { get; set; } = ConnectionManager.MaxChunkSize;

    /// <summary>
    /// Event that is called when the entirety of a chunk is received.
    /// </summary>
    public event Action<Packet.Packet>? ChunkReceivedEvent;

    /// <summary>
    /// Delegate for setting slice acknowledgement data in the update manager.
    /// </summary>
    private readonly SetSliceAckDataDelegate _setSliceAckData;

    /// <summary>
    /// Construct the chunk receiver with the delegate for setting slice acknowledgement data.
    /// </summary>
    /// <param name="setSliceAckData">Delegate to call when sending acknowledgement data.</param>
    public ChunkReceiver(SetSliceAckDataDelegate setSliceAckData) {
        _setSliceAckData = setSliceAckData ?? throw new ArgumentNullException(nameof(setSliceAckData));
    }


    /// <summary>
    /// Process received slice data by checking whether we have not yet received this slice and adding it to the data
    /// array and marking it received. If this is the first slice received in this chunk we note that we are
    /// receiving, set the number of slices we expect to receive and increment the currently receiving chunk ID.
    /// If this is the last slice in the chunk we invoke the event that an entire chunk is received.
    /// </summary>
    /// <param name="sliceData">The received slice data.</param>
    public void ProcessReceivedData(SliceData sliceData) {
        Packet.Packet? completedPacket = null;
        bool shouldSendAck;

        lock (_stateLock) {
            ResetIfTimedOut();

            if (IsStaleChunkId(sliceData)) {
                return;
            }

            if (!IsSliceDataValid(sliceData)) {
                return;
            }

            switch (ResolveChunkState(sliceData)) {
                case ChunkResolution.Ignore:
                    return;

                case ChunkResolution.DuplicateOfCompleted:
                    shouldSendAck = true;
                    break;

                case ChunkResolution.Accept:
                    if (!StoreSlice(sliceData, out var isChunkComplete)) {
                        // Duplicate within the current chunk, out-of-bounds SliceId, or rejected as oversized.
                        // No ack in any of these cases - matches original behavior.
                        return;
                    }

                    shouldSendAck = true;

                    if (isChunkComplete) {
                        completedPacket = AssembleAndResetChunk();
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // Perform delegate invocation and event triggering outside the state lock to prevent deadlocks
        if (shouldSendAck) {
            SendAckData();
        }

        if (completedPacket != null) {
            ChunkReceivedEvent?.Invoke(completedPacket);
        }
    }

    /// <summary>
    /// Resets receiving state if the in-progress chunk has gone too long without a new slice. No-op if we are
    /// not currently receiving a chunk. Must be called while holding <see cref="_stateLock"/>.
    /// </summary>
    private void ResetIfTimedOut() {
        if (_isReceiving && IsTimedOut()) {
            SoftReset();
            _isReceiving = false;
            _chunkId = null;
        }
    }

    /// <summary>
    /// Checks whether the slice's chunk ID is older than the chunk we're currently tracking, accounting for ID
    /// wraparound. Stale slices are dropped silently (no ack), since acking them could confuse the sender about
    /// which chunk we're actually on.
    /// </summary>
    private bool IsStaleChunkId(SliceData sliceData) {
        return _chunkId.HasValue && ConnectionManager.IsWrappingIdSmaller(sliceData.ChunkId, _chunkId.Value);
    }

    /// <summary>
    /// Validates structural bounds on incoming slice data before any state mutation happens. Rejects malformed
    /// slices and slice counts large enough to indicate an oversized-chunk attempt.
    /// </summary>
    private bool IsSliceDataValid(SliceData sliceData) {
        if (sliceData.NumSlices is < 1 or > ConnectionManager.MaxSlicesPerChunk) {
            Logger.Error($"Invalid slice count received: {sliceData.NumSlices}");
            return false;
        }

        if (sliceData.SliceId >= sliceData.NumSlices) {
            Logger.Error($"Invalid SliceId {sliceData.SliceId} for NumSlices {sliceData.NumSlices}");
            return false;
        }

        if (sliceData.Data.Length > ConnectionManager.MaxSliceSize) {
            Logger.Error($"Invalid slice data length: {sliceData.Data.Length}");
            return false;
        }

        var maxAllowedSlices = (MaxAllowedChunkSize + ConnectionManager.MaxSliceSize - 1) /
                               ConnectionManager.MaxSliceSize;
        if (sliceData.NumSlices > maxAllowedSlices) {
            Logger.Error(
                $"Rejected chunk with {sliceData.NumSlices} slices because it exceeds the allowed limit of {maxAllowedSlices}."
            );
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines how the slice relates to our current receiving state, and mutates that state if the slice
    /// starts a brand-new chunk. Must be called while holding <see cref="_stateLock"/>.
    /// </summary>
    private ChunkResolution ResolveChunkState(SliceData sliceData) {
        switch (_isReceiving) {
            // Ignore slices from newer chunks while the current chunk is still incomplete.
            case true when _chunkId.HasValue && sliceData.ChunkId != _chunkId.Value:
                return ChunkResolution.Ignore;

            case false when !_chunkId.HasValue || sliceData.ChunkId != _chunkId.Value:
                BeginNewChunk(sliceData);
                return ChunkResolution.Accept;

            case false:
                // _isReceiving is false and the chunk ID matches: we already fully received this chunk and the
                // sender is re-sending because our ack was lost. Re-ack without reprocessing.
                return ChunkResolution.DuplicateOfCompleted;

            default:
                // _isReceiving is true here, and within this class _chunkId is always set alongside _isReceiving
                // (see BeginNewChunk), so the chunk ID is guaranteed to match. We still rely on _numSlices to
                // catch a malformed/mismatched resend rather than trusting the match blindly.
                return _numSlices != sliceData.NumSlices ? ChunkResolution.Ignore : ChunkResolution.Accept;
        }
    }

    /// <summary>
    /// Resets local receiving state and begins tracking a new chunk identified by <paramref name="sliceData"/>.
    /// </summary>
    private void BeginNewChunk(SliceData sliceData) {
        SoftReset();

        _chunkId = sliceData.ChunkId;
        _isReceiving = true;
        _numSlices = sliceData.NumSlices;
        _received = new bool[_numSlices];
        _sliceSegments = new byte[_numSlices][];
        _lastReceiveTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Stores a slice belonging to the chunk currently being received, correcting the tracked chunk size once
    /// the last-indexed slice arrives. Must be called while holding <see cref="_stateLock"/>, and only when
    /// <see cref="ResolveChunkState"/> returned <see cref="ChunkResolution.Accept"/>.
    /// </summary>
    /// <param name="sliceData">The slice to store.</param>
    /// <param name="isChunkComplete">True if this was the final slice needed to complete the chunk.</param>
    /// <returns>
    /// False if the slice was a duplicate, out of bounds, or the corrected chunk size exceeded the allowed
    /// maximum (receiving state has already been reset in that case). True otherwise.
    /// </returns>
    private bool StoreSlice(SliceData sliceData, out bool isChunkComplete) {
        isChunkComplete = false;

        if (_received == null || _sliceSegments == null) {
            return false;
        }

        if (sliceData.SliceId >= _received.Length) {
            return false;
        }

        if (_received[sliceData.SliceId]) {
            // Duplicate slice within the current chunk; ignore.
            return false;
        }

        _numReceivedSlices += 1;
        _received[sliceData.SliceId] = true;
        _lastReceiveTimestamp = Stopwatch.GetTimestamp();

        // Store a reference to the received slice data segment directly to avoid GC LOH allocation pressure
        _sliceSegments[sliceData.SliceId] = sliceData.Data;

        // Whenever the last-ID slice arrives, correct the chunk size to account for its (potentially partial)
        // length. This must happen before the completion check below so out-of-order delivery is handled correctly.
        if (sliceData.SliceId == _numSlices - 1) {
            _chunkSize = (_numSlices - 1) * ConnectionManager.MaxSliceSize + sliceData.Data.Length;
            if (_chunkSize > MaxAllowedChunkSize) {
                Logger.Error($"Rejected chunk larger than allowed max size: {_chunkSize}");
                SoftReset();
                _isReceiving = false;
                _chunkId = null;
                return false;
            }
        }

        isChunkComplete = _numReceivedSlices == _numSlices;
        return true;
    }

    /// <summary>
    /// Builds the final packet from all received slice segments once a chunk is fully assembled, and releases
    /// the per-slice buffer now that it's no longer needed. Must only be called once <see cref="StoreSlice"/>
    /// has reported the chunk complete.
    /// </summary>
    private Packet.Packet AssembleAndResetChunk() {
        var byteArray = new byte[_chunkSize];
        var offset = 0;
        for (var i = 0; i < _numSlices; i++) {
            var segment = _sliceSegments![i];
            Array.Copy(segment, 0, byteArray, offset, segment.Length);
            offset += segment.Length;
        }

        _sliceSegments = null;
        _isReceiving = false;

        return new Packet.Packet(byteArray);
    }

    /// <summary>
    /// Reset the chunk receiver so it can be used for a new connection. This will reset most variables to their
    /// default values.
    /// </summary>
    public void Reset() {
        lock (_stateLock) {
            SoftReset();

            _isReceiving = false;
            _chunkId = null;
        }
    }

    /// <summary>
    /// Send acknowledgement data containing the boolean array of all slices that have been acknowledged thus far.
    /// </summary>
    private void SendAckData() {
        var shouldSendAck = false;
        byte ackChunkId = 0;
        ushort ackNumSlices = 0;
        bool[]? ackedSlices = null;

        lock (_stateLock) {
            if (_received != null && _chunkId.HasValue) {
                shouldSendAck = true;
                ackChunkId = _chunkId.Value;
                ackNumSlices = (ushort) _numSlices;
                ackedSlices = new bool[_numSlices];
                Array.Copy(_received, ackedSlices, _numSlices);
            }
        }

        if (shouldSendAck && ackedSlices != null) {
            _setSliceAckData(ackChunkId, ackNumSlices, ackedSlices);
        }
    }

    /// <summary>
    /// Soft reset the chunk receiver by clearing the array of received slices and setting chunk size, number of
    /// slices, and number of received slices to 0.
    /// </summary>
    private void SoftReset() {
        _received = null;
        _sliceSegments = null;

        _chunkSize = 0;
        _numSlices = 0;
        _numReceivedSlices = 0;
        _lastReceiveTimestamp = 0;
    }

    /// <summary>
    /// Checks whether the active partial chunk has timed out.
    /// </summary>
    private bool IsTimedOut() {
        if (_lastReceiveTimestamp == 0) {
            return false;
        }

        var elapsedMillis = (Stopwatch.GetTimestamp() - _lastReceiveTimestamp) * 1000 / Stopwatch.Frequency;
        return elapsedMillis > ReceiveTimeoutMillis;
    }
}

/// <summary>
/// Describes how an incoming slice relates to the chunk currently being tracked.
/// </summary>
internal enum ChunkResolution {
    /// <summary>Slice belongs to a chunk newer than the one we're currently receiving; drop it for now.</summary>
    Ignore,

    /// <summary>Slice belongs to a chunk we already fully received; our prior ack was likely lost.</summary>
    DuplicateOfCompleted,

    /// <summary>Slice belongs to the chunk we are currently receiving (just started, or already in progress).</summary>
    Accept
}

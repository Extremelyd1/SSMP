using System;
using System.Diagnostics;
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
        bool shouldSendAck = false;
        bool shouldTriggerEvent = false;
        bool isStaleDuplicate = false;
        Packet.Packet? packetToTrigger = null;

        lock (_stateLock) {
            //Logger.Debug($"Received slice packet: {sliceData.ChunkId}, {sliceData.SliceId}, {sliceData.NumSlices}");

            if (_isReceiving && IsTimedOut()) {
                SoftReset();
                _isReceiving = false;
                _chunkId = null;
            }

            // We check if the received chunk ID is smaller than the current chunk ID accounting for wrapping IDs
            if (_chunkId.HasValue && ConnectionManager.IsWrappingIdSmaller(sliceData.ChunkId, _chunkId.Value)) {
                //Logger.Debug("Chunk ID of received slice packet is smaller than currently receiving chunk");
                return;
            }

            // Validate slice count and ID bounds
            if (sliceData.NumSlices is < 1 or > ConnectionManager.MaxSlicesPerChunk) {
                Logging.Logger.Error($"Invalid slice count received: {sliceData.NumSlices}");
                return;
            }

            if (sliceData.SliceId >= sliceData.NumSlices) {
                Logging.Logger.Error($"Invalid SliceId {sliceData.SliceId} for NumSlices {sliceData.NumSlices}");
                return;
            }

            if (sliceData.Data.Length > ConnectionManager.MaxSliceSize) {
                Logging.Logger.Error($"Invalid slice data length: {sliceData.Data.Length}");
                return;
            }

            var maxAllowedSlices = (MaxAllowedChunkSize + ConnectionManager.MaxSliceSize - 1) /
                                   ConnectionManager.MaxSliceSize;
            if (sliceData.NumSlices > maxAllowedSlices) {
                Logging.Logger.Error(
                    $"Rejected chunk with {sliceData.NumSlices} slices because it exceeds the allowed limit of {maxAllowedSlices}."
                );
                return;
            }

            // Ignore slices from newer chunks while the current chunk is still incomplete.
            if (_isReceiving && _chunkId.HasValue && sliceData.ChunkId != _chunkId.Value) {
                return;
            }

            if (!_isReceiving) {
                if (!_chunkId.HasValue || sliceData.ChunkId != _chunkId.Value) {
                    //Logger.Debug($"Received new chunk with ID: {sliceData.ChunkId}");
                    SoftReset();

                    _chunkId = sliceData.ChunkId;
                    _isReceiving = true;
                    _numSlices = sliceData.NumSlices;
                    _received = new bool[_numSlices];
                    _sliceSegments = new byte[_numSlices][];
                    _lastReceiveTimestamp = Stopwatch.GetTimestamp();
                } else if (sliceData.ChunkId == _chunkId.Value) {
                    //Logger.Debug("Already received all slices, resending ack packet");
                    shouldSendAck = true;
                    isStaleDuplicate = true;
                }
            } else {
                // If the received number of slices does not match the number slices we are keeping track of, we discard
                // the slice altogether as it is likely not correct
                if (_numSlices != sliceData.NumSlices) {
                    //Logger.Debug("Number of slices in slice packet does not correspond with local number of slices");
                    return;
                }
            }

            if (!isStaleDuplicate) {
                if (_received == null || _sliceSegments == null) return;
                if (sliceData.SliceId >= _received.Length) return;

                if (_received[sliceData.SliceId]) {
                    //Logger.Debug($"Received duplicate slice: {sliceData.SliceId}, ignoring");
                    return;
                }

                _numReceivedSlices += 1;
                _received[sliceData.SliceId] = true;
                _lastReceiveTimestamp = Stopwatch.GetTimestamp();

                // Store a reference to the received slice data segment directly to avoid GC LOH allocation pressure
                _sliceSegments[sliceData.SliceId] = sliceData.Data;

                // Whenever the last-ID slice arrives, correct the chunk size to account for its (potentially partial)
                // length.
                // This must happen before the assembly check below so that out-of-order delivery is handled correctly.
                if (sliceData.SliceId == _numSlices - 1) {
                    _chunkSize = (_numSlices - 1) * ConnectionManager.MaxSliceSize + sliceData.Data.Length;
                    if (_chunkSize > MaxAllowedChunkSize) {
                        Logging.Logger.Error($"Rejected chunk larger than allowed max size: {_chunkSize}");
                        SoftReset();
                        _isReceiving = false;
                        _chunkId = null;
                        return;
                    }
                    //Logger.Debug($"Corrected chunk size after receiving last-ID slice: {_chunkSize}");
                }

                shouldSendAck = true;

                if (_numReceivedSlices == _numSlices) {
                    var byteArray = new byte[_chunkSize];
                    var offset = 0;
                    for (var i = 0; i < _numSlices; i++) {
                        var segment = _sliceSegments[i];
                        Array.Copy(segment, 0, byteArray, offset, segment.Length);
                        offset += segment.Length;
                    }

                    packetToTrigger = new Packet.Packet(byteArray);

                    _sliceSegments = null;
                    shouldTriggerEvent = true;
                    _isReceiving = false;
                }
            }
        }

        // Perform delegate invocation and event triggering outside the state lock to prevent deadlocks
        if (shouldSendAck) {
            SendAckData();
        }

        if (shouldTriggerEvent && packetToTrigger != null) {
            ChunkReceivedEvent?.Invoke(packetToTrigger);
        }
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
        bool shouldSendAck = false;
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

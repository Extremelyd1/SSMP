using System.Collections.Concurrent;
using System.Diagnostics;

namespace SSMP.Networking;

/// <summary>
/// Tracks round-trip times (RTT) for sent packets using exponential moving average.
/// Provides adaptive RTT measurements for reliability and congestion management.
/// </summary>
internal sealed class RttTracker {
    /// <summary>
    /// Initial connection timeout in milliseconds before first ACK is received.
    /// </summary>
    private const int InitialConnectionTimeout = 5000;

    /// <summary>
    /// Minimum RTT threshold in milliseconds.
    /// </summary>
    private const int MinRttThreshold = 200;

    /// <summary>
    /// Maximum RTT threshold in milliseconds.
    /// </summary>
    private const int MaxRttThreshold = 1000;

    /// <summary>
    /// EMA smoothing factor (0.1 = 10% of new sample, 90% of existing average).
    /// </summary>
    private const float RttSmoothingFactor = 0.1f;

    /// <summary>
    /// Loss detection multiplier (2x RTT).
    /// </summary>
    private const int LossDetectionMultiplier = 2;

    /// <summary>
    /// Dictionary tracking packets by sequence number with their send timestamp (from Stopwatch.GetTimestamp()).
    /// </summary>
    private readonly ConcurrentDictionary<ushort, long> _trackedPackets = new();

    /// <summary>
    /// Indicates whether the first acknowledgment has been received.
    /// </summary>
    private bool _firstAckReceived;

    /// <summary>
    /// Gets the current smoothed round-trip time in milliseconds.
    /// Uses exponential moving average for stable measurements.
    /// </summary>
    public float AverageRtt { get; private set; }

    /// <summary>
    /// Gets the adaptive timeout threshold for packet loss detection.
    /// Returns 2× average RTT, clamped between 200-1000ms after first ACK,
    /// or 5000ms during initial connection phase.
    /// </summary>
    public int MaximumExpectedRtt {
        get {
            if (!_firstAckReceived)
                return InitialConnectionTimeout;

            // Adaptive timeout: 2×RTT, clamped to reasonable bounds
            var adaptiveTimeout = (int) System.Math.Ceiling(AverageRtt * LossDetectionMultiplier);
            return System.Math.Clamp(adaptiveTimeout, MinRttThreshold, MaxRttThreshold);
        }
    }

    /// <summary>
    /// Maximum number of packets to track before starting cleanup.
    /// Cleanup removes the oldest expected sequence on each send.
    /// </summary>
    private const int MaxTrackedPackets = 128;

    /// <summary>
    /// Begins tracking round-trip time for a packet with the given sequence number.
    /// </summary>
    /// <param name="sequence">The packet sequence number to track.</param>
    public void OnSendPacket(ushort sequence) {
        _trackedPackets[sequence] = Stopwatch.GetTimestamp();

        // Remove an arbitrary tracked sequence if the dictionary is getting too large.
        // This runs once per send and removes at most one entry, preventing unbounded growth
        // while keeping the amount of cleanup work per send bounded.
        if (_trackedPackets.Count > MaxTrackedPackets) {
            foreach (var key in _trackedPackets.Keys) {
                _trackedPackets.TryRemove(key, out _);
                break;
            }
        }
    }

    /// <summary>
    /// Records acknowledgment receipt and updates RTT statistics.
    /// </summary>
    /// <param name="sequence">The acknowledged packet sequence number.</param>
    public void OnAckReceived(ushort sequence) {
        if (!_trackedPackets.TryRemove(sequence, out long timestamp))
            return;

        _firstAckReceived = true;
        
        long elapsedTicks = Stopwatch.GetTimestamp() - timestamp;
        long elapsedMs = elapsedTicks * 1000 / Stopwatch.Frequency;
        
        UpdateAverageRtt(elapsedMs);
    }

    /// <summary>
    /// Removes a packet from tracking (e.g., when marked as lost).
    /// </summary>
    /// <param name="sequence">The packet sequence number to stop tracking.</param>
    public void StopTracking(ushort sequence) {
        _trackedPackets.TryRemove(sequence, out _);
    }

    /// <summary>
    /// Updates the smoothed RTT using exponential moving average.
    /// Formula: SRTT = (1 - α) × SRTT + α × RTT, where α = 0.1
    /// </summary>
    private void UpdateAverageRtt(long measuredRtt) {
        AverageRtt = AverageRtt == 0
            ? measuredRtt
            : AverageRtt + (measuredRtt - AverageRtt) * RttSmoothingFactor;
    }
    
    /// <summary>
    /// Resets the RTT tracker to its initial state.
    /// Clears all tracked packets and resets RTT statistics.
    /// </summary>
    public void Reset() {
        _trackedPackets.Clear();
        _firstAckReceived = false;
        AverageRtt = 0;
    }
}

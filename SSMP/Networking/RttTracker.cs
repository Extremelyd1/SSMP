using System.Collections.Concurrent;
using System.Diagnostics;

namespace SSMP.Networking;

/// <summary>
/// Tracks round-trip times (RTT) for sent packets using exponential moving average.
/// Provides adaptive RTT measurements for reliability and congestion management.
/// </summary>
internal sealed class RttTracker {
    // RTT Bounds (milliseconds)
    private const int InitialConnectionTimeout = 5000;
    private const int MinRttThreshold = 200;
    private const int MaxRttThreshold = 1000;

    // EMA smoothing factor (0.1 = 10% of new sample, 90% of existing average)
    private const float RttSmoothingFactor = 0.1f;

    // Loss detection multiplier (2x RTT)
    private const int LossDetectionMultiplier = 2;

    private readonly ConcurrentDictionary<ushort, Stopwatch> _trackedPackets = new();
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
    /// Begins tracking round-trip time for a packet with the given sequence number.
    /// </summary>
    /// <param name="sequence">The packet sequence number to track.</param>
    public void OnSendPacket(ushort sequence) => _trackedPackets[sequence] = Stopwatch.StartNew();


    /// <summary>
    /// Records acknowledgment receipt and updates RTT statistics.
    /// </summary>
    /// <param name="sequence">The acknowledged packet sequence number.</param>
    public void OnAckReceived(ushort sequence) {
        if (!_trackedPackets.TryRemove(sequence, out Stopwatch? stopwatch))
            return;

        _firstAckReceived = true;
        UpdateAverageRtt(stopwatch.ElapsedMilliseconds);
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
}

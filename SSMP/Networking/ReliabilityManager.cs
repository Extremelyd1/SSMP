using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using SSMP.Networking.Packet.Update;

namespace SSMP.Networking;

/// <summary>
/// Manages packet reliability by detecting lost packets and triggering resends.
/// Uses RttTracker for RTT-based loss detection.
/// </summary>
internal class ReliabilityManager<TOutgoing, TPacketId>(
    UpdateManager<TOutgoing, TPacketId> updateManager,
    RttTracker rttTracker
)
    where TOutgoing : UpdatePacket<TPacketId>, new()
    where TPacketId : Enum {
    private readonly ConcurrentDictionary<ushort, TrackedPacket> _sentPackets = new();

    /// <summary>
    /// Records that a packet was sent for reliability tracking.
    /// </summary>
    public void OnSendPacket(ushort sequence, TOutgoing packet) {
        CheckForLostPackets();
        _sentPackets[sequence] = new TrackedPacket { Packet = packet };
    }

    /// <summary>
    /// Records that an ACK was received, removing the packet from tracking.
    /// </summary>
    public void OnAckReceived(ushort sequence) {
        _sentPackets.TryRemove(sequence, out _);
    }

    /// <summary>
    /// Checks all sent packets for those exceeding maximum expected RTT.
    /// Marks them as lost and resends reliable data if needed.
    /// </summary>
    private void CheckForLostPackets() {
        var maxExpectedRtt = rttTracker.MaximumExpectedRtt;
        long currentTimestamp = Stopwatch.GetTimestamp();

        foreach (var (key, tracked) in _sentPackets) {
            long elapsedTicks = currentTimestamp - tracked.Timestamp;
            long elapsedMs = elapsedTicks * 1000 / Stopwatch.Frequency;

            if (tracked.Lost || elapsedMs <= maxExpectedRtt) {
                continue;
            }

            tracked.Lost = true;
            rttTracker.StopTracking(key);
            if (tracked.Packet.ContainsReliableData) {
                updateManager.ResendReliableData(tracked.Packet);
            }
        }
    }

    /// <summary>
    /// Tracks a sent packet with its stopwatch and lost status.
    /// </summary>
    private class TrackedPacket {
        public TOutgoing Packet { get; init; } = null!;
        public long Timestamp { get; } = Stopwatch.GetTimestamp();
        public bool Lost { get; set; }
    }
}

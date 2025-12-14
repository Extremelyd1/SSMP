using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using SSMP.Logging;
using SSMP.Networking.Packet.Update;

namespace SSMP.Networking;

/// <summary>
/// UDP congestion manager to avoid flooding the network channel.
/// Only used for UDP/HolePunch transports. Steam transports bypass this entirely.
/// </summary>
/// <typeparam name="TOutgoing">The type of the outgoing packet.</typeparam>
/// <typeparam name="TPacketId">The type of the packet ID.</typeparam>
internal class CongestionManager<TOutgoing, TPacketId>
    where TOutgoing : UpdatePacket<TPacketId>, new()
    where TPacketId : Enum {
    /// <summary>
    /// Number of milliseconds between sending packets if the channel is clear.
    /// </summary>
    public const int HighSendRate = 17;

    /// <summary>
    /// Number of milliseconds between sending packet if the channel is congested.
    /// </summary>
    private const int LowSendRate = 50;

    /// <summary>
    /// The round trip time threshold after which we switch to the low send rate.
    /// </summary>
    private const int CongestionThreshold = 500;

    /// <summary>
    /// The maximum time threshold (in milliseconds) in which we need to have a good RTT before switching
    /// send rates.
    /// </summary>
    private const int MaximumSwitchThreshold = 60000;

    /// <summary>
    /// The minimum time threshold (in milliseconds) in which we need to have a good RTT before switching
    /// send rates.
    /// </summary>
    private const int MinimumSwitchThreshold = 1000;

    /// <summary>
    /// If we switch from High to Low send rates, without even spending this amount of time, we increase
    /// the switch threshold.
    /// </summary>
    private const int TimeSpentCongestionThreshold = 10000;

    /// <summary>
    /// The maximum expected round-trip time during connection. This is to ensure that we do not mark
    /// packets as lost while we are still connecting.
    /// </summary>
    private const int MaximumExpectedRttDuringConnection = 5000;

    /// <summary>
    /// The corresponding update manager from which we receive the packets that we calculate the RTT from.
    /// </summary>
    private readonly UpdateManager<TOutgoing, TPacketId> _updateManager;

    /// <summary>
    /// Dictionary containing for each sequence number the corresponding packet and stopwatch. We use this
    /// to check the RTT of sent packets and to resend packets that contain reliable data if they time out.
    /// </summary>
    private readonly ConcurrentDictionary<ushort, SentPacket<TOutgoing, TPacketId>> _sentQueue;

    /// <summary>
    /// Whether we have received our first packet from the server.
    /// </summary>
    private bool _firstPacketReceived;

    /// <summary>
    /// The current average round trip time.
    /// </summary>
    public float AverageRtt { get; private set; }

    /// <summary>
    /// The maximum expected round trip time of a packet after which it is considered lost.
    /// </summary>
    private int MaximumExpectedRtt {
        get {
            // If we haven't received the first packet yet, we use a high value as the expected RTT
            // to ensure connection is established
            if (!_firstPacketReceived) {
                return MaximumExpectedRttDuringConnection;
            }

            // Average round-trip time times 2, with a max of 1000 and a min of 200
            return System.Math.Min(
                1000,
                System.Math.Max(
                    200,
                    (int) System.Math.Ceiling(AverageRtt * 2)
                )
            );
        }
    }

    /// <summary>
    /// Whether the channel is currently congested.
    /// </summary>
    private bool _isChannelCongested;

    /// <summary>
    /// The current time for which we need to have a good RTT before switching send rates.
    /// </summary>
    private int _currentSwitchTimeThreshold;

    /// <summary>
    /// Whether we have spent the threshold in a high send rate. If so, we don't increase the
    /// switchTimeThreshold if we switch again.
    /// </summary>
    private bool _spentTimeThreshold;

    /// <summary>
    /// The stopwatch keeping track of time spent below the threshold with the average RTT.
    /// </summary>
    private readonly Stopwatch _belowThresholdStopwatch;

    /// <summary>
    /// The stopwatch keeping track of time spent in either congested or non-congested mode.
    /// </summary>
    private readonly Stopwatch _currentCongestionStopwatch;

    /// <summary>
    /// Construct the congestion manager with the given update manager.
    /// </summary>
    /// <param name="updateManager">The UDP update manager.</param>
    public CongestionManager(UpdateManager<TOutgoing, TPacketId> updateManager) {
        _updateManager = updateManager;

        _sentQueue = new ConcurrentDictionary<ushort, SentPacket<TOutgoing, TPacketId>>();

        AverageRtt = 0f;
        _currentSwitchTimeThreshold = 10000;

        _belowThresholdStopwatch = new Stopwatch();
        _currentCongestionStopwatch = new Stopwatch();
    }

    /// <summary>
    /// Callback method for when we receive a packet.
    /// Calculates RTT and adjusts send rates based on congestion.
    /// Only called for UDP/HolePunch transports.
    /// </summary>
    /// <param name="packet">The incoming packet.</param>
    /// <typeparam name="TIncoming">The type of the incoming packet.</typeparam>
    /// <typeparam name="TOtherPacketId">The type of the outgoing packet ID.</typeparam>
    public void OnReceivePackets<TIncoming, TOtherPacketId>(TIncoming packet)
        where TIncoming : UpdatePacket<TOtherPacketId>
        where TOtherPacketId : Enum {
        if (!_firstPacketReceived) {
            _firstPacketReceived = true;
        }

        // Check the congestion of the latest ack
        CheckCongestion(packet.Ack);

        // Check the congestion of all acknowledged packet in the ack field
        for (ushort i = 0; i < UpdateManager.AckSize; i++) {
            if (packet.AckField[i]) {
                var sequenceToCheck = (ushort) (packet.Ack - i - 1);
                CheckCongestion(sequenceToCheck);
            }
        }
    }

    /// <summary>
    /// Check the congestion after receiving the given sequence number that was acknowledged. We also
    /// switch send rates in this method if the average RTT is consistently high/low.
    /// </summary>
    /// <param name="sequence">The acknowledged sequence number.</param>
    private void CheckCongestion(ushort sequence) {
        if (!_sentQueue.TryRemove(sequence, out var sentPacket)) {
            return;
        }

        var rtt = sentPacket.Stopwatch.ElapsedMilliseconds;

        UpdateAverageRtt(rtt);
        AdjustSendRateIfNeeded();
    }

    /// <summary>
    /// Updates the average RTT with the new measurement using exponential moving average.
    /// </summary>
    /// <param name="rtt">The new RTT measurement in milliseconds.</param>
    private void UpdateAverageRtt(long rtt) {
        // If the average RTT is not set yet (highly unlikely that is zero), we set the average directly
        // rather than calculate a moving (inaccurate) average
        if (AverageRtt == 0) {
            AverageRtt = rtt;
            return;
        }

        var difference = rtt - AverageRtt;
        // Adjust average with 1/10th of difference
        AverageRtt += difference * 0.1f;
    }

    /// <summary>
    /// Adjusts send rate between high and low based on current average RTT and congestion state.
    /// Implements adaptive thresholds to prevent rapid switching.
    /// </summary>
    private void AdjustSendRateIfNeeded() {
        if (_isChannelCongested) {
            HandleCongestedState();
        } else {
            HandleNonCongestedState();
        }
    }

    /// <summary>
    /// Handles logic when channel is currently congested.
    /// Monitors if RTT drops below threshold long enough to switch back to high send rate.
    /// </summary>
    private void HandleCongestedState() {
        if (_belowThresholdStopwatch.IsRunning) {
            // If our average is above the threshold again, we reset the stopwatch
            if (AverageRtt > CongestionThreshold) {
                _belowThresholdStopwatch.Reset();
            }
        } else {
            // If the stopwatch wasn't running, and we are below the threshold
            // we can start the stopwatch again
            if (AverageRtt < CongestionThreshold) {
                _belowThresholdStopwatch.Start();
            }
        }

        // If the average RTT was below the threshold for a certain amount of time,
        // we can go back to high send rates
        if (_belowThresholdStopwatch.IsRunning
            && _belowThresholdStopwatch.ElapsedMilliseconds > _currentSwitchTimeThreshold) {
            SwitchToHighSendRate();
        }
    }

    /// <summary>
    /// Handles logic when channel is not congested.
    /// Monitors if RTT exceeds threshold to switch to low send rate, and adjusts switch thresholds.
    /// </summary>
    private void HandleNonCongestedState() {
        // Check whether we have spent enough time in this mode to decrease the switch threshold
        if (_currentCongestionStopwatch.ElapsedMilliseconds > TimeSpentCongestionThreshold) {
            DecreaseSwitchThreshold();
        }

        // If our average round trip time exceeds the threshold, switch to congestion values
        if (AverageRtt > CongestionThreshold) {
            SwitchToLowSendRate();
        }
    }

    /// <summary>
    /// Switches from congested to non-congested mode with high send rate.
    /// </summary>
    private void SwitchToHighSendRate() {
        Logger.Debug("Switched to non-congested send rates");

        _isChannelCongested = false;
        _updateManager.CurrentSendRate = HighSendRate;

        // Reset whether we have spent the threshold in non-congested mode
        _spentTimeThreshold = false;

        // Since we switched send rates, we restart the stopwatch again
        _currentCongestionStopwatch.Reset();
        _currentCongestionStopwatch.Start();
    }

    /// <summary>
    /// Switches from non-congested to congested mode with low send rate.
    /// Increases switch threshold if we didn't spend enough time in high send rate.
    /// </summary>
    private void SwitchToLowSendRate() {
        Logger.Debug("Switched to congested send rates");

        _isChannelCongested = true;
        _updateManager.CurrentSendRate = LowSendRate;

        // If we were too short in the High send rates before switching again, we
        // double the threshold for switching
        if (!_spentTimeThreshold) {
            IncreaseSwitchThreshold();
        }

        // Since we switched send rates, we restart the stopwatch again
        _currentCongestionStopwatch.Reset();
        _currentCongestionStopwatch.Start();
    }

    /// <summary>
    /// Decreases the switch threshold when stable time is spent in non-congested mode.
    /// Helps the system recover faster from temporary congestion.
    /// </summary>
    private void DecreaseSwitchThreshold() {
        // We spent at least the threshold in non-congestion mode
        _spentTimeThreshold = true;

        _currentCongestionStopwatch.Reset();
        _currentCongestionStopwatch.Start();

        // Cap it at a minimum
        _currentSwitchTimeThreshold = System.Math.Max(
            _currentSwitchTimeThreshold / 2,
            MinimumSwitchThreshold
        );

        Logger.Debug(
            $"Proper time spent in non-congested mode, halved switch threshold to: {_currentSwitchTimeThreshold}");

        // After we reach the minimum threshold, there's no reason to keep the stopwatch going
        if (_currentSwitchTimeThreshold == MinimumSwitchThreshold) {
            _currentCongestionStopwatch.Reset();
        }
    }

    /// <summary>
    /// Increases the switch threshold when switching too quickly between modes.
    /// Prevents rapid oscillation between send rates.
    /// </summary>
    private void IncreaseSwitchThreshold() {
        // Cap it at a maximum
        _currentSwitchTimeThreshold = System.Math.Min(
            _currentSwitchTimeThreshold * 2,
            MaximumSwitchThreshold
        );

        Logger.Debug(
            $"Too little time spent in non-congested mode, doubled switch threshold to: {_currentSwitchTimeThreshold}");
    }

    /// <summary>
    /// Callback method for when we send an update packet with the given sequence number.
    /// Tracks sent packets and resends reliable data if packets are lost.
    /// Only called for UDP/HolePunch transports.
    /// </summary>
    /// <param name="sequence">The sequence number of the sent packet.</param>
    /// <param name="updatePacket">The update packet.</param>
    public void OnSendPacket(ushort sequence, TOutgoing updatePacket) {
        // Before we add another item to our queue, check for lost packets
        CheckForLostPackets();

        // Now we add our new sequence number into the queue with a running stopwatch
        _sentQueue[sequence] = new SentPacket<TOutgoing, TPacketId> {
            Packet = updatePacket,
            Stopwatch = Stopwatch.StartNew()
        };
    }

    /// <summary>
    /// Checks all sent packets for those exceeding maximum expected RTT.
    /// Marks them as lost and resends reliable data if needed.
    /// </summary>
    private void CheckForLostPackets() {
        foreach (var sentPacket in _sentQueue.Values) {
            // If the packet was not marked as lost already and the stopwatch has elapsed the maximum expected
            // round trip time, we resend the reliable data
            if (!sentPacket.Lost && sentPacket.Stopwatch.ElapsedMilliseconds > MaximumExpectedRtt) {
                sentPacket.Lost = true;

                // Check if this packet contained information that needed to be reliable
                // and if so, resend the data by adding it to the current packet
                if (sentPacket.Packet.ContainsReliableData) {
                    _updateManager.ResendReliableData(sentPacket.Packet);
                }
            }
        }
    }
}

/// <summary>
/// Data class for a packet that was sent.
/// </summary>
/// <typeparam name="TPacket">The type of the sent packet.</typeparam>
/// <typeparam name="TPacketId">The type of the packet ID for the sent packet.</typeparam>
internal class SentPacket<TPacket, TPacketId>
    where TPacket : UpdatePacket<TPacketId>
    where TPacketId : Enum {
    /// <summary>
    /// The packet that was sent.
    /// </summary>
    public required TPacket Packet { get; init; }

    /// <summary>
    /// The stopwatch keeping track of the time it takes for the packet to get acknowledged.
    /// </summary>
    public required Stopwatch Stopwatch { get; init; }

    /// <summary>
    /// Whether the sent packet was marked as lost because it took too long to get an acknowledgement.
    /// </summary>
    public bool Lost { get; set; }
}

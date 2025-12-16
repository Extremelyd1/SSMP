using System;
using System.Diagnostics;
using SSMP.Logging;
using SSMP.Networking.Packet.Update;

namespace SSMP.Networking;

/// <summary>
/// Congestion manager that adjusts send rates based on RTT measurements.
/// Only used for UDP/HolePunch transports. Steam transports bypass this entirely.
/// Uses RttTracker for RTT measurements; reliability is handled by ReliabilityManager.
/// </summary>
/// <typeparam name="TOutgoing">The type of the outgoing packet.</typeparam>
/// <typeparam name="TPacketId">The type of the packet ID.</typeparam>
internal class CongestionManager<TOutgoing, TPacketId>
    where TOutgoing : UpdatePacket<TPacketId>, new()
    where TPacketId : Enum
{
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
    /// If we switch from High to Low send rates without spending this amount of time, we increase
    /// the switch threshold.
    /// </summary>
    private const int TimeSpentCongestionThreshold = 10000;

    /// <summary>
    /// The update manager whose send rate we adjust.
    /// </summary>
    private readonly UpdateManager<TOutgoing, TPacketId> _updateManager;

    /// <summary>
    /// The RTT tracker for RTT measurements.
    /// </summary>
    private readonly RttTracker _rttTracker;

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
    /// Construct the congestion manager with the given update manager and RTT tracker.
    /// </summary>
    /// <param name="updateManager">The update manager to adjust send rates for.</param>
    /// <param name="rttTracker">The RTT tracker for RTT measurements.</param>
    public CongestionManager(UpdateManager<TOutgoing, TPacketId> updateManager, RttTracker rttTracker)
    {
        _updateManager = updateManager;
        _rttTracker = rttTracker;

        _currentSwitchTimeThreshold = 10000;

        _belowThresholdStopwatch = new Stopwatch();
        _currentCongestionStopwatch = new Stopwatch();
    }

    /// <summary>
    /// Called when a packet is received to adjust send rates based on current RTT.
    /// </summary>
    public void OnReceivePacket()
    {
        AdjustSendRateIfNeeded();
    }

    /// <summary>
    /// Adjusts send rate between high and low based on current average RTT and congestion state.
    /// Implements adaptive thresholds to prevent rapid switching.
    /// </summary>
    private void AdjustSendRateIfNeeded()
    {
        if (_isChannelCongested)
        {
            HandleCongestedState();
        }
        else
        {
            HandleNonCongestedState();
        }
    }

    /// <summary>
    /// Handles logic when channel is currently congested.
    /// Monitors if RTT drops below threshold long enough to switch back to high send rate.
    /// </summary>
    private void HandleCongestedState()
    {
        var currentRtt = _rttTracker.AverageRtt;

        if (_belowThresholdStopwatch.IsRunning)
        {
            // If our average is above the threshold again, we reset the stopwatch
            if (currentRtt > CongestionThreshold)
            {
                _belowThresholdStopwatch.Reset();
            }
        }
        else
        {
            // If the stopwatch wasn't running, and we are below the threshold
            // we can start the stopwatch again
            if (currentRtt < CongestionThreshold)
            {
                _belowThresholdStopwatch.Start();
            }
        }

        // If the average RTT was below the threshold for a certain amount of time,
        // we can go back to high send rates
        if (_belowThresholdStopwatch.IsRunning
            && _belowThresholdStopwatch.ElapsedMilliseconds > _currentSwitchTimeThreshold)
        {
            SwitchToHighSendRate();
        }
    }

    /// <summary>
    /// Handles logic when channel is not congested.
    /// Monitors if RTT exceeds threshold to switch to low send rate, and adjusts switch thresholds.
    /// </summary>
    private void HandleNonCongestedState()
    {
        // Check whether we have spent enough time in this mode to decrease the switch threshold
        if (_currentCongestionStopwatch.ElapsedMilliseconds > TimeSpentCongestionThreshold)
        {
            DecreaseSwitchThreshold();
        }

        // If our average round trip time exceeds the threshold, switch to congestion values
        if (_rttTracker.AverageRtt > CongestionThreshold)
        {
            SwitchToLowSendRate();
        }
    }

    /// <summary>
    /// Switches from congested to non-congested mode with high send rate.
    /// </summary>
    private void SwitchToHighSendRate()
    {
        Logger.Debug("Switched to non-congested send rates");

        _isChannelCongested = false;
        _updateManager.CurrentSendRate = HighSendRate;

        // Reset whether we have spent the threshold in non-congested mode
        _spentTimeThreshold = false;

        // Since we switched send rates, we restart the stopwatch again
        _currentCongestionStopwatch.Restart();
    }

    /// <summary>
    /// Switches from non-congested to congested mode with low send rate.
    /// Increases switch threshold if we didn't spend enough time in high send rate.
    /// </summary>
    private void SwitchToLowSendRate()
    {
        Logger.Debug("Switched to congested send rates");

        _isChannelCongested = true;
        _updateManager.CurrentSendRate = LowSendRate;

        // If we were too short in the High send rates before switching again, we
        // double the threshold for switching
        if (!_spentTimeThreshold)
        {
            IncreaseSwitchThreshold();
        }

        // Since we switched send rates, we restart the stopwatch again
        _currentCongestionStopwatch.Restart();
    }

    /// <summary>
    /// Decreases the switch threshold when stable time is spent in non-congested mode.
    /// Helps the system recover faster from temporary congestion.
    /// </summary>
    private void DecreaseSwitchThreshold()
    {
        // We spent at least the threshold in non-congestion mode
        _spentTimeThreshold = true;

        _currentCongestionStopwatch.Restart();

        // Cap it at a minimum
        _currentSwitchTimeThreshold = System.Math.Max(
            _currentSwitchTimeThreshold / 2,
            MinimumSwitchThreshold
        );

        Logger.Debug(
            $"Proper time spent in non-congested mode, halved switch threshold to: {_currentSwitchTimeThreshold}");

        // After we reach the minimum threshold, there's no reason to keep the stopwatch going
        if (_currentSwitchTimeThreshold == MinimumSwitchThreshold)
        {
            _currentCongestionStopwatch.Reset();
        }
    }

    /// <summary>
    /// Increases the switch threshold when switching too quickly between modes.
    /// Prevents rapid oscillation between send rates.
    /// </summary>
    private void IncreaseSwitchThreshold()
    {
        // Cap it at a maximum
        _currentSwitchTimeThreshold = System.Math.Min(
            _currentSwitchTimeThreshold * 2,
            MaximumSwitchThreshold
        );

        Logger.Debug(
            $"Too little time spent in non-congested mode, doubled switch threshold to: {_currentSwitchTimeThreshold}");
    }
}
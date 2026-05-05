using UnityEngine;

namespace SSMP.Fsm;

/// <summary>
/// Client-side interpolation for networked entities using dead reckoning
/// with RTT-adaptive visual error correction.
/// </summary>
/// <remarks>
/// Two layered techniques bridge the gap between server tick rate and render rate:
/// <para><b>Dead reckoning</b><br/>
/// Between updates, the object is projected forward using its last known velocity.
/// Keeps motion smooth during packet loss at the cost of occasionally predicting
/// the wrong position.</para>
///
/// <para><b>Visual offset correction</b><br/>
/// When a server update corrects the logical position, the visual error is absorbed
/// into an offset that smoothly decays to zero over a short window instead of snapping.
/// The logical position is corrected instantly; only the rendered position is eased.</para>
///
/// <para><b>RTT adaptation</b><br/>
/// Scales all smoothing parameters automatically. LAN players get tight, accurate
/// parameters; poor-connection players get looser ones that prioritize smoothness
/// over precision.</para>
/// </remarks>
internal class PredictiveInterpolation : MonoBehaviour {
    #region Settings

    /// <summary>
    /// Seconds without a server update before dead reckoning is hard-stopped.
    /// Normal velocity decay handles mild packet loss long before this triggers.
    /// </summary>
    [Header("Prediction Limits")] [SerializeField]
    private float extremeLossThreshold = 1.0f;

    /// <summary>
    /// Squared distance (units²) beyond which a position correction snaps
    /// instead of smoothing. Avoids a sqrt per frame; 16 ≈ 4 m gap.
    /// </summary>
    [SerializeField] private float snapThresholdSq = 16.0f;

    /// <summary>
    /// Squared speed (units/s)² below which the object is treated as stationary.
    /// Prevents micro-jitter when velocity has nearly, but not fully, zeroed out.
    /// </summary>
    [SerializeField] private float minPredictionSpeedSq = 0.001f;

    /// <summary>
    /// Maximum extrapolation speed (units/s).
    /// Clamps velocity estimates inflated by packets arriving in rapid succession
    /// after a lag spike.
    /// </summary>
    [SerializeField] private float maxProjectedSpeed = 50.0f;

    /// <summary>
    /// Server update interval in seconds (e.g. 1/20 for 20 Hz).
    /// Governs extrapolation caps and velocity fallback timing.
    /// </summary>
    [Header("Network Timing")]
    [Tooltip("The fixed tick rate of the server, e.g. 0.05 for 20 ticks/sec.")]
    [SerializeField]
    private float serverDeltaTime = 1.0f / 20.0f;

    /// <summary>
    /// Minimum local inter-packet interval before two arrivals are flagged as
    /// bunched. Bunched packets produce unreliable velocity estimates and are
    /// skipped during the velocity update step.
    /// </summary>
    [SerializeField] private float minServerDeltaTime = 1.0f / 128.0f;

    /// <summary>
    /// Blend weight applied when merging a new instantaneous velocity sample
    /// into the running estimate (0 = ignore new, 1 = replace entirely).
    /// </summary>
    [Header("Smoothing Weights")] [SerializeField, Range(0f, 1f)]
    private float velocityBlendFactor = 0.7f;

    /// <summary>
    /// Visual offset correction time (seconds) used when RTT adaptation is off.
    /// Larger values are smoother but keep the visual lag around longer.
    /// </summary>
    [SerializeField] private float visualCorrectionTime = 0.1f;

    /// <summary>
    /// Base rate at which velocity decays once packets stop arriving.
    /// Scaled by the per-RTT <see cref="DecayMultipliers"/> tier when adaptation
    /// is enabled.
    /// </summary>
    [SerializeField] private float velocityDecayRate = 2.0f;

    /// <summary>
    /// When enabled, correction time, prediction cap, and velocity decay
    /// are scaled automatically based on measured RTT.
    /// </summary>
    [Header("RTT Adaptation")] [SerializeField]
    private bool enableRttAdaptation = true;

    /// <summary>
    /// Exponent controlling how quickly adapted parameters track RTT changes.
    /// Used as the rate in <c>1 - e^(-k·dt)</c> exponential smoothing.
    /// </summary>
    [SerializeField] private float rttSmoothingSpeed = 3.0f;

    #endregion

    #region Constants

    // Starting RTT assumption before any measurement is available.
    private const float DefaultRttMs = 75.0f;

    // Default extrapolation cap: serverDeltaTime × this multiplier.
    private const float DefaultPredictionCapMultiplier = 2.0f;

    // Guards division-by-zero in velocity and prediction calculations.
    private const float MinDeltaTime = 0.0001f;

    // Guards against a correction time of zero, which would cause instant snapping.
    private const float MinCorrectionTime = 0.0001f;

    // Squared-magnitude threshold below which a vector is treated as zero.
    private const float TinySq = 0.000001f;

    #endregion

    #region RTT Tier Definitions

    // Piecewise-linear lookup table: RttSamples is the input domain (ms),
    // and each parallel array below is a separate output range.
    // Values between tiers are linearly interpolated; values outside clamp
    // to the nearest endpoint. See InterpolateValueForRtt().
    //
    // Tiers: [0] LAN ~20ms  [1] Excellent ~50ms  [2] Good ~100ms
    //        [3] Fair ~180ms  [4] Poor ~260ms

    private static readonly float[] RttSamples = [
        20f,
        50f,
        100f,
        180f,
        260f
    ];

    // Seconds for the visual offset to decay to zero per tier.
    // Poor connections warrant longer windows because corrections are larger
    // and more frequent.
    private static readonly float[] CorrectionTimes = [
        0.05f,
        0.06f,
        0.10f,
        0.14f,
        0.20f
    ];

    // Extrapolation cap expressed as a multiplier of serverDeltaTime per tier.
    // Poor connections get a larger cap to bridge irregular update gaps without
    // stopping dead mid-motion.
    private static readonly float[] PredictionCaps = [
        1.8f,
        2.0f,
        2.5f,
        3.0f,
        3.5f
    ];

    // Multiplier on velocityDecayRate per tier.
    // LAN (18×) decays fast because missing packets are genuinely anomalous.
    // Poor (3×) decays slowly because gaps are routine and stopping looks wrong.
    private static readonly float[] DecayMultipliers = [
        18f,
        15f,
        10f,
        6f,
        3f
    ];

    #endregion

    #region State Variables

    // Most recent position confirmed by the server.
    // Dead reckoning projects forward from this using _velocity.
    private Vector3 _lastServerPosition;

    // Current predicted position, integrated each frame via dead reckoning,
    // then snapped to the authoritative position on each server update.
    // The rendered position is _logicalPosition + _visualOffset.
    private Vector3 _logicalPosition;

    // Velocity estimate (units/s) derived from successive authoritative positions.
    private Vector3 _velocity;

    // Seconds elapsed since the last server update arrived.
    private float _timeSinceLastPacket;

    // Wall-clock time of the last received packet, used to compute local
    // inter-packet intervals when the server supplies no tick-count delta.
    private float _lastPacketReceiveTime;

    // Difference between the rendered position and the current logical position.
    // Absorbs the visual jump when a server correction moves the logical position,
    // then decays to zero over correctionTime.
    private Vector3 _visualOffset;

    // Spring tracking velocity for the _visualOffset damper. Maintained across frames.
    private Vector3 _visualOffsetVelocity;

    private Transform _cachedTransform = null!;

    // Last accepted server sequence number, used to discard stale packets.
    private uint _lastServerSequenceId;

    // False until the first sequenced packet arrives; skips ordering checks
    // until the baseline sequence ID is established.
    private bool _hasSequencedPacket;

    // False until the first valid server packet is processed.
    // Prevents extrapolation from a stale spawn position before data arrives.
    private bool _isInitialized;

    // Latched true on the first valid server packet; lets OnEnable decide
    // whether to snap to the last known position or wait for fresh data.
    private bool _hasNewServerData;

    // Smoothly interpolated adaptive parameters, updated by AdaptToRTT().
    // Kept separate from raw RTT to avoid jarring jumps on sudden network changes.
    private float _adaptedCorrectionTime;
    private float _adaptedPredictionCapMultiplier;
    private float _adaptedDecayMultiplier;

    // Exponentially smoothed RTT in milliseconds.
    private float _currentRtt;

    #endregion

    private void Awake() {
        EnsureTransformCached();

        var startPos = _cachedTransform.position;

        _lastServerPosition = startPos;
        _logicalPosition = startPos;

        var now = Time.time;
        _lastPacketReceiveTime = now;

        // Pre-warm adaptive parameters so the first frames behave predictably.
        _currentRtt = DefaultRttMs;
        _adaptedCorrectionTime = InterpolateValueForRtt(_currentRtt, CorrectionTimes);
        _adaptedPredictionCapMultiplier = InterpolateValueForRtt(_currentRtt, PredictionCaps);
        _adaptedDecayMultiplier = InterpolateValueForRtt(_currentRtt, DecayMultipliers);
    }

    private void OnEnable() {
        EnsureTransformCached();

        // Re-enabled after pooling: snap back to the last known authoritative position.
        if (_isInitialized && _hasNewServerData) {
            ForceSnap(_lastServerPosition);
            return;
        }

        // First enable: hold the current transform position until the first packet.
        _velocity = Vector3.zero;
        _visualOffset = Vector3.zero;
        _visualOffsetVelocity = Vector3.zero;

        var pos = _cachedTransform.position;

        _lastServerPosition = pos;
        _logicalPosition = pos;

        _timeSinceLastPacket = 0f;
        _lastPacketReceiveTime = Time.time;
    }

    /// <summary>
    /// Advances interpolation by one frame. Call from a centralized manager
    /// to control update order across all networked entities.
    /// </summary>
    /// <param name="dt">Frame delta time in seconds.</param>
    public void ManualUpdate(float dt) {
        if (dt <= 0f) return;

        EnsureTransformCached();

        var safeServerDeltaTime = GetSafeServerDeltaTime();

        _timeSinceLastPacket += dt;

        var decayMultiplier = enableRttAdaptation ? _adaptedDecayMultiplier : 1f;
        var predictionCapMultiplier =
            enableRttAdaptation ? _adaptedPredictionCapMultiplier : DefaultPredictionCapMultiplier;
        var correctionTime = enableRttAdaptation ? _adaptedCorrectionTime : visualCorrectionTime;

        correctionTime = Mathf.Max(correctionTime, MinCorrectionTime);

        // Step 1: Velocity decay
        // Once the expected update window passes with no packet, decay velocity
        // exponentially. 1/(1+rate·dt) is a stable first-order approximation that
        // avoids overshoot at large dt.
        if (_velocity.sqrMagnitude > TinySq) {
            if (_timeSinceLastPacket > safeServerDeltaTime) {
                var decayFactor = velocityDecayRate * decayMultiplier * dt;
                var decay = 1f / (1f + decayFactor);

                _velocity *= decay;

                if (_velocity.sqrMagnitude < minPredictionSpeedSq) {
                    _velocity = Vector3.zero;
                }
            }

            // Hard stop after extreme loss; the object is better treated as
            // stationary than allowed to drift indefinitely.
            if (_timeSinceLastPacket > extremeLossThreshold) {
                _velocity = Vector3.zero;
            }
        }

        // 2. Explicit Extrapolation (Dead Reckoning)
        // Project forward from the last authoritative position. The cap prevents
        // a long packet gap from sending the object unrealistically far.
        var maxPredictionTime = safeServerDeltaTime * Mathf.Max(0f, predictionCapMultiplier);
        var clampedPredictionTime = Mathf.Min(_timeSinceLastPacket, maxPredictionTime);

        _logicalPosition = _lastServerPosition + _velocity * clampedPredictionTime;

        // 3. Visual correction
        // Ease the residual offset from the last server correction back to zero.
        SmoothOffsetToZero(ref _visualOffset, ref _visualOffsetVelocity, correctionTime, dt);

        // 4. Rendered position
        // logical (authoritative projection) + visual (residual correction error).
        _cachedTransform.position = _logicalPosition + _visualOffset;
    }

    /// <summary>
    /// Feeds a new authoritative server position into the interpolator.
    /// Corrects the logical position immediately; eases the visual position
    /// to avoid a visible jump.
    /// </summary>
    /// <param name="newPos">Server-confirmed world position.</param>
    /// <param name="snapshotsSinceLast">
    /// Server ticks elapsed since the previous update.
    /// Pass 0 to fall back to local wall-clock timing.
    /// </param>
    /// <param name="sequenceId">
    /// Monotonic sequence counter. Pass 0 to disable ordering checks.
    /// Non-zero values cause out-of-order packets to be discarded.
    /// </param>
    /// <param name="isTeleport">
    /// Skips all smoothing and snaps immediately to <paramref name="newPos"/>.
    /// </param>
    public void SetNewPosition(
        Vector3 newPos,
        int snapshotsSinceLast = 1,
        uint sequenceId = 0,
        bool isTeleport = false
    ) {
        EnsureTransformCached();

        if (!AcceptSequence(sequenceId)) {
            return;
        }

        _hasNewServerData = true;

        var now = Time.time;
        var localArrivalDelta = now - _lastPacketReceiveTime;

        if (localArrivalDelta < 0f) {
            localArrivalDelta = 0f;
        }

        _lastPacketReceiveTime = now;

        // First valid packet: velocity cannot be estimated yet, so snap.
        if (!_isInitialized) {
            _isInitialized = true;
            ForceSnap(newPos);
            return;
        }

        var safeServerDeltaTime = GetSafeServerDeltaTime();
        var hasServerTickDelta = snapshotsSinceLast > 0;

        // Server tick count is preferred over local timing; it reflects
        // simulation time rather than network jitter.
        var actualDeltaTime = hasServerTickDelta
            ? snapshotsSinceLast * safeServerDeltaTime
            : localArrivalDelta;

        if (actualDeltaTime < MinDeltaTime) {
            actualDeltaTime = safeServerDeltaTime;
        }

        // After a long gap (e.g. OS sleep), the first resumed packet would produce
        // a near-zero velocity estimate by dividing small displacement by a huge dt.
        // Cap prevents that distortion.
        actualDeltaTime = Mathf.Min(
            actualDeltaTime,
            Mathf.Max(extremeLossThreshold, safeServerDeltaTime)
        );

        // Packets delivered in a burst by the OS/network stack arrive with
        // impossibly short local intervals. Local timing is meaningless here;
        // skip the velocity update rather than computing an inflated speed.
        var isPacketBunched =
            !hasServerTickDelta &&
            localArrivalDelta > 0f &&
            localArrivalDelta < minServerDeltaTime;

        // Large prediction error means the object was visually wrong for too long;
        // smooth correction would look worse than a snap.
        var predictionErrorSq = (newPos - _logicalPosition).sqrMagnitude;

        if (isTeleport || predictionErrorSq > snapThresholdSq) {
            ForceSnap(newPos);
            return;
        }

        // Visual continuity: keep the rendered position stable across the logical
        // correction by recording the new gap as a visual offset.
        //
        //   newVisualOffset = currentRendered − newAuthoritative
        //                   = (logicalOld + visualOffsetOld) − newPos
        //
        // The smoother then decays this to zero over correctionTime.
        _visualOffset = _cachedTransform.position - newPos;

        if (_visualOffset.sqrMagnitude > snapThresholdSq) {
            ForceSnap(newPos);
            return;
        }

        // Partially reset the correction spring to reduce oscillation when
        // rapid packets alternate between under- and over-prediction.
        _visualOffsetVelocity *= 0.9f;

        // Velocity estimation
        var deltaPos = newPos - _lastServerPosition;
        var instantVelocity = deltaPos / actualDeltaTime;
        var instantSpeedSq = instantVelocity.sqrMagnitude;

        if (instantSpeedSq < minPredictionSpeedSq) {
            _velocity = Vector3.zero;
        } else if (!isPacketBunched) {
            var maxSpeedSq = maxProjectedSpeed * maxProjectedSpeed;

            if (instantSpeedSq > maxSpeedSq) {
                instantVelocity = instantVelocity.normalized * maxProjectedSpeed;
            }

            _velocity = Vector3.Lerp(_velocity, instantVelocity, velocityBlendFactor);
        }
        // Bunched packet: retain the previous estimate rather than using noisy timing.

        _lastServerPosition = newPos;
        _logicalPosition = newPos;
        _timeSinceLastPacket = 0f;
    }

    /// <summary>
    /// Forces the position to the given position.
    /// </summary>
    /// <param name="position">Vector3 containing the position to snap to.</param>
    private void ForceSnap(Vector3 position) {
        EnsureTransformCached();

        _lastServerPosition = position;
        _logicalPosition = position;

        _velocity = Vector3.zero;
        _visualOffset = Vector3.zero;
        _visualOffsetVelocity = Vector3.zero;

        _timeSinceLastPacket = 0f;
        _lastPacketReceiveTime = Time.time;

        _cachedTransform.position = position;
    }

    /// <summary>
    /// Lazily caches the transform reference. Avoids a per-frame allocation in
    /// older Unity versions and handles calls made before <c>Awake</c>.
    /// </summary>
    private void EnsureTransformCached() {
        if (_cachedTransform == null) {
            _cachedTransform = transform;
        }
    }

    /// <summary>
    /// Updates adaptive parameters for the given RTT. Prefer the overload
    /// with explicit <c>dt</c> when called outside the Unity update loop.
    /// </summary>
    /// <param name="rttMs">Round-trip time in milliseconds.</param>
    public void AdaptToRTT(float rttMs) {
        AdaptToRTT(rttMs, Time.deltaTime);
    }

    /// <summary>
    /// Updates adaptive parameters for the given RTT.
    /// </summary>
    /// <param name="rttMs">Round-trip time in milliseconds.</param>
    /// <param name="dt">Time since the last adaptation call, in seconds.</param>
    public void AdaptToRTT(float rttMs, float dt) {
        if (!enableRttAdaptation) return;

        rttMs = Mathf.Max(0f, rttMs);
        dt = Mathf.Max(0f, dt);

        // blend = 1 - e^(-k·dt): frame-rate-independent exponential smoothing.
        // A larger dt produces a proportionally larger step.
        var blend = 1f - Mathf.Exp(-rttSmoothingSpeed * dt);

        // Smooth the raw measurement first, then look up targets for the smoothed
        // value. Double-smoothing prevents sharp parameter jumps on noisy RTT spikes.
        _currentRtt = Mathf.Lerp(_currentRtt, rttMs, blend);

        var targetCorrectionTime = InterpolateValueForRtt(_currentRtt, CorrectionTimes);
        var targetPredictionCap = InterpolateValueForRtt(_currentRtt, PredictionCaps);
        var targetDecayMultiplier = InterpolateValueForRtt(_currentRtt, DecayMultipliers);

        _adaptedCorrectionTime = Mathf.Lerp(_adaptedCorrectionTime, targetCorrectionTime, blend);
        _adaptedPredictionCapMultiplier = Mathf.Lerp(_adaptedPredictionCapMultiplier, targetPredictionCap, blend);
        _adaptedDecayMultiplier = Mathf.Lerp(_adaptedDecayMultiplier, targetDecayMultiplier, blend);
    }

    /// <summary>
    /// Accepts or rejects a packet based on its sequence number.
    /// Out-of-order and duplicate packets are discarded to prevent the object's
    /// position from rolling back.
    /// </summary>
    /// <remarks>
    /// Unsigned 32-bit subtraction handles counter wraparound correctly:
    /// casting (sequenceId - _lastServerSequenceId) to int yields a positive
    /// result even when the counter wraps from 0xFFFFFFFF to 0x00000001.
    /// </remarks>
    private bool AcceptSequence(uint sequenceId) {
        // 0 is a sentinel meaning "no ordering required".
        if (sequenceId == 0) {
            return true;
        }

        if (!_hasSequencedPacket) {
            _hasSequencedPacket = true;
            _lastServerSequenceId = sequenceId;
            return true;
        }

        if ((int) (sequenceId - _lastServerSequenceId) <= 0) {
            return false;
        }

        _lastServerSequenceId = sequenceId;
        return true;
    }

    /// <summary>
    /// Drives <paramref name="offset"/> to zero using a Padé-approximated
    /// critically-damped spring.
    /// </summary>
    /// <remarks>
    /// The Padé approximation of e^(-ω·dt) keeps the integrator stable at large
    /// dt (e.g. a frame hitch), unlike a naive Euler step which overshoots and
    /// oscillates. Both <paramref name="offset"/> and <paramref name="velocity"/>
    /// are zeroed exactly once both fall below <see cref="TinySq"/>.
    /// </remarks>
    /// <param name="offset">Current offset; driven toward zero in place.</param>
    /// <param name="velocity">Spring tracking velocity; updated alongside offset.</param>
    /// <param name="smoothTime">Approximate time to reach zero, in seconds.</param>
    /// <param name="dt">Frame delta time in seconds.</param>
    private static void SmoothOffsetToZero(
        ref Vector3 offset,
        ref Vector3 velocity,
        float smoothTime,
        float dt
    ) {
        if (offset.sqrMagnitude < TinySq && velocity.sqrMagnitude < TinySq) {
            offset = Vector3.zero;
            velocity = Vector3.zero;
            return;
        }

        smoothTime = Mathf.Max(smoothTime, MinCorrectionTime);

        // ω = 2/smoothTime; x = ω·dt; exp ≈ e^(-x) via Padé rational approximation.
        var omega = 2f / smoothTime;
        var x = omega * dt;
        var exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);

        // Symplectic-style integration: temp couples the position and velocity
        // updates so both are advanced in the same step.
        var temp = (velocity + omega * offset) * dt;
        velocity = (velocity - omega * temp) * exp;
        offset = (offset + temp) * exp;

        if (!(offset.sqrMagnitude < TinySq) || !(velocity.sqrMagnitude < TinySq)) {
            return;
        }

        offset = Vector3.zero;
        velocity = Vector3.zero;
    }

    /// <summary>
    /// Linearly interpolates a value from <paramref name="tierValues"/> using
    /// <see cref="RttSamples"/> as the input domain.
    /// Values below the first sample clamp to <c>tierValues[0]</c>;
    /// values above the last sample clamp to <c>tierValues[^1]</c>.
    /// </summary>
    /// <param name="rtt">Round-trip time in milliseconds.</param>
    /// <param name="tierValues">
    /// Output values per tier. Must match <see cref="RttSamples"/> in length;
    /// returns <c>tierValues[^1]</c> on a length mismatch.
    /// </param>
    private static float InterpolateValueForRtt(float rtt, float[] tierValues) {
        switch (tierValues.Length) {
            case 0: return 0f;
            case 1: return tierValues[0];
        }

        // Misconfigured table: fall back to the most conservative tier.
        if (tierValues.Length != RttSamples.Length) {
            return tierValues[^1];
        }

        rtt = Mathf.Max(0f, rtt);

        if (rtt <= RttSamples[0]) return tierValues[0];

        for (var i = 1; i < RttSamples.Length; i++) {
            if (rtt > RttSamples[i]) {
                continue;
            }

            var t = Mathf.InverseLerp(RttSamples[i - 1], RttSamples[i], rtt);
            return Mathf.Lerp(tierValues[i - 1], tierValues[i], t);
        }

        return tierValues[^1];
    }

    /// <summary>
    /// Returns <see cref="serverDeltaTime"/> clamped to <see cref="MinDeltaTime"/>.
    /// </summary>
    private float GetSafeServerDeltaTime() => Mathf.Max(serverDeltaTime, MinDeltaTime);
}

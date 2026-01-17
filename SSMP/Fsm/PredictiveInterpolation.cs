using UnityEngine;

namespace SSMP.Fsm {
    /// <summary>
    /// Handles smooth client-side interpolation of networked entities using 
    /// explicit extrapolation with RTT-adaptive error correction.
    /// </summary>
    internal class PredictiveInterpolation : MonoBehaviour {
        #region Settings

        /// <summary>
        /// Threshold (seconds) where prediction is hard-stopped to prevent huge de-syncs.
        /// </summary>
        [Header("Prediction Limits")] [SerializeField]
        private float extremeLossThreshold = 1.0f;

        /// <summary>
        /// Squared distance threshold for valid prediction vs teleport.
        /// </summary>
        [SerializeField] private float snapThresholdSq = 16.0f;

        /// <summary>
        /// Minimum squared speed to consider moving.
        /// </summary>
        [SerializeField] private float minPredictionSpeedSq = 0.001f;

        /// <summary>
        /// Maximum allowed speed for prediction to prevent explosions on lag spikes.
        /// </summary>
        [SerializeField] private float maxProjectedSpeed = 50.0f;

        [Header("Network Timing")]
        [Tooltip("The fixed tick rate of your server (e.g. 0.05 for 20 ticks/sec).")]
        [SerializeField]
        private float serverDeltaTime = 1.0f / 20.0f;

        [SerializeField] private float minServerDeltaTime = 1.0f / 128.0f; // ~7ms

        [Header("Smoothing Weights")] [SerializeField, Range(0f, 1f)]
        private float velocityBlendFactor = 0.7f;

        [SerializeField] private float visualCorrectionTime = 0.1f;
        [SerializeField] private float velocityDecayRate = 2.0f;

        [Header("RTT Adaptation")] [SerializeField]
        private bool enableRttAdaptation = true;

        [SerializeField] private float rttSmoothingSpeed = 3.0f; // How fast to transition between RTT tiers

        #endregion

        #region RTT Tier Definitions

        // RTT thresholds (in milliseconds)
        private static readonly float[] RttThresholds = [20f, 50f, 100f, 180f];

        // Visual correction time per tier: LAN, Excellent, Good, Fair, Poor
        // Higher values = smoother but more latent visual corrections
        private static readonly float[] CorrectionTimes = [0.05f, 0.06f, 0.10f, 0.14f, 0.20f];

        // Prediction time cap multiplier per tier (multiplied by serverDeltaTime)
        // Higher values = more extrapolation allowed before clamping
        private static readonly float[] PredictionCaps = [1.8f, 2.0f, 2.5f, 3.0f, 3.5f];

        // Decay rate multiplier per tier
        // Lower values = slower decay, smoother movement but more gliding risk
        private static readonly float[] DecayMultipliers = [18f, 15f, 10f, 6f, 3f];

        #endregion

        #region State Variables

        // The last authoritative position received from the server (used for velocity calculation)
        private Vector3 _lastServerPosition;

        // The current predicted position (integrated frame-by-frame)
        private Vector3 _logicalPosition;

        private Vector3 _velocity;

        private float _timeSinceLastPacket;
        private float _lastUpdateTime;

        // Visual Offsets
        private Vector3 _visualOffset;
        private Vector3 _visualOffsetVelocity;

        private Transform _cachedTransform = null!;
        private uint _lastServerSequenceId;
        private bool _predictionDisabled;
        private bool _isInitialized;
        private bool _hasNewServerData;

        // RTT-Adaptive values (smoothly interpolated)
        private float _adaptedCorrectionTime;
        private float _adaptedPredictionCapMultiplier;
        private float _adaptedDecayMultiplier;
        private float _currentRtt;

        #endregion

        private void Awake() {
            EnsureTransformCached();
            _lastServerPosition = _cachedTransform.position;
            _logicalPosition = _cachedTransform.position;
            _lastUpdateTime = Time.time;

            // Initialize adaptive values to defaults (Good tier ~75ms)
            _adaptedCorrectionTime = visualCorrectionTime;
            _adaptedPredictionCapMultiplier = 1.5f;
            _adaptedDecayMultiplier = 15f;
            _currentRtt = 75f;
        }

        private void OnEnable() {
            // If we are initialized and have valid server data, snap to it.
            // If we don't have data, we wait for the first packet rather than snapping to an arbitrary pool position.
            if (_isInitialized && _hasNewServerData) {
                ForceSnap(_lastServerPosition);
            } else {
                // Reset smoothing to prevent jump-scares on spawn
                _visualOffsetVelocity = Vector3.zero;
                // _visualRotationVel = 0f;
            }
        }

        /// <summary>
        /// Manually update interpolation. Call this from a centralized update loop.
        /// </summary>
        /// <param name="dt">The delta time for this frame.</param>
        public void ManualUpdate(float dt) {
            if (_predictionDisabled) return;

            _timeSinceLastPacket += dt;

            // Cache adapted values once (avoid repeated ternary evaluation)
            var decayMult = _adaptedDecayMultiplier;
            var predCapMult = _adaptedPredictionCapMultiplier;
            var corrTime = _adaptedCorrectionTime;

            // 1. Handle Packet Loss / Decay
            if (_velocity.x != 0f || _velocity.y != 0f || _velocity.z != 0f) {
                if (_timeSinceLastPacket > serverDeltaTime) {
                    var decayFactor = velocityDecayRate * decayMult * dt;
                    var decay = 1f / (1f + decayFactor);

                    _velocity.x *= decay;
                    _velocity.y *= decay;
                    _velocity.z *= decay;

                    const float threshold = 0.01f;
                    if (_velocity.x is > -threshold and < threshold &&
                        _velocity.y is > -threshold and < threshold &&
                        _velocity.z is > -threshold and < threshold) {
                        _velocity = Vector3.zero;
                    }
                }

                if (_timeSinceLastPacket > extremeLossThreshold) {
                    _velocity = Vector3.zero;
                }
            }

            // 2. Explicit Extrapolation (Dead Reckoning)
            var clampedTime = _timeSinceLastPacket;
            var maxTime = serverDeltaTime * predCapMult;
            if (clampedTime > maxTime) clampedTime = maxTime;

            // Inline position calculation
            var px = _lastServerPosition.x + _velocity.x * clampedTime;
            var py = _lastServerPosition.y + _velocity.y * clampedTime;
            var pz = _lastServerPosition.z + _velocity.z * clampedTime;

            // 3. Smooth Visual Offsets - Padé Approximation for performance
            if (_visualOffset.x != 0f || _visualOffset.y != 0f || _visualOffset.z != 0f) {
                var omega = 2f / corrTime;
                var x = omega * dt;
                var exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);

                _visualOffset.x = (_visualOffset.x + _visualOffsetVelocity.x * dt) * exp;
                _visualOffset.y = (_visualOffset.y + _visualOffsetVelocity.y * dt) * exp;
                _visualOffset.z = (_visualOffset.z + _visualOffsetVelocity.z * dt) * exp;

                _visualOffsetVelocity.x = (_visualOffsetVelocity.x - omega * _visualOffset.x) * exp;
                _visualOffsetVelocity.y = (_visualOffsetVelocity.y - omega * _visualOffset.y) * exp;
                _visualOffsetVelocity.z = (_visualOffsetVelocity.z - omega * _visualOffset.z) * exp;
            }

            // 4. Apply Final State - direct position set
            _cachedTransform.position = new Vector3(
                px + _visualOffset.x,
                py + _visualOffset.y,
                pz + _visualOffset.z
            );
        }

        /// <summary>
        /// Updates the entity's position using the latest prediction state.
        /// </summary>
        public void SetNewPosition(Vector3 newPos) {
            SetNewState(newPos);
        }

        /// <summary>
        /// Updates the prediction state with a new authoritative snapshot from the server.
        /// </summary>
        /// <param name="newPos">The new authoritative position.</param>
        /// <param name="snapshotsSinceLast">Number of ticks since the last update (for velocity calc).</param>
        /// <param name="sequenceId">Optional sequence ID to discard out-of-order packets.</param>
        /// <param name="isTeleport">If true, snaps immediately without smoothing.</param>
        public void SetNewState(
            Vector3 newPos,
            int snapshotsSinceLast = 1,
            uint sequenceId = 0,
            bool isTeleport = false
        ) {
            EnsureTransformCached();

            // 1. Sequence Safety (Allow 0 for non-sequenced updates)
            if (sequenceId != 0) {
                // Unsigned arithmetic handles wrap-around automatically
                if ((int) (sequenceId - _lastServerSequenceId) <= 0) return;
                _lastServerSequenceId = sequenceId;
            }

            _hasNewServerData = true;

            // 2. Initialization Safety
            if (!_isInitialized || _predictionDisabled) {
                _isInitialized = true;
                ForceSnap(newPos);
                return;
            }

            // 3. Time Safety
            var now = Time.time;
            float actualDeltaTime;

            if (snapshotsSinceLast > 0) {
                actualDeltaTime = snapshotsSinceLast * serverDeltaTime;
            } else {
                // Fallback: calculate local time difference
                actualDeltaTime = now - _lastUpdateTime;
            }

            // Detect bunched packets (arriving too fast)
            var isPacketBunched = actualDeltaTime < minServerDeltaTime;

            // Clamp for safety in calculations
            if (actualDeltaTime < 0.0001f) actualDeltaTime = serverDeltaTime;

            _lastUpdateTime = now;

            // 4. Snap / Teleport Check
            // Compare against logical position to detect large prediction errors
            var distSq = (newPos - _logicalPosition).sqrMagnitude;
            if (isTeleport || distSq > snapThresholdSq) {
                ForceSnap(newPos);
                return;
            }

            // 5. Visual Offset Calculation (Continuity Logic)
            // Maintain visual continuity: VisualPos = LogicalPos + Offset. 
            // NewOffset = OldVisualPos - NewLogicalPos (which will be newPos)
            // This ensures the object doesn't visually jump when logical position updates.
            _visualOffset = _cachedTransform.position - newPos;

            // FIX: Dampen velocity slightly on new packet to reduce oscillation from SmoothDamp
            _visualOffsetVelocity *= 0.9f;

            // 6. Velocity Estimation
            // First, check if player has stopped (must be done even for bunched packets)
            var movementSq = (newPos - _lastServerPosition).sqrMagnitude;
            var playerStopped = movementSq < minPredictionSpeedSq;

            if (playerStopped) {
                // Player has stopped - zero velocity immediately to prevent gliding
                _velocity = Vector3.zero;
            } else if (!isPacketBunched) {
                // Only update velocity from non-bunched packets (to avoid noise)
                var instantVel = (newPos - _lastServerPosition) / actualDeltaTime;

                // Clamp velocity to prevent physics explosions on lag spikes
                if (instantVel.sqrMagnitude > maxProjectedSpeed * maxProjectedSpeed) {
                    instantVel = instantVel.normalized * maxProjectedSpeed;
                }

                // Blend toward new velocity for smooth prediction
                _velocity = Vector3.Lerp(_velocity, instantVel, velocityBlendFactor);
            }
            // If bunched and still moving, keep previous velocity estimate

            // 7. Commit State
            _lastServerPosition = newPos;
            // Snap logical position to authoritative state
            _logicalPosition = newPos;
            _timeSinceLastPacket = 0f;
        }

        public void ForceSnap(Vector3 position) {
            EnsureTransformCached();

            _lastServerPosition = position;
            _logicalPosition = position;

            _velocity = Vector3.zero;
            _visualOffset = Vector3.zero;
            _visualOffsetVelocity = Vector3.zero;

            _timeSinceLastPacket = 0f;
            _lastUpdateTime = Time.time;

            _cachedTransform.position = position;
        }

        public void SetPredictionEnabled(bool shouldEnable) {
            EnsureTransformCached();
            _predictionDisabled = !shouldEnable;
            if (_predictionDisabled) {
                ForceSnap(_cachedTransform.position);
            }
        }

        // Helper to prevent NullRef if called before Awake
        private void EnsureTransformCached() {
            if (_cachedTransform == null) _cachedTransform = transform;
        }

        /// <summary>
        /// Adapts interpolation parameters based on measured RTT.
        /// Call this whenever you measure/update the client's RTT.
        /// </summary>
        /// <param name="rttMs">Round-trip time in milliseconds</param>
        public void AdaptToRTT(float rttMs) {
            if (!enableRttAdaptation) return;

            // Smooth the RTT transition to prevent jarring changes
            _currentRtt = Mathf.Lerp(_currentRtt, rttMs, Time.deltaTime * rttSmoothingSpeed);

            // Calculate target values based on current RTT
            var targetCorrectionTime = InterpolateValueForRtt(_currentRtt, CorrectionTimes);
            var targetPredictionCap = InterpolateValueForRtt(_currentRtt, PredictionCaps);
            var targetDecayMult = InterpolateValueForRtt(_currentRtt, DecayMultipliers);

            // Smoothly transition adapted values
            var smoothFactor = Time.deltaTime * rttSmoothingSpeed;
            _adaptedCorrectionTime = Mathf.Lerp(_adaptedCorrectionTime, targetCorrectionTime, smoothFactor);
            _adaptedPredictionCapMultiplier = Mathf.Lerp(
                _adaptedPredictionCapMultiplier, targetPredictionCap, smoothFactor
            );
            _adaptedDecayMultiplier = Mathf.Lerp(_adaptedDecayMultiplier, targetDecayMult, smoothFactor);
        }

        /// <summary>
        /// Interpolates a value from the tier arrays based on RTT.
        /// </summary>
        private static float InterpolateValueForRtt(float rtt, float[] tierValues) {
            // Below minimum threshold - use LAN tier
            if (rtt <= RttThresholds[0]) {
                return tierValues[0];
            }

            // Find which tier range we're in and interpolate
            for (var i = 0; i < RttThresholds.Length; i++) {
                if (rtt <= RttThresholds[i]) {
                    var prevThreshold = (i == 0) ? 0f : RttThresholds[i - 1];
                    var t = Mathf.InverseLerp(prevThreshold, RttThresholds[i], rtt);
                    return Mathf.Lerp(tierValues[i], tierValues[i + 1], t);
                }
            }

            // Above maximum threshold - use Poor tier
            return tierValues[^1];
        }
    }
}

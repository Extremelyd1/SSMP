/*
using UnityEngine;

namespace SSMP.Fsm;

/// <summary>
/// MonoBehaviour for interpolating position between position updates.
/// </summary>
internal class PositionInterpolation : MonoBehaviour {
    /// <summary>
    /// The approximate time it takes to reach the target.
    /// 0.1s provides a smooth buffer without feeling too floaty.
    /// </summary>
    private const float SmoothTime = 0.1f;

    /// <summary>
    /// Threshold for considering position "reached" to avoid unnecessary updates.
    /// </summary>
    private const float PositionThreshold = 0.0001f;

    /// <summary>
    /// Cached squared threshold for faster distance checks.
    /// </summary>
    private const float PositionThresholdSq = PositionThreshold * PositionThreshold;

    /// <summary>
    /// The current velocity, used by SmoothDamp.
    /// </summary>
    private Vector3 _currentVelocity;

    /// <summary>
    /// The target position to reach.
    /// </summary>
    private Vector3 _targetPosition;

    /// <summary>
    /// Cached transform reference to avoid repeated GetComponent calls.
    /// </summary>
    private Transform _transform = null!;

    /// <summary>
    /// Cached local position to avoid redundant property access.
    /// </summary>
    private Vector3 _cachedPosition;

    /// <summary>
    /// Flag to track if we've reached the target position.
    /// </summary>
    private bool _hasReachedTarget;

    /// <summary>
    /// Cached Time.deltaTime to avoid property access overhead.
    /// </summary>
    private float _deltaTime;

    public void Awake() {
        // Cache transform reference once instead of accessing it every frame
        _transform = transform;
    }

    public void Start() {
        _cachedPosition = _transform.localPosition;
        _targetPosition = _cachedPosition;
        _hasReachedTarget = true;
    }

    /// <summary>
    /// Update loop for smooth movement.
    /// </summary>
    public void Update() {
        // Skip interpolation if we've already reached the target
        if (_hasReachedTarget) {
            return;
        }

        // Cache deltaTime once per frame
        _deltaTime = Time.deltaTime;

        // Use cached position to avoid property getter overhead
        _cachedPosition = _transform.localPosition;
        
        // Smoothly move towards the target position independent of network update rate
        _cachedPosition = Vector3.SmoothDamp(
            _cachedPosition,
            _targetPosition,
            ref _currentVelocity,
            SmoothTime,
            Mathf.Infinity,
            _deltaTime
        );

        // Apply the new position
        _transform.localPosition = _cachedPosition;

        // Check if we've reached the target using squared distance (faster than magnitude)
        var sqrDistanceToTarget = (_cachedPosition.x - _targetPosition.x) * (_cachedPosition.x - _targetPosition.x) +
                                     (_cachedPosition.y - _targetPosition.y) * (_cachedPosition.y - _targetPosition.y) +
                                     (_cachedPosition.z - _targetPosition.z) * (_cachedPosition.z - _targetPosition.z);
        
        if (sqrDistanceToTarget < PositionThresholdSq) {
            // Snap to target when close enough
            _transform.localPosition = _targetPosition;
            _cachedPosition = _targetPosition;
            _hasReachedTarget = true;
            _currentVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Set the new position to interpolate to.
    /// </summary>
    /// <param name="newPosition">The new position as Vector3.</param>
    public void SetNewPosition(Vector3 newPosition) {
        // Manual squared distance calculation (faster than vector subtraction + sqrMagnitude)
        var sqrDistance = (newPosition.x - _targetPosition.x) * (newPosition.x - _targetPosition.x) +
                           (newPosition.y - _targetPosition.y) * (newPosition.y - _targetPosition.y) +
                           (newPosition.z - _targetPosition.z) * (newPosition.z - _targetPosition.z);
        
        // Only update if the new position is actually different
        if (sqrDistance > PositionThresholdSq) {
            _targetPosition = newPosition;
            _hasReachedTarget = false;
        }
    }
}*/

using System.Collections.Concurrent;
using System.Net;

namespace MMS.Services;

/// <summary>
/// Stores UDP-discovered external endpoints and pending client join tokens.
/// Replaces Google STUN with a self-hosted endpoint discovery mechanism.
/// </summary>
public sealed class DiscoveryService {
    /// <summary>How long a recorded endpoint or pending join remains valid.</summary>
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromSeconds(60);

    /// <summary>How often <see cref="WaitForDiscoveryAsync"/> polls the cache.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    // Stores discovered endpoints. Expiry is a monotonic timestamp (ticks).
    private readonly ConcurrentDictionary<Guid, (IPEndPoint Endpoint, long ExpiryTicks)> _cache = new();

    // Maps clientToken → (hostToken, registrationTicks) for pending UDP joins.
    private readonly ConcurrentDictionary<Guid, (string HostToken, long RegisteredTicks)> _pendingJoins = new();

    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initialises a new <see cref="DiscoveryService"/>.
    /// </summary>
    /// <param name="timeProvider">
    /// Abstraction over time. Pass <see cref="TimeProvider.System"/> in production
    /// or a fake in tests. Defaults to <see cref="TimeProvider.System"/> if omitted.
    /// </param>
    public DiscoveryService(TimeProvider? timeProvider = null) {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Records a discovered external endpoint for the given token.
    /// Expires after <see cref="EntryLifetime"/>.
    /// </summary>
    public void Record(Guid token, IPEndPoint endpoint) {
        var expiryTicks = GetExpiryTicks(EntryLifetime);
        _cache[token] = (endpoint, expiryTicks);
    }

    /// <summary>
    /// Returns the recorded endpoint for a token, or <see langword="null"/> if not found or expired.
    /// Expired entries are evicted on access.
    /// </summary>
    private IPEndPoint? TryGet(Guid token) {
        if (!_cache.TryGetValue(token, out var entry))
            return null;

        if (!IsExpired(entry.ExpiryTicks))
            return entry.Endpoint;

        _cache.TryRemove(token, out _);
        return null;
    }

    /// <summary>
    /// Polls for a discovered endpoint until it appears or the timeout elapses.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="PeriodicTimer"/> rather than <c>Task.Delay</c> in a loop —
    /// it does not re-queue the timer on each tick and has clean cancellation semantics.
    /// The cache is checked once immediately before the first timer tick to avoid a
    /// <see cref="PollInterval"/> delay when the packet has already arrived.
    /// </remarks>
    /// <param name="token">Token to wait for.</param>
    /// <param name="timeout">Maximum time to wait before returning <see langword="null"/>.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// The discovered <see cref="IPEndPoint"/> on success;
    /// <see langword="null"/> if the timeout elapses or <paramref name="ct"/> is cancelled.
    /// </returns>
    public async Task<IPEndPoint?> WaitForDiscoveryAsync(
        Guid token,
        TimeSpan timeout,
        CancellationToken ct = default
    ) {
        // Check immediately before entering the polling loop.
        var ep = TryGet(token);
        if (ep is not null)
            return ep;

        using var timer = new PeriodicTimer(PollInterval);
        var deadlineTicks = GetExpiryTicks(timeout);

        while (!IsExpired(deadlineTicks) && await timer.WaitForNextTickAsync(ct)) {
            ep = TryGet(token);
            if (ep is not null)
                return ep;
        }

        return null;
    }

    /// <summary>
    /// Registers a client token waiting for UDP discovery.
    /// Maps <paramref name="clientToken"/> → <paramref name="hostToken"/> so the UDP
    /// listener can find and notify the correct lobby host.
    /// </summary>
    public void RegisterPendingJoin(Guid clientToken, string hostToken) {
        _pendingJoins[clientToken] = (hostToken, _timeProvider.GetTimestamp());
    }

    /// <summary>
    /// Atomically removes and returns the host token associated with a pending join.
    /// Returns <see langword="null"/> if the token is not registered.
    /// </summary>
    public string? TryConsumePendingJoin(Guid clientToken)
        => _pendingJoins.TryRemove(clientToken, out var entry) ? entry.HostToken : null;

    /// <summary>
    /// Removes expired endpoint cache entries and stale pending joins.
    /// Called periodically by <see cref="LobbyCleanupService"/>.
    /// </summary>
    internal void Cleanup() {
        var now = _timeProvider.GetTimestamp();

        foreach (var (key, value) in _cache) {
            if (now > value.ExpiryTicks)
                _cache.TryRemove(key, out _);
        }

        var pendingJoinCutoff = now - DurationToTicks(EntryLifetime);
        foreach (var (key, value) in _pendingJoins) {
            if (value.RegisteredTicks < pendingJoinCutoff)
                _pendingJoins.TryRemove(key, out _);
        }
    }

    /// <summary>Converts a <see cref="TimeSpan"/> to a tick duration using the provider's frequency.</summary>
    private long DurationToTicks(TimeSpan duration)
        => (long)(_timeProvider.TimestampFrequency * duration.TotalSeconds);

    /// <summary>Returns a future monotonic tick count at <paramref name="lifetime"/> from now.</summary>
    private long GetExpiryTicks(TimeSpan lifetime)
        => _timeProvider.GetTimestamp() + DurationToTicks(lifetime);

    /// <summary>Returns <see langword="true"/> if the current timestamp is past <paramref name="expiryTicks"/>.</summary>
    private bool IsExpired(long expiryTicks)
        => _timeProvider.GetTimestamp() > expiryTicks;
}

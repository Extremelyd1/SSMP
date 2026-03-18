using System.Collections.Concurrent;
using MMS.Models.Matchmaking;

namespace MMS.Services.Matchmaking;

/// <summary>
/// Thread-safe in-memory store for active join sessions and discovery tokens.
/// Individual operations are atomic; callers own higher-level consistency
/// (e.g. removing a session and its token together).
/// </summary>
public sealed class JoinSessionStore {
    /// <summary>
    /// Thread-safe dictionary of active join sessions keyed by <see cref="JoinSession.JoinId"/>.
    /// </summary>
    private readonly ConcurrentDictionary<string, JoinSession> _joinSessions = new();

    /// <summary>
    /// Metadata for active discovery tokens, used for correlating UDP discovery packets.
    /// </summary>
    private readonly ConcurrentDictionary<string, DiscoveryTokenMetadata> _discoveryMetadata = new();

    /// <summary>
    /// Secondary index for efficient lookup of join sessions by lobby connection data.
    /// Valus are dummy bytes to use <see cref="ConcurrentDictionary{TKey, TValue}"/> as a set.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _joinIdsByLobby = new();

    /// <summary>
    /// Secondary index for efficient lookup of expired sessions.
    /// </summary>
    private readonly SortedSet<(DateTime expiresAtUtc, string joinId)> _expiryIndex = new();

    /// <summary>
    /// Lock for thread-safe access to <see cref="_expiryIndex"/>.
    /// </summary>
    private readonly Lock _indexLock = new();

    /// <summary>Adds or replaces the session keyed by <see cref="JoinSession.JoinId"/>.</summary>
    public void Add(JoinSession session) {
        if (_joinSessions.TryGetValue(session.JoinId, out var previous))
            RemoveIndexes(previous);

        _joinSessions[session.JoinId] = session;
        AddIndexes(session);
    }

    /// <summary>Attempts to retrieve a session by its join identifier.</summary>
    public bool TryGet(string joinId, out JoinSession? session) =>
        _joinSessions.TryGetValue(joinId, out session);

    /// <summary>Removes a session and returns it. Returns <see langword="false"/> if not found.</summary>
    public bool Remove(string joinId, out JoinSession? session) {
        if (!_joinSessions.TryRemove(joinId, out session))
            return false;

        RemoveIndexes(session);

        return true;
    }

    /// <summary>Returns the join identifiers for all sessions belonging to the given lobby.</summary>
    public IReadOnlyList<string> GetJoinIdsForLobby(string lobbyConnectionData) =>
        _joinIdsByLobby.TryGetValue(lobbyConnectionData, out var joinIds)
            ? joinIds.Keys.ToList()
            : [];

    /// <summary>Returns the join identifiers of all sessions expired before <paramref name="nowUtc"/>.</summary>
    public IReadOnlyList<string> GetExpiredJoinIds(DateTime nowUtc) {
        var expiredJoinIds = new List<string>();

        lock (_indexLock) {
            expiredJoinIds.AddRange(
                _expiryIndex
                    .TakeWhile(entry => entry.expiresAtUtc < nowUtc)
                    .Select(entry => entry.joinId)
            );
        }

        return expiredJoinIds;
    }

    /// <summary>Inserts or replaces the metadata associated with a discovery token.</summary>
    public void UpsertDiscoveryToken(string token, DiscoveryTokenMetadata metadata) =>
        _discoveryMetadata[token] = CloneMetadata(metadata);

    /// <summary>Returns <see langword="true"/> if the discovery token is currently registered.</summary>
    public bool ContainsDiscoveryToken(string token) => _discoveryMetadata.ContainsKey(token);

    /// <summary>Attempts to retrieve the metadata for a discovery token.</summary>
    public bool TryGetDiscoveryMetadata(string token, out DiscoveryTokenMetadata? metadata) {
        if (!_discoveryMetadata.TryGetValue(token, out var stored)) {
            metadata = null;
            return false;
        }

        metadata = CloneMetadata(stored);
        return true;
    }

    /// <summary>
    /// Returns the discovered port for a token, or <see langword="null"/> if the token
    /// is unknown or its port has not yet been recorded.
    /// </summary>
    public int? GetDiscoveredPort(string token) =>
        _discoveryMetadata.TryGetValue(token, out var metadata) ? metadata.DiscoveredPort : null;

    /// <summary>Removes a discovery token and its metadata.</summary>
    public void RemoveDiscoveryToken(string token) =>
        _discoveryMetadata.TryRemove(token, out _);

    /// <summary>Updates only the discovered port for an existing discovery token.</summary>
    public bool TrySetDiscoveredPort(string token, int port) {
        while (_discoveryMetadata.TryGetValue(token, out var metadata)) {
            var updated = CloneMetadata(metadata);
            updated.DiscoveredPort = port;

            if (_discoveryMetadata.TryUpdate(token, updated, metadata))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns tokens created before <paramref name="cutoffUtc"/>.
    /// Used during periodic cleanup to evict stale tokens no longer tied to an active session.
    /// </summary>
    public IReadOnlyList<string> GetExpiredDiscoveryTokens(DateTime cutoffUtc) =>
        _discoveryMetadata.Where(kvp => kvp.Value.CreatedAt < cutoffUtc)
                          .Select(kvp => kvp.Key)
                          .ToList();

    /// <summary>
    /// Adds the session to the lobby and expiry indexes.
    /// </summary>
    /// <param name="session">The session to index.</param>
    private void AddIndexes(JoinSession session) {
        var lobbyJoinIds = _joinIdsByLobby.GetOrAdd(
            session.LobbyConnectionData, _ => new ConcurrentDictionary<string, byte>()
        );
        lobbyJoinIds[session.JoinId] = 0;

        lock (_indexLock) {
            _expiryIndex.Add((session.ExpiresAtUtc, session.JoinId));
        }
    }

    /// <summary>
    /// Removes the session from the lobby and expiry indexes.
    /// </summary>
    /// <param name="session">The session to de-index.</param>
    private void RemoveIndexes(JoinSession session) {
        if (_joinIdsByLobby.TryGetValue(session.LobbyConnectionData, out var lobbyJoinIds))
            lobbyJoinIds.TryRemove(session.JoinId, out _);

        lock (_indexLock) {
            _expiryIndex.Remove((session.ExpiresAtUtc, session.JoinId));
        }
    }

    /// <summary>
    /// Creates a deep copy of the given discovery metadata.
    /// </summary>
    /// <param name="metadata">The metadata to clone.</param>
    /// <returns>A new <see cref="DiscoveryTokenMetadata"/> instance with identical values.</returns>
    private static DiscoveryTokenMetadata CloneMetadata(DiscoveryTokenMetadata metadata) =>
        new() {
            JoinId = metadata.JoinId,
            HostConnectionData = metadata.HostConnectionData,
            DiscoveredPort = metadata.DiscoveredPort,
            CreatedAt = metadata.CreatedAt
        };
}

using System.Collections.Concurrent;
using MMS.Bootstrap;
using MMS.Models.Lobby;
using MMS.Services.Matchmaking;
using MMS.Services.Utility;

namespace MMS.Services.Lobbies;

/// <summary>
/// Thread-safe in-memory lobby store.
/// Handles lobby creation, lookup, heartbeat, and expiry sweeps.
/// NAT hole-punch coordination and join session management are delegated to
/// <see cref="JoinSessionService"/>.
/// </summary>
public class LobbyService {
    /// <summary>
    /// The backing store for all active lobbies, keyed by their unique connection data.
    /// </summary>
    private readonly ConcurrentDictionary<string, Lobby> _lobbies = new();

    /// <summary>
    /// Map for looking up lobbies by their host authentication token.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _tokenToConnectionData = new();

    /// <summary>
    /// Map for looking up lobbies by their short player-facing join code.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _codeToConnectionData = new();

    /// <summary>
    /// Thread synchronization primitive for atomic lobby creation and cleanup.
    /// </summary>
    private readonly Lock _createLobbyLock = new();

    /// <summary>
    /// Service used to generate random human-readable names for new lobbies.
    /// </summary>
    private readonly LobbyNameService _lobbyNameService;

    /// <summary>
    /// Initializes a new instance of the <see cref="LobbyService"/> class.
    /// </summary>
    /// <param name="lobbyNameService">Service used to generate random human-readable names for new lobbies.</param>
    public LobbyService(LobbyNameService lobbyNameService) {
        _lobbyNameService = lobbyNameService;
    }

    /// <summary>
    /// Creates and stores a new lobby.
    /// </summary>
    /// <param name="connectionData">
    /// The unique connection identifier for this lobby (e.g. <c>ip:port</c> for matchmaking,
    /// Steam lobby ID for Steam lobbies).
    /// </param>
    /// <param name="lobbyName">Human-readable display name assigned to this lobby.</param>
    /// <param name="lobbyType">
    /// Lobby transport type. Accepted values: <c>"matchmaking"</c> (default), <c>"steam"</c>.
    /// </param>
    /// <param name="hostLanIp">Optional LAN address of the host, used for same-network fast-path.</param>
    /// <param name="isPublic">Whether the lobby appears in the public browser.</param>
    /// <returns>The newly created <see cref="Lobby"/> instance.</returns>
    public Lobby CreateLobby(
        string connectionData,
        string lobbyName,
        string lobbyType = "matchmaking",
        string? hostLanIp = null,
        bool isPublic = true
    ) {
        if (!IsMatchmakingLobbyType(lobbyType) && !IsSteamLobby(lobbyType))
            throw new ArgumentOutOfRangeException(
                nameof(lobbyType), lobbyType, "Lobby type must be 'matchmaking' or 'steam'."
            );

        var hostToken = TokenGenerator.GenerateToken(32);
        var hostDiscoveryToken = IsMatchmakingLobbyType(lobbyType) ? TokenGenerator.GenerateToken(32) : null;
        lock (_createLobbyLock) {
            if (_lobbies.TryGetValue(connectionData, out var existingLobby)) {
                RemoveLobbyIndexes(existingLobby);
                if (!string.IsNullOrEmpty(existingLobby.LobbyName))
                    _lobbyNameService.FreeLobbyName(existingLobby.LobbyName);
            }

            var lobbyCode = IsSteamLobby(lobbyType)
                ? ""
                : ReserveLobbyCode(connectionData);

            var lobby = new Lobby(
                connectionData,
                hostToken,
                lobbyCode,
                lobbyName,
                lobbyType,
                hostLanIp,
                isPublic,
                hostDiscoveryToken
            );

            _lobbies[connectionData] = lobby;
            _tokenToConnectionData[hostToken] = connectionData;
            return lobby;
        }
    }

    /// <summary>
    /// Returns the lobby for <paramref name="connectionData"/>, or <see langword="null"/> if absent or expired.
    /// Expired lobbies are removed lazily on access.
    /// </summary>
    /// <param name="connectionData">The connection identifier the lobby was registered under.</param>
    public Lobby? GetLobby(string connectionData) {
        if (!_lobbies.TryGetValue(connectionData, out var lobby)) return null;
        if (!lobby.IsDead) return lobby;

        RemoveLobby(lobby);
        return null;
    }

    /// <summary>
    /// Returns the lobby owned by <paramref name="token"/>, or <see langword="null"/> if absent or expired.
    /// </summary>
    /// <param name="token">The host authentication token issued at lobby creation.</param>
    public Lobby? GetLobbyByToken(string token) {
        if (!_tokenToConnectionData.TryGetValue(token, out var connData))
            return null;

        var lobby = GetLobby(connData);
        return lobby is not null && lobby.HostToken == token ? lobby : null;
    }

    /// <summary>
    /// Returns the lobby identified by <paramref name="code"/>, or <see langword="null"/> if absent or expired.
    /// The lookup is case-insensitive.
    /// </summary>
    /// <param name="code">The player-facing lobby code (e.g. <c>ABC123</c>).</param>
    public Lobby? GetLobbyByCode(string code) {
        var normalizedCode = code.ToUpperInvariant();
        if (!_codeToConnectionData.TryGetValue(normalizedCode, out var connData)) return null;

        var lobby = GetLobby(connData);
        return (lobby is not null && string.Equals(lobby.LobbyCode, normalizedCode, StringComparison.Ordinal))
            ? lobby
            : null;
    }

    /// <summary>
    /// Returns all active public lobbies, optionally filtered by <paramref name="lobbyType"/>.
    /// </summary>
    /// <param name="lobbyType">
    /// Optional case-insensitive filter (e.g. <c>"matchmaking"</c> or <c>"steam"</c>).
    /// Pass <see langword="null"/> to return all types.
    /// </param>
    public IEnumerable<Lobby> GetLobbies(string? lobbyType = null) {
        var active = _lobbies.Values.Where(l => l is { IsDead: false, IsPublic: true });
        return string.IsNullOrEmpty(lobbyType)
            ? active
            : active.Where(l => l.LobbyType.Equals(lobbyType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Records a heartbeat for the lobby owned by <paramref name="token"/> and updates the
    /// connected-player count.
    /// </summary>
    /// <remarks>
    /// When <paramref name="connectedPlayers"/> drops to zero on a matchmaking lobby,
    /// <see cref="Lobby.ExternalPort"/> is cleared so that stale NAT mappings are not
    /// reused for subsequent joins.
    /// </remarks>
    /// <param name="token">The host authentication token.</param>
    /// <param name="connectedPlayers">Current number of remote players connected to the host.</param>
    /// <returns><see langword="true"/> if the lobby was found and updated; <see langword="false"/> otherwise.</returns>
    public bool Heartbeat(string token, int connectedPlayers) {
        if (connectedPlayers < 0)
            return false;

        var lobby = GetLobbyByToken(token);
        if (lobby == null) return false;

        lobby.LastHeartbeat = DateTime.UtcNow;

        if (IsMatchmakingLobby(lobby) && connectedPlayers == 0)
            lobby.ExternalPort = null;

        return true;
    }

    /// <summary>
    /// Removes the lobby owned by <paramref name="token"/> from all indexes.
    /// </summary>
    /// <param name="token">The host authentication token issued at lobby creation.</param>
    /// <param name="onRemoving">Optional callback invoked with the lobby instance before it is removed from all indexes.</param>
    /// <returns><see langword="true"/> if the lobby was found and removed; <see langword="false"/> otherwise.</returns>
    public bool RemoveLobbyByToken(string token, Action<Lobby>? onRemoving = null) {
        var lobby = GetLobbyByToken(token);
        return lobby != null && RemoveLobby(lobby, onRemoving);
    }

    /// <summary>
    /// Removes all lobbies whose <see cref="Lobby.IsDead"/> flag is set.
    /// </summary>
    /// <param name="onRemoving">Optional callback invoked with each lobby instance before it is removed.</param>
    /// <returns>The number of lobbies removed.</returns>
    public int CleanupDeadLobbies(Action<Lobby>? onRemoving = null) {
        var dead = _lobbies.Values.Where(l => l.IsDead).ToList();
        return dead.Count(lobby => RemoveLobby(lobby, onRemoving));
    }

    /// <summary>
    /// Removes a lobby from all indexes and releases its name back to <see cref="LobbyNameService"/>.
    /// </summary>
    /// <param name="lobby">The specific lobby instance to remove.</param>
    /// <param name="onRemoving">Optional callback invoked with the lobby instance before it is removed from all indexes.</param>
    /// <returns><see langword="false"/> if the lobby was not found (already removed).</returns>
    private bool RemoveLobby(Lobby lobby, Action<Lobby>? onRemoving = null) {
        if (!_lobbies.TryRemove(new KeyValuePair<string, Lobby>(lobby.ConnectionData, lobby)))
            return false;

        try {
            onRemoving?.Invoke(lobby);
        } catch (Exception ex) {
            ProgramState.Logger.LogWarning(
                ex, "Lobby removal callback failed for {ConnectionData}", PrivacyFormatter.Format(lobby.ConnectionData)
            );
        } finally {
            _tokenToConnectionData.TryRemove(lobby.HostToken, out _);
            _codeToConnectionData.TryRemove(lobby.LobbyCode, out _);
            _lobbyNameService.FreeLobbyName(lobby.LobbyName);
        }

        return true;
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="lobbyType"/> is <c>"steam"</c>.</summary>
    private static bool IsSteamLobby(string lobbyType) =>
        lobbyType.Equals("steam", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reserves a unique lobby code and associates it with the given connection data.
    /// Retries until a code is successfully inserted into the concurrent store.
    /// </summary>
    /// <param name="connectionData">The connection data to associate with the reserved code.</param>
    /// <returns>The reserved lobby code.</returns>
    private string ReserveLobbyCode(string connectionData) {
        while (true) {
            var code = TokenGenerator.GenerateUniqueLobbyCode(new HashSet<string>(_codeToConnectionData.Keys));
            if (_codeToConnectionData.TryAdd(code, connectionData))
                return code;
        }
    }

    /// <summary>
    /// Removes all index entries associated with a lobby, including its host token
    /// and lobby code if one was assigned.
    /// </summary>
    /// <param name="lobby">The lobby whose indexes should be removed.</param>
    private void RemoveLobbyIndexes(Lobby lobby) {
        _tokenToConnectionData.TryRemove(lobby.HostToken, out _);
        if (!string.IsNullOrEmpty(lobby.LobbyCode))
            _codeToConnectionData.TryRemove(lobby.LobbyCode, out _);
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="lobbyType"/> is <c>"matchmaking"</c>.</summary>
    private static bool IsMatchmakingLobbyType(string lobbyType) =>
        lobbyType.Equals("matchmaking", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns <see langword="true"/> if <paramref name="lobby"/> is a matchmaking lobby.</summary>
    private static bool IsMatchmakingLobby(Lobby lobby) =>
        IsMatchmakingLobbyType(lobby.LobbyType);
}

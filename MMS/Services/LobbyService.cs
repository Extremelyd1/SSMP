using System.Collections.Concurrent;
using MMS.Models;

namespace MMS.Services;

/// <summary>
/// Thread-safe in-memory lobby storage with heartbeat-based expiration.
/// Lobbies are keyed by ConnectionData (Steam ID or IP:Port).
/// </summary>
public class LobbyService {
    private readonly ConcurrentDictionary<string, Lobby> _lobbies = new();
    private readonly ConcurrentDictionary<string, string> _tokenToConnectionData = new();
    private readonly ConcurrentDictionary<string, string> _codeToConnectionData = new();
    private static readonly Random Random = new();

    // Token chars for host authentication (all alphanumeric lowercase)
    private const string TokenChars = "abcdefghijklmnopqrstuvwxyz0123456789";

    // Lobby code chars - uppercase alphanumeric
    private const string LobbyCodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int LobbyCodeLength = 6;

    /// <summary>
    /// Creates a new lobby keyed by ConnectionData.
    /// </summary>
    public Lobby CreateLobby(
        string connectionData,
        string lobbyName,
        string lobbyType = "matchmaking",
        string? hostLanIp = null
    ) {
        var hostToken = GenerateToken(32);
        var lobbyCode = GenerateLobbyCode();
        var lobby = new Lobby(connectionData, hostToken, lobbyCode, lobbyName, lobbyType, hostLanIp);

        _lobbies[connectionData] = lobby;
        _tokenToConnectionData[hostToken] = connectionData;
        _codeToConnectionData[lobbyCode] = connectionData;

        return lobby;
    }

    /// <summary>
    /// Gets lobby by ConnectionData. Returns null if not found or expired.
    /// </summary>
    public Lobby? GetLobby(string connectionData) {
        if (!_lobbies.TryGetValue(connectionData, out var lobby)) return null;
        if (!lobby.IsDead) return lobby;

        RemoveLobby(connectionData);
        return null;
    }

    /// <summary>
    /// Gets lobby by host token. Returns null if not found or expired.
    /// </summary>
    public Lobby? GetLobbyByToken(string token) {
        return _tokenToConnectionData.TryGetValue(token, out var connData) ? GetLobby(connData) : null;
    }

    /// <summary>
    /// Gets lobby by lobby code. Returns null if not found or expired.
    /// </summary>
    public Lobby? GetLobbyByCode(string code) {
        // Normalize to uppercase for case-insensitive matching
        var normalizedCode = code.ToUpperInvariant();
        return _codeToConnectionData.TryGetValue(normalizedCode, out var connData) ? GetLobby(connData) : null;
    }

    /// <summary>
    /// Refreshes lobby heartbeat. Returns false if lobby not found.
    /// </summary>
    public bool Heartbeat(string token) {
        var lobby = GetLobbyByToken(token);
        if (lobby == null) return false;

        lobby.LastHeartbeat = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Removes lobby by host token. Returns false if not found.
    /// </summary>
    public bool RemoveLobbyByToken(string token) {
        var lobby = GetLobbyByToken(token);
        return lobby != null && RemoveLobby(lobby.ConnectionData);
    }

    /// <summary>
    /// Returns all active (non-expired) lobbies.
    /// </summary>
    public IEnumerable<Lobby> GetAllLobbies() => _lobbies.Values.Where(l => !l.IsDead);

    /// <summary>
    /// Returns active lobbies, optionally filtered by type ("steam" or "matchmaking").
    /// </summary>
    public IEnumerable<Lobby> GetLobbies(string? lobbyType = null) {
        var lobbies = _lobbies.Values.Where(l => !l.IsDead);
        return string.IsNullOrEmpty(lobbyType)
            ? lobbies
            : lobbies.Where(l => l.LobbyType.Equals(lobbyType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Removes all expired lobbies. Returns count removed.
    /// </summary>
    public int CleanupDeadLobbies() {
        var dead = _lobbies.Values.Where(l => l.IsDead).ToList();
        foreach (var lobby in dead) {
            RemoveLobby(lobby.ConnectionData);
        }
        return dead.Count;
    }

    private bool RemoveLobby(string connectionData) {
        if (!_lobbies.TryRemove(connectionData, out var lobby)) return false;
        _tokenToConnectionData.TryRemove(lobby.HostToken, out _);
        _codeToConnectionData.TryRemove(lobby.LobbyCode, out _);
        return true;
    }

    private static string GenerateToken(int length) {
        return new string(Enumerable.Range(0, length).Select(_ => TokenChars[Random.Next(TokenChars.Length)]).ToArray());
    }

    private string GenerateLobbyCode() {
        // Generate unique code, retry if collision (extremely rare with 30^6 = 729M combinations)
        string code;
        do {
            code = new string(Enumerable.Range(0, LobbyCodeLength)
                .Select(_ => LobbyCodeChars[Random.Next(LobbyCodeChars.Length)]).ToArray());
        } while (_codeToConnectionData.ContainsKey(code));
        return code;
    }
}

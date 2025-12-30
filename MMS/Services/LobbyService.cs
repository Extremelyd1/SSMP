using System.Collections.Concurrent;
using MMS.Models;

namespace MMS.Services;

/// <summary>
/// Thread-safe in-memory lobby storage with heartbeat-based expiration.
/// Lobbies are keyed by ConnectionData (Steam ID or IP:Port).
/// </summary>
public class LobbyService {
    /// <summary>Thread-safe dictionary of lobbies keyed by ConnectionData.</summary>
    private readonly ConcurrentDictionary<string, Lobby> _lobbies = new();

    /// <summary>Maps host tokens to ConnectionData for quick lookup.</summary>
    private readonly ConcurrentDictionary<string, string> _tokenToConnectionData = new();

    /// <summary>Maps lobby codes to ConnectionData for quick lookup.</summary>
    private readonly ConcurrentDictionary<string, string> _codeToConnectionData = new();

    /// <summary>Random number generator for token and code generation.</summary>
    private static readonly Random Random = new();

    /// <summary>Characters used for host authentication tokens (lowercase alphanumeric).</summary>
    private const string TokenChars = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Characters used for lobby codes (uppercase alphanumeric).</summary>
    private const string LobbyCodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>Length of generated lobby codes.</summary>
    private const int LobbyCodeLength = 6;

    /// <summary>
    /// Creates a new lobby keyed by ConnectionData.
    /// </summary>
    public Lobby CreateLobby(
        string connectionData,
        string lobbyName,
        string lobbyType = "matchmaking",
        string? hostLanIp = null,
        bool isPublic = true
    ) {
        var hostToken = GenerateToken(32);
        
        // Only generate lobby codes for matchmaking lobbies
        // Steam lobbies use Steam's native join flow (no MMS invite codes)
        var lobbyCode = lobbyType == "steam" ? "" : GenerateLobbyCode();
        var lobby = new Lobby(connectionData, hostToken, lobbyCode, lobbyName, lobbyType, hostLanIp, isPublic);

        _lobbies[connectionData] = lobby;
        _tokenToConnectionData[hostToken] = connectionData;
        
        // Only register code if we generated one
        if (!string.IsNullOrEmpty(lobbyCode)) {
            _codeToConnectionData[lobbyCode] = connectionData;
        }

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
    /// Returns active PUBLIC lobbies, optionally filtered by type ("steam" or "matchmaking").
    /// Private lobbies are excluded from browser listings.
    /// </summary>
    public IEnumerable<Lobby> GetLobbies(string? lobbyType = null) {
        var lobbies = _lobbies.Values.Where(l => !l.IsDead && l.IsPublic);
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

    /// <summary>
    /// Removes a lobby by its ConnectionData and cleans up token/code mappings.
    /// </summary>
    /// <param name="connectionData">The ConnectionData of the lobby to remove.</param>
    /// <returns>True if the lobby was found and removed; otherwise, false.</returns>
    private bool RemoveLobby(string connectionData) {
        if (!_lobbies.TryRemove(connectionData, out var lobby)) return false;
        _tokenToConnectionData.TryRemove(lobby.HostToken, out _);
        _codeToConnectionData.TryRemove(lobby.LobbyCode, out _);
        return true;
    }

    /// <summary>
    /// Generates a random token of the specified length.
    /// </summary>
    /// <param name="length">Length of the token to generate.</param>
    /// <returns>A random alphanumeric token string.</returns>
    private static string GenerateToken(int length) {
        return new string(Enumerable.Range(0, length).Select(_ => TokenChars[Random.Next(TokenChars.Length)]).ToArray());
    }

    /// <summary>
    /// Generates a unique lobby code, retrying on collision.
    /// </summary>
    /// <returns>A unique 6-character uppercase alphanumeric code.</returns>
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

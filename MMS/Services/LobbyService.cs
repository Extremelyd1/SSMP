using System.Collections.Concurrent;
using MMS.Models;

namespace MMS.Services;

/// <summary>
/// Thread-safe in-memory lobby storage with heartbeat-based expiration.
/// Lobbies are keyed by ConnectionData (Steam ID or IP:Port).
/// </summary>
public class LobbyService(LobbyNameService lobbyNameService) {
    /// <summary>Thread-safe dictionary of lobbies keyed by ConnectionData.</summary>
    private readonly ConcurrentDictionary<string, Lobby> _lobbies = new();

    /// <summary>Maps host tokens to ConnectionData for quick lookup.</summary>
    private readonly ConcurrentDictionary<string, string> _tokenToConnectionData = new();

    /// <summary>Maps lobby codes to ConnectionData for quick lookup.</summary>
    private readonly ConcurrentDictionary<string, string> _codeToConnectionData = new();

    /// <summary>Consolidated metadata for active discovery sessions (matchmaking only).</summary>
    private readonly ConcurrentDictionary<string, DiscoveryTokenMetadata> _discoveryMetadata = new();

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
        // Matchmaking lobbies use a discovery token for NAT traversal; Steam lobbies do not.
        string? hostDiscoveryToken = null;
        if (lobbyType == "matchmaking") {
            hostDiscoveryToken = GenerateToken(32);
            _discoveryMetadata[hostDiscoveryToken] = new DiscoveryTokenMetadata {
                HostConnectionData = connectionData
            };
        }

        var lobby = new Lobby(connectionData, hostToken, lobbyCode, lobbyName, lobbyType, hostLanIp, isPublic) {
            HostDiscoveryToken = hostDiscoveryToken
        };

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
        var lobbies = _lobbies.Values.Where(l => l is { IsDead: false, IsPublic: true });
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

        // Cleanup expired discovery tokens (older than 2 minutes)
        var tokenCutoff = DateTime.UtcNow.AddMinutes(-2);
        var expiredTokens = _discoveryMetadata
            .Where(kvp => kvp.Value.CreatedAt < tokenCutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var token in expiredTokens) {
            _discoveryMetadata.TryRemove(token, out _);
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

        if (lobby.HostDiscoveryToken != null) {
            _discoveryMetadata.TryRemove(lobby.HostDiscoveryToken, out _);
        }

        lobbyNameService.FreeLobbyName(lobby.LobbyName);

        return true;
    }

    /// <summary>
    /// Registers a new discovery token for a client (matchmaking only).
    /// </summary>
    public string? RegisterClientDiscoveryToken(string lobbyCode, string clientIp) {
        var lobby = GetLobbyByCode(lobbyCode);
        if (lobby == null || lobby.LobbyType == "steam") return null;

        var token = GenerateToken(32);
        _discoveryMetadata[token] = new DiscoveryTokenMetadata {
            LobbyCode = lobbyCode,
            ClientIp = clientIp
        };
        return token;
    }

    /// <summary>
    /// Gets client info for a discovery token.
    /// </summary>
    public bool TryGetClientInfo(string token, out string lobbyCode, out string clientIp) {
        if (_discoveryMetadata.TryGetValue(token, out var metadata) && metadata.ClientIp != null) {
            lobbyCode = metadata.LobbyCode ?? "";
            clientIp = metadata.ClientIp;
            return true;
        }
        lobbyCode = "";
        clientIp = "";
        return false;
    }

    /// <summary>
    /// If the token belongs to a host, updates their lobby's external port.
    /// </summary>
    public void ApplyHostPort(string token, int port) {
        if (!_discoveryMetadata.TryGetValue(token, out var metadata) || metadata.HostConnectionData == null) return;
        var lobby = GetLobby(metadata.HostConnectionData);
        if (lobby != null) {
            lobby.ExternalPort = port;
        }
    }

    /// <summary>
    /// Updates the discovered port for a given token.
    /// </summary>
    public void SetDiscoveredPort(string token, int port) {
        if (_discoveryMetadata.TryGetValue(token, out var metadata)) {
            metadata.DiscoveredPort = port;
        }
    }

    /// <summary>
    /// Gets the discovered port for a given token, if any.
    /// </summary>
    public int? GetDiscoveredPort(string token) {
        return _discoveryMetadata.TryGetValue(token, out var metadata) ? metadata.DiscoveredPort : null;
    }

    /// <summary>
    /// Removes a discovery token and its associated metadata.
    /// </summary>
    public void RemoveDiscoveryToken(string token) {
        _discoveryMetadata.TryRemove(token, out _);
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

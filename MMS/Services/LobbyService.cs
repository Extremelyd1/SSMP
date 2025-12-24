using System.Collections.Concurrent;
using MMS.Models;

namespace MMS.Services;

/// <summary>
/// In-memory lobby storage and management with heartbeat-based liveness.
/// </summary>
public class LobbyService {
    private readonly ConcurrentDictionary<string, Lobby> _lobbies = new();
    private readonly ConcurrentDictionary<string, string> _tokenToLobbyId = new();
    private static readonly Random Random = new();

    /// <summary>
    /// Creates a new lobby and returns it (including the secret host token).
    /// </summary>
    public Lobby CreateLobby(string hostIp, int hostPort) {
        var id = GenerateLobbyId();
        var hostToken = GenerateHostToken();
        var lobby = new Lobby(id, hostToken, hostIp, hostPort);
        
        _lobbies[id] = lobby;
        _tokenToLobbyId[hostToken] = id;
        
        return lobby;
    }

    /// <summary>
    /// Gets a lobby by ID, or null if not found or dead.
    /// </summary>
    public Lobby? GetLobby(string id) {
        if (_lobbies.TryGetValue(id.ToUpperInvariant(), out var lobby)) {
            if (lobby.IsDead) {
                RemoveLobby(id);
                return null;
            }
            return lobby;
        }
        return null;
    }

    /// <summary>
    /// Gets a lobby by host token (for the host to find their own lobby).
    /// </summary>
    public Lobby? GetLobbyByToken(string token) {
        if (_tokenToLobbyId.TryGetValue(token, out var lobbyId)) {
            return GetLobby(lobbyId);
        }
        return null;
    }

    /// <summary>
    /// Updates the heartbeat for a lobby (host calls this periodically).
    /// </summary>
    public bool Heartbeat(string token) {
        var lobby = GetLobbyByToken(token);
        if (lobby == null) return false;
        
        lobby.LastHeartbeat = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Removes a lobby by ID.
    /// </summary>
    public bool RemoveLobby(string id) {
        if (_lobbies.TryRemove(id.ToUpperInvariant(), out var lobby)) {
            _tokenToLobbyId.TryRemove(lobby.HostToken, out _);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes a lobby by host token (for the host to close their lobby).
    /// </summary>
    public bool RemoveLobbyByToken(string token) {
        var lobby = GetLobbyByToken(token);
        if (lobby == null) return false;
        return RemoveLobby(lobby.Id);
    }

    /// <summary>
    /// Gets all active (non-dead) lobbies.
    /// </summary>
    public IEnumerable<Lobby> GetAllLobbies() {
        return _lobbies.Values.Where(l => !l.IsDead);
    }

    /// <summary>
    /// Removes all dead lobbies and returns count of removed.
    /// </summary>
    public int CleanupDeadLobbies() {
        var deadLobbies = _lobbies.Values.Where(l => l.IsDead).ToList();
        foreach (var lobby in deadLobbies) {
            RemoveLobby(lobby.Id);
        }
        return deadLobbies.Count;
    }

    /// <summary>
    /// Generates a unique lobby ID like "8X92-AC44".
    /// </summary>
    private string GenerateLobbyId() {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string id;
        do {
            var part1 = new string(Enumerable.Range(0, 4).Select(_ => chars[Random.Next(chars.Length)]).ToArray());
            var part2 = new string(Enumerable.Range(0, 4).Select(_ => chars[Random.Next(chars.Length)]).ToArray());
            id = $"{part1}-{part2}";
        } while (_lobbies.ContainsKey(id));
        
        return id;
    }

    /// <summary>
    /// Generates a secret host token.
    /// </summary>
    private string GenerateHostToken() {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Range(0, 32).Select(_ => chars[Random.Next(chars.Length)]).ToArray());
    }
}

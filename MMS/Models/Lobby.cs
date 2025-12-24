using System.Collections.Concurrent;

namespace MMS.Models;

/// <summary>
/// Represents a pending client waiting to punch.
/// </summary>
public record PendingClient(string ClientIp, int ClientPort, DateTime RequestedAt);

/// <summary>
/// Represents a game lobby for matchmaking.
/// </summary>
public class Lobby {
    public string Id { get; init; } = null!;
    public string HostToken { get; init; } = null!;
    public string HostIp { get; set; } = null!;
    public int HostPort { get; set; }
    public DateTime LastHeartbeat { get; set; }

    /// <summary>
    /// Clients waiting for the host to punch back to them.
    /// </summary>
    public ConcurrentQueue<PendingClient> PendingClients { get; } = new();

    /// <summary>
    /// Whether this lobby is considered dead (no heartbeat for 60+ seconds).
    /// </summary>
    public bool IsDead => DateTime.UtcNow - LastHeartbeat > TimeSpan.FromSeconds(60);

    public Lobby(string id, string hostToken, string hostIp, int hostPort) {
        Id = id;
        HostToken = hostToken;
        HostIp = hostIp;
        HostPort = hostPort;
        LastHeartbeat = DateTime.UtcNow;
    }
}

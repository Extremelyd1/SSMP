using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace MMS.Models;

/// <summary>
/// Client waiting for NAT hole-punch.
/// </summary>
public record PendingClient(string ClientIp, int ClientPort, DateTime RequestedAt);

/// <summary>
/// Game lobby. ConnectionData serves as both identifier and connection info.
/// Steam: ConnectionData = Steam lobby ID. Matchmaking: ConnectionData = IP:Port.
/// </summary>
public class Lobby(
    string connectionData,
    string hostToken,
    string lobbyCode,
    string lobbyName,
    string lobbyType = "matchmaking",
    string? hostLanIp = null
) {
    /// <summary>Connection data: Steam lobby ID for Steam, IP:Port for matchmaking.</summary>
    public string ConnectionData { get; } = connectionData;

    /// <summary>Secret token for host authentication.</summary>
    public string HostToken { get; } = hostToken;

    /// <summary>Human-readable 6-character invite code.</summary>
    public string LobbyCode { get; } = lobbyCode;

    /// <summary>Display name of the lobby.</summary>
    public string LobbyName { get; } = lobbyName;

    /// <summary>Lobby type: "steam" or "matchmaking".</summary>
    public string LobbyType { get; } = lobbyType;

    /// <summary>Optional LAN IP for local network discovery.</summary>
    public string? HostLanIp { get; } = hostLanIp;

    /// <summary>Timestamp of the last heartbeat from the host.</summary>
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    /// <summary>Queue of clients waiting for NAT hole-punch.</summary>
    public ConcurrentQueue<PendingClient> PendingClients { get; } = new();

    /// <summary>True if no heartbeat received in the last 60 seconds.</summary>
    public bool IsDead => DateTime.UtcNow - LastHeartbeat > TimeSpan.FromSeconds(60);

    /// <summary>
    /// WebSocket connection from the host for push notifications.
    /// </summary>
    public WebSocket? HostWebSocket { get; set; }
}

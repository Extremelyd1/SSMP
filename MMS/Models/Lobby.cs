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
    public string ConnectionData { get; } = connectionData;
    public string HostToken { get; } = hostToken;
    public string LobbyCode { get; } = lobbyCode;
    public string LobbyName { get; } = lobbyName;
    public string LobbyType { get; } = lobbyType;
    public string? HostLanIp { get; } = hostLanIp;

    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public ConcurrentQueue<PendingClient> PendingClients { get; } = new();
    public bool IsDead => DateTime.UtcNow - LastHeartbeat > TimeSpan.FromSeconds(60);

    /// <summary>
    /// WebSocket connection from the host for push notifications.
    /// </summary>
    public WebSocket? HostWebSocket { get; set; }
}

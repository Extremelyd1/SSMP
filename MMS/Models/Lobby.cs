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
    string? hostLanIp = null,
    bool isPublic = true
) {
    /// <summary>Stable connection data used as the lobby identity and storage key.</summary>
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

    /// <summary>Whether the lobby should appear in public browser listings.</summary>
    public bool IsPublic { get; } = isPublic;

    /// <summary>Timestamp of the last heartbeat from the host.</summary>
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    /// <summary>Queue of clients waiting for NAT hole-punch.</summary>
    public ConcurrentQueue<PendingClient> PendingClients { get; } = new();

    /// <summary>True if no heartbeat received in the last 60 seconds.</summary>
    public bool IsDead => DateTime.UtcNow - LastHeartbeat > TimeSpan.FromSeconds(60);

    /// <summary>Discovered external port for NAT traversal.</summary>
    public int? ExternalPort { get; internal set; }

    /// <summary>Token used for UDP port discovery.</summary>
    public string? HostDiscoveryToken { get; init; }

    /// <summary>Connection data that should be advertised to clients.</summary>
    public string AdvertisedConnectionData {
        get {
            if (ExternalPort == null || LobbyType != "matchmaking") return ConnectionData;
            var ip = ConnectionData.Split(':')[0];
            return $"{ip}:{ExternalPort}";
        }
    }

    /// <summary>
    /// WebSocket connection from the host for push notifications.
    /// </summary>
    public WebSocket? HostWebSocket { get; set; }
}

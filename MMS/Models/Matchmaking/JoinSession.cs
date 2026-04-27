using System.Net.WebSockets;

namespace MMS.Models.Matchmaking;

/// <summary>
/// Short-lived matchmaking rendezvous session for a single join attempt.
/// </summary>
public sealed class JoinSession {
    /// <summary>
    /// Unique identifier for this join attempt.
    /// </summary>
    public required string JoinId { get; init; }

    /// <summary>
    /// Connection data of the lobby being joined.
    /// </summary>
    public required string LobbyConnectionData { get; init; }

    /// <summary>
    /// Public IP address of the joining client.
    /// </summary>
    public required string ClientIp { get; init; }

    /// <summary>
    /// Public IP address observed from the client's UDP discovery packet, if available.
    /// </summary>
    public string? ClientDiscoveredIp { get; set; }

    /// <summary>
    /// Client discovery token used to map the external port.
    /// </summary>
    public required string ClientDiscoveryToken { get; init; }

    /// <summary>
    /// Externally visible client port once discovery completes.
    /// </summary>
    public int? ClientExternalPort { get; set; }

    /// <summary>
    /// Indicates that the join is waiting for a fresh host mapping after requesting a host refresh.
    /// </summary>
    public bool AwaitingHostRefresh { get; set; }

    /// <summary>
    /// The client WebSocket attached to this join attempt, if connected.
    /// </summary>
    public WebSocket? ClientWebSocket { get; set; }

    /// <summary>
    /// Timestamp when this join session should expire.
    /// </summary>
    public DateTime ExpiresAtUtc { get; } = DateTime.UtcNow.AddSeconds(20);
}

namespace MMS.Models;

/// <summary>
/// Metadata for an active NAT traversal discovery session.
/// </summary>
public sealed class DiscoveryTokenMetadata {
    /// <summary>
    /// The UTC timestamp when this discovery token was created.
    /// Used for automatic cleanup of stale sessions.
    /// </summary>
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>
    /// The external port discovered via UDP. 
    /// Null if the port has not been discovered yet.
    /// </summary>
    public int? DiscoveredPort { get; set; }

    /// <summary>
    /// The invite code of the lobby this token is associated with.
    /// Only populated for client discovery tokens.
    /// </summary>
    public string? LobbyCode { get; init; }

    /// <summary>
    /// The public IP address of the client performing discovery.
    /// Only populated for client discovery tokens.
    /// </summary>
    public string? ClientIp { get; init; }

    /// <summary>
    /// The connection data of the host lobby.
    /// Only populated for host discovery tokens.
    /// </summary>
    public string? HostConnectionData { get; init; }
}

using System.Net.WebSockets;
using MMS.Models.Lobbies;
using MMS.Models.Matchmaking;

namespace MMS.Services.Matchmaking;

/// <summary>
/// Compatibility facade over <see cref="JoinSessionCoordinator"/> for the join-session
/// lifecycle and NAT hole-punch coordination.
/// </summary>
/// <remarks>
/// A join session represents a single client attempt to connect to a lobby host.
/// Typical flow:
/// <list type="number">
///   <item><c>POST /lobby/{id}/join</c> -> <see cref="CreateJoinSession"/> allocates a session and client discovery token.</item>
///   <item>Client opens a WebSocket -> <see cref="AttachJoinWebSocket"/> stores it for server-push events.</item>
///   <item>Client UDP packet arrives -> <see cref="SetDiscoveredPortAsync"/> records the external port and sends <c>refresh_host_mapping</c> to the host.</item>
///   <item>Host UDP packet arrives -> <see cref="SetDiscoveredPortAsync"/> records the host port and sends synchronized <c>start_punch</c> to both sides.</item>
/// </list>
/// </remarks>
public class JoinSessionService {
    /// <summary>
    /// The core coordinator that handles the cross-component logic for joining sessions.
    /// </summary>
    private readonly JoinSessionCoordinator _coordinator;

    /// <summary>
    /// Initializes a new instance of the <see cref="JoinSessionService"/> class.
    /// </summary>
    /// <param name="coordinator">The core coordinator that handles join session lifecycle logic.</param>
    public JoinSessionService(JoinSessionCoordinator coordinator) {
        _coordinator = coordinator;
    }
    /// <summary>
    /// Allocates a new join session for a client attempting to connect to <paramref name="lobby"/>.
    /// Returns <see langword="null"/> for Steam lobbies.
    /// </summary>
    public JoinSession? CreateJoinSession(Lobby lobby, string clientIp) =>
        _coordinator.CreateJoinSession(lobby, clientIp);

    /// <summary>Returns an active, non-expired session by its identifier, or <see langword="null"/> if not found or expired.</summary>
    public JoinSession? GetJoinSession(string joinId) =>
        _coordinator.GetJoinSession(joinId);

    /// <summary>
    /// Associates a WebSocket with an existing session so the server can push events to the client.
    /// Returns <see langword="false"/> if the session was not found.
    /// </summary>
    public bool AttachJoinWebSocket(string joinId, WebSocket webSocket) =>
        _coordinator.AttachJoinWebSocket(joinId, webSocket);

    /// <summary>Records the externally observed UDP port for a discovery token and advances the punch flow.</summary>
    public Task SetDiscoveredPortAsync(string token, int port, CancellationToken cancellationToken = default) =>
        _coordinator.SetDiscoveredPortAsync(token, port, cancellationToken);

    /// <summary>Returns the externally observed UDP port for a discovery token, or <see langword="null"/> if not yet recorded.</summary>
    public int? GetDiscoveredPort(string token) =>
        _coordinator.GetDiscoveredPort(token);

    /// <summary>Sends the <c>begin_client_mapping</c> message to the client identified by <paramref name="joinId"/>.</summary>
    public Task SendBeginClientMappingAsync(string joinId, CancellationToken cancellationToken) =>
        _coordinator.SendBeginClientMappingAsync(joinId, cancellationToken);

    /// <summary>
    /// Asks the host to refresh its NAT mapping for the given join session.
    /// Returns <see langword="false"/> if the message could not be dispatched.
    /// </summary>
    public Task<bool> SendHostRefreshRequestAsync(string joinId, CancellationToken cancellationToken) =>
        _coordinator.SendHostRefreshRequestAsync(joinId, cancellationToken);

    /// <summary>Notifies both client and host of failure, then cleans up the session.</summary>
    public Task FailJoinSessionAsync(string joinId, string reason, CancellationToken cancellationToken = default) =>
        _coordinator.FailJoinSessionAsync(joinId, reason, cancellationToken);

    /// <summary>Removes all expired sessions and purges stale discovery tokens.</summary>
    public int CleanupExpiredSessions() =>
        _coordinator.CleanupExpiredSessions();

    /// <summary>
    /// Removes all sessions belonging to <paramref name="lobby"/> and its host discovery token.
    /// Called when a lobby is closed or evicted.
    /// </summary>
    internal void CleanupSessionsForLobby(Lobby lobby) =>
        _coordinator.CleanupSessionsForLobby(lobby);
}

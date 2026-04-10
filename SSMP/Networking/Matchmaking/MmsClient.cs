using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SSMP.Logging;
using SSMP.Networking.Matchmaking.Host;
using SSMP.Networking.Matchmaking.Join;
using SSMP.Networking.Matchmaking.Protocol;
using SSMP.Networking.Matchmaking.Query;
using SSMP.Networking.Matchmaking.Utilities;

namespace SSMP.Networking.Matchmaking;

/// <summary>
/// High-level facade for MatchMaking Service (MMS) operations.
/// Keeps the existing public surface stable while delegating work to focused collaborators.
///
/// Core collaborators:
/// <list type="bullet">
///   <item><see cref="MmsHostSessionService"/> for host lifecycle</item>
///   <item><see cref="MmsLobbyQueryService"/> for query/read operations</item>
///   <item><see cref="MmsJoinCoordinator"/> for client join rendezvous</item>
/// </list>
/// </summary>
internal class MmsClient {
    private readonly MmsHostSessionService _hostSession;
    private readonly MmsLobbyQueryService _queries;
    private readonly MmsJoinCoordinator _joinCoordinator;
    private MatchmakingError _lastHttpError = MatchmakingError.None;

    /// <summary>The last matchmaking error from the most recent operation.</summary>
    public MatchmakingError LastMatchmakingError =>
        _localError != MatchmakingError.None ? _localError : _lastHttpError;

    /// <summary>Internal error state for non-HTTP failures.</summary>
    private MatchmakingError _localError = MatchmakingError.None;

    /// <inheritdoc cref="MmsHostSessionService.RefreshHostMappingRequested"/>
    public event Action<string, string, long>? RefreshHostMappingRequested {
        add => _hostSession.RefreshHostMappingRequested += value;
        remove => _hostSession.RefreshHostMappingRequested -= value;
    }

    /// <inheritdoc cref="MmsHostSessionService.HostMappingReceived"/>
    public event Action? HostMappingReceived {
        add => _hostSession.HostMappingReceived += value;
        remove => _hostSession.HostMappingReceived -= value;
    }

    /// <inheritdoc cref="MmsHostSessionService.StartPunchRequested"/>
    public event Action<string, string, int, int, long>? StartPunchRequested {
        add => _hostSession.StartPunchRequested += value;
        remove => _hostSession.StartPunchRequested -= value;
    }

    public MmsClient(
        string baseUrl,
        MmsHostSessionService? hostSession = null,
        MmsLobbyQueryService? queries = null,
        MmsJoinCoordinator? joinCoordinator = null
    ) {
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        string? discoveryHost = null;
        if (Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var uri))
            discoveryHost = uri.Host;

        _hostSession = hostSession ??
                       new MmsHostSessionService(
                           normalizedBaseUrl,
                           discoveryHost,
                           new MmsWebSocketHandler(MmsUtilities.ToWebSocketUrl(normalizedBaseUrl))
                       );
        _queries = queries ?? new MmsLobbyQueryService(normalizedBaseUrl);
        _joinCoordinator = joinCoordinator ?? new MmsJoinCoordinator(normalizedBaseUrl, discoveryHost);
    }

    /// <summary>
    /// Updates the number of connected remote players.
    /// Immediately sends a heartbeat if the count changed and a lobby is active,
    /// so MMS can clear stale host mappings as soon as the last player disconnects.
    /// </summary>
    public void SetConnectedPlayers(int count) => _hostSession.SetConnectedPlayers(count);

    /// <summary>
    /// Creates a new lobby on MMS and starts the heartbeat and host WebSocket.
    /// </summary>
    /// <returns>Lobby code, lobby name, and host discovery token; all null on failure.</returns>
    public async Task<(string? lobbyCode, string? lobbyName, string? hostDiscoveryToken)> CreateLobbyAsync(
        int hostPort,
        bool isPublic = true,
        string gameVersion = "unknown",
        PublicLobbyType lobbyType = PublicLobbyType.Matchmaking
    ) {
        ClearErrors();
        var result = await _hostSession.CreateLobbyAsync(hostPort, isPublic, gameVersion, lobbyType);
        _lastHttpError = result.error;
        return result.result;
    }

    /// <summary>
    /// Registers an existing Steam lobby with MMS for discovery.
    /// </summary>
    /// <returns>MMS lobby code, or null on failure.</returns>
    public async Task<string?> RegisterSteamLobbyAsync(
        string steamLobbyId,
        bool isPublic = true,
        string gameVersion = "unknown"
    ) {
        ClearErrors();
        var result = await _hostSession.RegisterSteamLobbyAsync(steamLobbyId, isPublic, gameVersion);
        _lastHttpError = result.error;
        return result.lobbyCode;
    }

    /// <summary>Closes the active lobby and deregisters it from MMS.</summary>
    public void CloseLobby() => _hostSession.CloseLobby();

    /// <summary>Looks up lobby join details from MMS.</summary>
    public async Task<JoinLobbyResult?> JoinLobbyAsync(string lobbyId, int clientPort) {
        ClearErrors();
        var result = await _queries.JoinLobbyAsync(lobbyId, clientPort);
        _lastHttpError = result.error;
        return result.result;
    }

    /// <summary>
    /// Drives the client-side matchmaking WebSocket handshake.
    /// Sends UDP discovery packets to establish the NAT mapping, then waits
    /// for MMS to signal when both sides should begin simultaneous hole-punch.
    /// </summary>
    public async Task<MatchmakingJoinStartResult?> CoordinateMatchmakingJoinAsync(
        string joinId,
        Action<byte[], IPEndPoint> sendRawAction,
        CancellationToken cancellationToken = default
    ) {
        ClearErrors();
        return await _joinCoordinator.CoordinateAsync(joinId, sendRawAction, SetJoinFailed, cancellationToken);
    }

    /// <summary>
    /// Fetches a list of public lobbies from MMS.
    /// </summary>
    public async Task<List<PublicLobbyInfo>?> GetPublicLobbiesAsync(PublicLobbyType? lobbyType = null) {
        ClearErrors();
        var result = await _queries.GetPublicLobbiesAsync(lobbyType);
        _lastHttpError = result.error;
        return result.lobbies;
    }

    /// <summary>
    /// Contacts MMS and verifies that its advertised matchmaking protocol version
    /// matches the client's expected version.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when MMS is reachable and compatible,
    /// <see langword="false"/> when MMS is reachable but requires an update,
    /// or <see langword="null"/> when MMS could not be contacted.
    /// </returns>
    public async Task<bool?> ProbeMatchmakingCompatibilityAsync() {
        ClearErrors();
        var (isCompatible, error) = await _queries.ProbeMatchmakingCompatibilityAsync();
        _localError = error;
        return isCompatible;
    }

    /// <summary>
    /// Starts the WebSocket listener for host push events (pending clients / start-punch).
    /// Must be called after creating a lobby.
    /// </summary>
    public void StartWebSocketConnection() => _hostSession.StartWebSocketConnection();

    /// <summary>
    /// Fires off a background UDP discovery refresh for the given host token.
    /// Runs for up to <see cref="MmsProtocol.DiscoveryDurationSeconds"/> seconds.
    /// </summary>
    public void StartHostDiscoveryRefresh(string hostDiscoveryToken, Action<byte[], IPEndPoint> sendRawAction) =>
        _hostSession.StartHostDiscoveryRefresh(hostDiscoveryToken, sendRawAction);

    /// <summary>
    /// Stops the active host discovery refresh loop, if one is running.
    /// </summary>
    public void StopHostDiscoveryRefresh() => _hostSession.StopHostDiscoveryRefresh();

    /// <summary>
    /// Signals a join failure with a specific reason.
    /// </summary>
    private void SetJoinFailed(string reason) {
        Logger.Warn($"MmsClient: matchmaking join failed – {reason}");
        _localError = MatchmakingError.JoinFailed;
    }

    /// <summary>
    /// Clears the internal and HTTP error states.
    /// </summary>
    private void ClearErrors() {
        _localError = MatchmakingError.None;
        _lastHttpError = MatchmakingError.None;
    }
}

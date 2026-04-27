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

/// <summary>Public entry point for MMS coordination. Delegates to specialized services.</summary>
internal sealed class MmsClient {
    private readonly MmsLobbyQueryService _queries;
    private readonly MmsJoinCoordinator _joinCoordinator;
    
    public MmsHostSessionService HostSession { get; }

    /// <summary>Last error from most recent operation.</summary>
    public MatchmakingError LastMatchmakingError { get; private set; } = MatchmakingError.None;

    /// <summary>Last machine-readable join failure reason returned by MMS.</summary>
    public string? LastJoinFailureReason { get; private set; }

    public MmsClient(
        string baseUrl,
        int discoveryPort,
        MmsHostSessionService? hostSession = null,
        MmsLobbyQueryService? queries = null,
        MmsJoinCoordinator? joinCoordinator = null
    ) {
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        if (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var uri)) {
            throw new ArgumentException($"Invalid base URL: {baseUrl}. NAT discovery will fail.", nameof(baseUrl));
        }

        var discoveryHost = uri.Host;

        HostSession = hostSession ??
                       new MmsHostSessionService(
                           normalizedBaseUrl,
                           discoveryHost,
                           discoveryPort,
                           new MmsWebSocketHandler(MmsUtilities.ToWebSocketUrl(normalizedBaseUrl))
                       );
        _queries = queries ?? new MmsLobbyQueryService(normalizedBaseUrl);
        _joinCoordinator = joinCoordinator ?? new MmsJoinCoordinator(normalizedBaseUrl, discoveryHost, discoveryPort);
    }

    /// <summary>Updates connected players; triggers heartbeat if count changes.</summary>
    public void SetConnectedPlayers(int count) => HostSession.SetConnectedPlayers(count);

    /// <summary>Creates lobby and starts host services.</summary>
    /// <returns>Lobby code, lobby name, and host discovery token; all null on failure.</returns>
    public async Task<(string? lobbyCode, string? lobbyName, string? hostDiscoveryToken)> CreateLobbyAsync(
        int hostPort,
        bool isPublic = true,
        string gameVersion = "unknown",
        PublicLobbyType lobbyType = PublicLobbyType.Matchmaking
    ) {
        ClearErrors();
        var result = await HostSession.CreateLobbyAsync(hostPort, isPublic, gameVersion, lobbyType);
        LastMatchmakingError = result.error;
        return result.result;
    }

    /// <summary>Registers existing Steam lobby for discovery.</summary>
    /// <returns>MMS lobby code, or null on failure.</returns>
    public async Task<string?> RegisterSteamLobbyAsync(
        string steamLobbyId,
        bool isPublic = true,
        string gameVersion = "unknown"
    ) {
        ClearErrors();
        var result = await HostSession.RegisterSteamLobbyAsync(steamLobbyId, isPublic, gameVersion);
        LastMatchmakingError = result.error;
        return result.lobbyCode;
    }

    /// <summary>Closes active lobby and deregisters from MMS.</summary>
    public void CloseLobby() => HostSession.CloseLobby();

    /// <summary>Retrieves join details from MMS.</summary>
    public async Task<JoinLobbyResult?> JoinLobbyAsync(string lobbyId, int clientPort) {
        ClearErrors();
        var result = await _queries.JoinLobbyAsync(lobbyId, clientPort);
        LastMatchmakingError = result.error;
        return result.result;
    }

    /// <summary>Drives UDP discovery and WebSocket hole-punch signal wait.</summary>
    public async Task<MatchmakingJoinStartResult?> CoordinateMatchmakingJoinAsync(
        string joinId,
        Action<byte[], IPEndPoint> sendRawAction,
        CancellationToken cancellationToken = default
    ) {
        ClearErrors();
        return await _joinCoordinator.CoordinateAsync(joinId, sendRawAction, SetJoinFailed, cancellationToken);
    }

    /// <summary>Fetches public lobbies from MMS.</summary>
    public async Task<List<PublicLobbyInfo>?> GetPublicLobbiesAsync(PublicLobbyType? lobbyType = null) {
        ClearErrors();
        var result = await _queries.GetPublicLobbiesAsync(lobbyType);
        LastMatchmakingError = result.error;
        return result.lobbies;
    }

    /// <summary>Checks server reaching and version compatibility.</summary>
    public async Task<bool?> ProbeMatchmakingCompatibilityAsync() {
        ClearErrors();
        var (isCompatible, error) = await _queries.ProbeMatchmakingCompatibilityAsync();
        LastMatchmakingError = error;
        return isCompatible;
    }

    /// <summary>Starts host push event listener. Call after creating lobby.</summary>
    public void StartWebSocketConnection() => HostSession.StartWebSocketConnection();

    /// <summary>Triggers background UDP discovery refresh for given token.</summary>
    public void StartHostDiscoveryRefresh(string hostDiscoveryToken, Action<byte[], IPEndPoint> sendRawAction) =>
        HostSession.StartHostDiscoveryRefresh(hostDiscoveryToken, sendRawAction);

    /// <summary>Stops active host discovery refresh loop.</summary>
    public void StopHostDiscoveryRefresh() => HostSession.StopHostDiscoveryRefresh();

    /// <summary>
    /// Signals a join failure with a specific reason.
    /// </summary>
    private void SetJoinFailed(string reason) {
        Logger.Warn($"MmsClient: matchmaking join failed - {reason}");
        LastMatchmakingError = MatchmakingError.JoinFailed;
        LastJoinFailureReason = reason;
    }

    /// <summary>
    /// Clears the internal and HTTP error states.
    /// </summary>
    private void ClearErrors() {
        LastMatchmakingError = MatchmakingError.None;
        LastJoinFailureReason = null;
    }
}

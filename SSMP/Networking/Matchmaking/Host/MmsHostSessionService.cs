using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SSMP.Logging;
using SSMP.Networking.Matchmaking.Join;
using SSMP.Networking.Matchmaking.Parsing;
using SSMP.Networking.Matchmaking.Protocol;
using SSMP.Networking.Matchmaking.Transport;
using SSMP.Networking.Matchmaking.Utilities;

namespace SSMP.Networking.Matchmaking.Host;

/// <summary>
/// Manages the full lifecycle of the local host's MMS lobby session, including
/// lobby creation, heartbeat keep-alive, UDP discovery refresh, and clean teardown.
/// </summary>
internal sealed class MmsHostSessionService {
    /// <summary>The base HTTP URL of the MMS server (e.g. <c>https://mms.example.com</c>).</summary>
    private readonly string _baseUrl;

    /// <summary>
    /// Hostname used for UDP NAT hole-punch discovery, or <c>null</c> if discovery
    /// is disabled for this session.
    /// </summary>
    private readonly string? _discoveryHost;

    private readonly object _sessionLock = new();

    /// <summary>WebSocket handler that receives real-time MMS server events.</summary>
    private readonly MmsWebSocketHandler _webSocket;

    /// <summary>
    /// Bearer token issued by MMS when the lobby was created. Used to authenticate
    /// heartbeat and delete requests. <c>null</c> when no lobby is active.
    /// </summary>
    private string? _hostToken;

    /// <summary>
    /// The MMS lobby ID of the currently active session, or <c>null</c> when no
    /// lobby is active.
    /// </summary>
    private string? _currentLobbyId;

    /// <summary>
    /// Timer that fires <see cref="SendHeartbeat"/> at regular intervals to keep
    /// the MMS lobby alive. <c>null</c> when no lobby is active.
    /// </summary>
    private Timer? _heartbeatTimer;

    /// <summary>The number of players currently connected to this host's session.</summary>
    private int _connectedPlayers;

    /// <summary>Count of consecutive heartbeat send failures observed by the timer callback.</summary>
    private int _heartbeatFailureCount;

    /// <summary>
    /// Cancellation source that controls the background UDP discovery refresh task.
    /// <c>null</c> when no refresh is running.
    /// </summary>
    private CancellationTokenSource? _hostDiscoveryRefreshCts;

    /// <summary>
    /// Initializes a new <see cref="MmsHostSessionService"/>.
    /// </summary>
    /// <param name="baseUrl">Base HTTP URL of the MMS server.</param>
    /// <param name="discoveryHost">
    /// Hostname of the MMS UDP discovery endpoint, or <c>null</c> to disable
    /// NAT hole-punch discovery.
    /// </param>
    /// <param name="webSocket">WebSocket handler for real-time MMS events.</param>
    public MmsHostSessionService(
        string baseUrl,
        string? discoveryHost,
        MmsWebSocketHandler webSocket
    ) {
        _baseUrl = baseUrl;
        _discoveryHost = discoveryHost;
        _webSocket = webSocket;
    }

    /// <summary>
    /// Raised when MMS requests a host-mapping refresh.
    /// Provides the discovery token, peer address, and a correlation timestamp.
    /// Forwarded directly from <see cref="MmsWebSocketHandler.RefreshHostMappingRequested"/>.
    /// </summary>
    public event Action<string, string, long>? RefreshHostMappingRequested {
        add => _webSocket.RefreshHostMappingRequested += value;
        remove => _webSocket.RefreshHostMappingRequested -= value;
    }

    /// <summary>
    /// Raised when MMS confirms that a host mapping has been received and recorded.
    /// Forwarded directly from <see cref="MmsWebSocketHandler.HostMappingReceived"/>.
    /// </summary>
    public event Action? HostMappingReceived {
        add => _webSocket.HostMappingReceived += value;
        remove => _webSocket.HostMappingReceived -= value;
    }

    /// <summary>
    /// Raised when MMS instructs this host to begin NAT hole-punching toward a client.
    /// Provides the peer token, peer address, port, punch ID, and a correlation timestamp.
    /// Forwarded directly from <see cref="MmsWebSocketHandler.StartPunchRequested"/>.
    /// </summary>
    public event Action<string, string, int, int, long>? StartPunchRequested {
        add => _webSocket.StartPunchRequested += value;
        remove => _webSocket.StartPunchRequested -= value;
    }

    /// <summary>
    /// Updates the number of players connected to this host and immediately sends
    /// a heartbeat to MMS if the count has changed and a lobby is active.
    /// Negative values are clamped to zero.
    /// </summary>
    /// <param name="count">New connected-player count.</param>
    public void SetConnectedPlayers(int count) {
        var normalized = System.Math.Max(0, count);
        var previous = Interlocked.Exchange(ref _connectedPlayers, normalized);
        if (previous == normalized) return;

        if (_hostToken != null) SendHeartbeat(state: null);
    }

    /// <summary>
    /// Creates a new UDP lobby on MMS and activates the local session on success.
    /// </summary>
    /// <param name="hostPort">UDP port this host is listening on.</param>
    /// <param name="isPublic">Whether the lobby should appear in public listings.</param>
    /// <param name="gameVersion">Game version string used for matchmaking compatibility checks.</param>
    /// <param name="lobbyType">Lobby subtype (e.g. casual, ranked).</param>
    /// <returns>
    /// A tuple of <c>(lobbyCode, lobbyName, hostDiscoveryToken)</c> on success,
    /// or <c>(null, null, null)</c> if the request failed or the response was invalid.
    /// </returns>
    public async
        Task<((string? lobbyCode, string? lobbyName, string? hostDiscoveryToken) result, MatchmakingError error)>
        CreateLobbyAsync(
            int hostPort,
            bool isPublic,
            string gameVersion,
            PublicLobbyType lobbyType
        ) {
        var (buffer, length) = MmsJsonParser.FormatCreateLobbyJson(
            hostPort, isPublic, gameVersion, lobbyType, MmsUtilities.GetLocalIpAddress()
        );
        try {
            var response = await MmsHttpClient.PostJsonAsync(
                $"{_baseUrl}{MmsRoutes.Lobby}",
                new string(buffer, 0, length)
            );
            if (!response.Success || response.Body == null)
                return ((null, null, null), response.Error);

            return TryActivateLobby(
                response.Body,
                "CreateLobby",
                out var lobbyName,
                out var lobbyCode,
                out var hostDiscoveryToken
            )
                ? ((lobbyCode, lobbyName, hostDiscoveryToken), MatchmakingError.None)
                : ((null, null, null), MatchmakingError.NetworkFailure);
        } finally {
            MmsJsonParser.ReturnBuffer(buffer);
        }
    }

    /// <summary>
    /// Registers an existing Steam lobby with MMS, creating a corresponding MMS lobby entry.
    /// </summary>
    /// <param name="steamLobbyId">Steam lobby identifier to associate.</param>
    /// <param name="isPublic">Whether the lobby should appear in public MMS listings.</param>
    /// <param name="gameVersion">Game version string for matchmaking compatibility.</param>
    /// <returns>
    /// The MMS lobby code on success, or <c>null</c> if the request failed or the
    /// response was invalid.
    /// </returns>
    public async Task<(string? lobbyCode, MatchmakingError error)> RegisterSteamLobbyAsync(
        string steamLobbyId,
        bool isPublic,
        string gameVersion
    ) {
        var response = await MmsHttpClient.PostJsonAsync(
            $"{_baseUrl}{MmsRoutes.Lobby}",
            BuildSteamLobbyJson(steamLobbyId, isPublic, gameVersion)
        );
        if (!response.Success || response.Body == null)
            return (null, response.Error);

        if (!TryActivateLobby(response.Body, "RegisterSteamLobby", out _, out var lobbyCode, out _))
            return (null, MatchmakingError.NetworkFailure);

        Logger.Info($"MmsHostSessionService: registered Steam lobby {steamLobbyId} as MMS lobby {lobbyCode}");
        return (lobbyCode, MatchmakingError.None);
    }

    /// <summary>
    /// Tears down the active lobby: stops the heartbeat timer, cancels UDP discovery,
    /// closes the WebSocket connection, and sends a DELETE to MMS in the background.
    /// Does nothing if no lobby is currently active.
    /// </summary>
    public void CloseLobby() {
        (string token, string? lobbyId)? snapshot;
        lock (_sessionLock) {
            if (_hostToken == null) return;
            snapshot = SnapshotAndClearSessionUnsafe();
        }

        StopHeartbeat();
        StopHostDiscoveryRefresh();
        _webSocket.Stop();

        var (tokenSnapshot, lobbyIdSnapshot) = snapshot.Value;
        _ = SafeDeleteLobbyAsync(tokenSnapshot, lobbyIdSnapshot);
    }

    /// <summary>
    /// Starts the WebSocket connection that receives pending-client and punch events
    /// from MMS. Requires an active lobby (<see cref="CreateLobbyAsync"/> must have
    /// succeeded first).
    /// </summary>
    public void StartPendingClientPolling() {
        if (_hostToken == null) {
            Logger.Error("MmsHostSessionService: cannot start WebSocket without a host token");
            return;
        }

        _webSocket.Start(_hostToken);
    }

    /// <summary>
    /// Starts a background task that sends periodic UDP discovery packets to MMS
    /// for the duration of <see cref="MmsProtocol.DiscoveryDurationSeconds"/>,
    /// enabling MMS to learn this host's external IP and port for NAT hole-punching.
    /// Any previously running refresh is stopped first.
    /// Does nothing if <see cref="_discoveryHost"/> is <c>null</c>.
    /// </summary>
    /// <param name="hostDiscoveryToken">Session token sent inside each UDP packet.</param>
    /// <param name="sendRawAction">
    /// Callback that writes raw bytes through the caller's UDP socket to the given endpoint.
    /// </param>
    public void StartHostDiscoveryRefresh(string hostDiscoveryToken, Action<byte[], IPEndPoint> sendRawAction) {
        if (_discoveryHost == null) return;

        StopHostDiscoveryRefresh();

        _hostDiscoveryRefreshCts =
            new CancellationTokenSource(TimeSpan.FromSeconds(MmsProtocol.DiscoveryDurationSeconds));
        var cts = _hostDiscoveryRefreshCts;

        MmsUtilities.RunBackground(
            RunHostDiscoveryRefreshAsync(hostDiscoveryToken, sendRawAction, cts),
            nameof(MmsHostSessionService),
            "host UDP discovery"
        );
    }

    /// <summary>
    /// Cancels the active UDP discovery refresh task, if any.
    /// Safe to call when no refresh is running.
    /// </summary>
    public void StopHostDiscoveryRefresh() {
        _hostDiscoveryRefreshCts?.Cancel();
        _hostDiscoveryRefreshCts = null;
    }

    /// <summary>
    /// Builds the JSON request body for a Steam lobby registration.
    /// </summary>
    /// <param name="steamLobbyId">Steam lobby ID sent as <c>ConnectionData</c>.</param>
    /// <param name="isPublic">Public-visibility flag.</param>
    /// <param name="gameVersion">Game version string for compatibility filtering.</param>
    /// <returns>A JSON string ready to POST to the MMS lobby endpoint.</returns>
    private static string BuildSteamLobbyJson(string steamLobbyId, bool isPublic, string gameVersion) =>
        $"{{\"{MmsFields.ConnectionDataRequest}\":\"{MmsUtilities.EscapeJsonString(steamLobbyId)}\"," +
        $"\"{MmsFields.IsPublicRequest}\":{MmsUtilities.BoolToJson(isPublic)}," +
        $"\"{MmsFields.GameVersionRequest}\":\"{MmsUtilities.EscapeJsonString(gameVersion)}\"," +
        $"\"{MmsFields.LobbyTypeRequest}\":\"steam\"}}";

    /// <summary>
    /// Records the active lobby ID and host token, then starts the heartbeat timer.
    /// </summary>
    /// <param name="lobbyId">MMS lobby identifier.</param>
    /// <param name="hostToken">Bearer token for authenticating subsequent MMS requests.</param>
    private void ActivateLobby(string lobbyId, string hostToken) {
        lock (_sessionLock) {
            _hostToken = hostToken;
            _currentLobbyId = lobbyId;
        }

        StartHeartbeat();
    }

    /// <summary>
    /// Captures the current session token and lobby ID, then clears both fields.
    /// Called during <see cref="CloseLobby"/> to ensure the delete request uses
    /// the correct values even if state is mutated concurrently.
    /// </summary>
    /// <returns>
    /// A tuple of <c>(hostToken, lobbyId)</c> holding the values that were active
    /// at the moment of the snapshot.
    /// </returns>
    private (string token, string? lobbyId) SnapshotAndClearSessionUnsafe() {
        var snapshot = (_hostToken!, _currentLobbyId);
        _hostToken = null;
        _currentLobbyId = null;
        return snapshot;
    }

    /// <summary>
    /// Parses an MMS lobby-activation response and, on success, calls
    /// <see cref="ActivateLobby"/> and logs the result.
    /// </summary>
    /// <param name="response">Raw JSON response body from MMS.</param>
    /// <param name="operation">Human-readable operation name used in log messages.</param>
    /// <param name="lobbyName">Receives the lobby display name, or <c>null</c> on failure.</param>
    /// <param name="lobbyCode">Receives the short lobby join code, or <c>null</c> on failure.</param>
    /// <param name="hostDiscoveryToken">Receives the UDP discovery token, or <c>null</c> on failure.</param>
    /// <returns><c>true</c> if parsing and activation succeeded; <c>false</c> otherwise.</returns>
    private bool TryActivateLobby(
        string response,
        string operation,
        out string? lobbyName,
        out string? lobbyCode,
        out string? hostDiscoveryToken
    ) {
        if (!MmsResponseParser.TryParseLobbyActivation(
                response,
                out var lobbyId,
                out var hostToken,
                out lobbyName,
                out lobbyCode,
                out hostDiscoveryToken
            )) {
            Logger.Error($"MmsHostSessionService: invalid {operation} response (length={response.Length})");
            return false;
        }

        ActivateLobby(lobbyId!, hostToken!);
        Logger.Info($"MmsHostSessionService: {operation} succeeded for lobby {lobbyCode}");
        return true;
    }

    /// <summary>
    /// Stops any existing heartbeat timer and starts a new one that fires
    /// <see cref="SendHeartbeat"/> every <see cref="MmsProtocol.HeartbeatIntervalMs"/>.
    /// </summary>
    private void StartHeartbeat() {
        StopHeartbeat();
        _heartbeatTimer = new Timer(
            SendHeartbeat, null, MmsProtocol.HeartbeatIntervalMs, MmsProtocol.HeartbeatIntervalMs
        );
    }

    /// <summary>
    /// Disposes the heartbeat timer. Safe to call when no timer is active.
    /// </summary>
    private void StopHeartbeat() {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    /// <summary>
    /// Timer callback that POSTs the current connected-player count to the MMS
    /// heartbeat endpoint. Fire-and-forget; failures are silently dropped.
    /// </summary>
    /// <param name="state">Unused timer state; always <c>null</c>.</param>
    private void SendHeartbeat(object? state) {
        string? token;
        lock (_sessionLock) {
            token = _hostToken;
        }

        if (token == null) return;

        var heartbeatTask = MmsHttpClient.PostJsonAsync(
            $"{_baseUrl}{MmsRoutes.LobbyHeartbeat(token)}",
            BuildHeartbeatJson(_connectedPlayers)
        );
        heartbeatTask.ContinueWith(
            task => {
                if (task.IsFaulted) {
                    var failures = Interlocked.Increment(ref _heartbeatFailureCount);
                    Logger.Debug($"MmsHostSessionService: heartbeat send faulted ({failures} consecutive failures)");
                    return;
                }

                if (task.Result.Success) {
                    Interlocked.Exchange(ref _heartbeatFailureCount, 0);
                    return;
                }

                var rejectedFailures = Interlocked.Increment(ref _heartbeatFailureCount);
                Logger.Debug(
                    $"MmsHostSessionService: heartbeat rejected or failed ({rejectedFailures} consecutive failures)"
                );
            },
            TaskScheduler.Default
        );
    }

    /// <summary>
    /// Builds the JSON body for a heartbeat POST.
    /// </summary>
    /// <param name="connectedPlayers">Current connected-player count to report to MMS.</param>
    /// <returns>A JSON string ready to POST to the heartbeat endpoint.</returns>
    private static string BuildHeartbeatJson(int connectedPlayers) =>
        $"{{\"ConnectedPlayers\":{connectedPlayers}}}";

    /// <summary>
    /// Backing task for <see cref="StartHostDiscoveryRefresh"/>. Runs
    /// <see cref="UdpDiscoveryService.SendUntilCancelledAsync"/> and disposes
    /// <paramref name="cts"/> when it completes, regardless of outcome.
    /// </summary>
    /// <param name="hostDiscoveryToken">Token forwarded to <see cref="UdpDiscoveryService"/>.</param>
    /// <param name="sendRawAction">UDP send callback forwarded to <see cref="UdpDiscoveryService"/>.</param>
    /// <param name="cts">
    /// The <see cref="CancellationTokenSource"/> that governs this refresh's lifetime.
    /// Disposed here after the task ends.
    /// </param>
    private async Task RunHostDiscoveryRefreshAsync(
        string hostDiscoveryToken,
        Action<byte[], IPEndPoint> sendRawAction,
        CancellationTokenSource cts
    ) {
        try {
            if (_discoveryHost == null) return;

            await UdpDiscoveryService.SendUntilCancelledAsync(
                _discoveryHost,
                hostDiscoveryToken,
                sendRawAction,
                cts.Token
            );
        } finally {
            cts.Dispose();
            if (ReferenceEquals(_hostDiscoveryRefreshCts, cts))
                _hostDiscoveryRefreshCts = null;
        }
    }

    /// <summary>
    /// Sends a DELETE to the MMS lobby endpoint. Logs success or warns on failure.
    /// Intended to be called fire-and-forget after <see cref="CloseLobby"/> has
    /// already cleared the local session state.
    /// </summary>
    /// <param name="hostToken">Bearer token identifying the lobby to delete.</param>
    /// <param name="lobbyId">Lobby ID used only for logging.</param>
    private async Task SafeDeleteLobbyAsync(string hostToken, string? lobbyId) {
        var response = await MmsHttpClient.DeleteAsync($"{_baseUrl}{MmsRoutes.LobbyDelete(hostToken)}");
        if (response.Success) {
            Logger.Info($"MmsHostSessionService: closed lobby {lobbyId}");
            return;
        }

        Logger.Warn($"MmsHostSessionService: CloseLobby DELETE failed for lobby {lobbyId}");
    }
}

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

/// <summary>Host lobby lifecycle: creation, heartbeat, UDP discovery, and teardown.</summary>
internal sealed class MmsHostSessionService : IDisposable {
    /// <summary>The base HTTP URL of the MMS server (e.g. <c>https://mms.example.com</c>).</summary>
    private readonly string _baseUrl;

    /// <summary>NAT discovery hostname; null if disabled.</summary>
    private readonly string? _discoveryHost;

    /// <summary>Synchronization lock for thread-safe access to session state (tokens, lobby IDs).</summary>
    private readonly object _sessionLock = new();

    /// <summary>Prevents concurrent lobby creation.</summary>
    private int _creationLock;

    /// <summary>Whether this service instance has been disposed.</summary>
    private volatile bool _disposed;

    /// <summary>WebSocket handler that receives real-time MMS server events.</summary>
    private readonly MmsWebSocketHandler _webSocket;

    /// <summary>MMS session bearer token; null if no active lobby.</summary>
    private volatile string? _hostToken;

    /// <summary>
    /// The MMS lobby ID of the currently active session, or <c>null</c> when no
    /// lobby is active.
    /// </summary>
    private string? _currentLobbyId;

    /// <summary>MMS lobby keep-alive timer.</summary>
    private Timer? _heartbeatTimer;

    /// <summary>
    /// Cancellation source to suppress in-flight heartbeat continuations after the lobby is closed.
    /// </summary>
    private CancellationTokenSource? _heartbeatCts;

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
    /// Provides the join ID, host discovery token, and a server correlation timestamp.
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
    /// Provides the join ID, client IP, client port, host port, and a startTimeMs correlation timestamp.
    /// Forwarded directly from <see cref="MmsWebSocketHandler.StartPunchRequested"/>.
    /// </summary>
    public event Action<string, string, int, int, long>? StartPunchRequested {
        add => _webSocket.StartPunchRequested += value;
        remove => _webSocket.StartPunchRequested -= value;
    }

    /// <summary>Updates player count; triggers immediate heartbeat if changed.</summary>
    public void SetConnectedPlayers(int count) {
        if (_disposed) throw new ObjectDisposedException(nameof(MmsHostSessionService));

        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Connected player count cannot be negative.");

        var previous = Interlocked.Exchange(ref _connectedPlayers, count);
        if (previous == count) return;

        if (_hostToken != null) SendHeartbeat(state: null);
    }

    /// <summary>Creates lobby on MMS and activates session.</summary>
    public async
        Task<((string? lobbyCode, string? lobbyName, string? hostDiscoveryToken) result, MatchmakingError error)>
        CreateLobbyAsync(
            int hostPort,
            bool isPublic,
            string gameVersion,
            PublicLobbyType lobbyType
        ) {
        if (_disposed) throw new ObjectDisposedException(nameof(MmsHostSessionService));

        if (Interlocked.CompareExchange(ref _creationLock, 1, 0) != 0)
            return ((null, null, null), MatchmakingError.NetworkFailure);

        try {
            lock (_sessionLock) {
                if (_disposed) throw new ObjectDisposedException(nameof(MmsHostSessionService));
                if (_hostToken != null) return ((null, null, null), MatchmakingError.NetworkFailure);
            }

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
        } finally {
            Interlocked.Exchange(ref _creationLock, 0);
        }
    }

    /// <summary>
    /// Registers an existing Steam lobby with MMS, creating a corresponding MMS lobby entry.
    /// </summary>
    /// <param name="steamLobbyId">Steam lobby identifier to associate.</param>
    /// <param name="isPublic">Whether the lobby should appear in public MMS listings.</param>
    /// <param name="gameVersion">Game version string for matchmaking compatibility.</param>
    /// <returns>
    /// Returns the MMS lobby code on success; otherwise returns null along with a MatchmakingError describing the failure.
    /// </returns>
    public async Task<(string? lobbyCode, MatchmakingError error)> RegisterSteamLobbyAsync(
        string steamLobbyId,
        bool isPublic,
        string gameVersion
    ) {
        if (_disposed) throw new ObjectDisposedException(nameof(MmsHostSessionService));

        if (Interlocked.CompareExchange(ref _creationLock, 1, 0) != 0)
            return (null, MatchmakingError.NetworkFailure);

        try {
            lock (_sessionLock) {
                if (_disposed) throw new ObjectDisposedException(nameof(MmsHostSessionService));
                if (_hostToken != null) return (null, MatchmakingError.NetworkFailure);
            }

            var response = await MmsHttpClient.PostJsonAsync(
                $"{_baseUrl}{MmsRoutes.Lobby}",
                BuildSteamLobbyJson(steamLobbyId, isPublic, gameVersion)
            );
            if (!response.Success || response.Body == null)
                return (null, response.Error);

            return !TryActivateLobby(response.Body, "RegisterSteamLobby", out _, out var lobbyCode, out _)
                ? (null, MatchmakingError.NetworkFailure)
                : (lobbyCode, MatchmakingError.None);
        } finally {
            Interlocked.Exchange(ref _creationLock, 0);
        }
    }

    /// <summary>Stops session: heartbeat, discovery, and socket. Deletes lobby on MMS.</summary>
    public void CloseLobby() {
        (string token, string? lobbyId)? snapshot;
        lock (_sessionLock) {
            if (_hostToken == null) return;
            snapshot = SnapshotAndClearSessionUnsafe();
            StopHeartbeat();
        }

        StopHostDiscoveryRefresh();
        _webSocket.Stop();

        var (tokenSnapshot, lobbyIdSnapshot) = snapshot.Value;
        _ = SafeDeleteLobbyAsync(tokenSnapshot, lobbyIdSnapshot);
    }

    /// <summary>
    /// Starts the WebSocket connection that receives pending-client and punch events
    /// from MMS. Requires an active lobby; logs an error and returns if no host token is available.
    /// </summary>
    public void StartWebSocketConnection() {
        if (_disposed) throw new ObjectDisposedException(nameof(MmsHostSessionService));

        if (_hostToken == null) {
            Logger.Error("MmsHostSessionService: cannot start WebSocket without a host token.");
            return;
        }

        _webSocket.Start(_hostToken);
    }

    /// <summary>Starts periodic background UDP discovery for external IP learning.</summary>
    /// <param name="hostDiscoveryToken">Session token sent inside each UDP packet.</param>
    /// <param name="sendRawAction">
    /// Callback that writes raw bytes through the caller's UDP socket to the given endpoint.
    /// </param>
    public void StartHostDiscoveryRefresh(string hostDiscoveryToken, Action<byte[], IPEndPoint> sendRawAction) {
        if (_disposed) throw new ObjectDisposedException(nameof(MmsHostSessionService));

        if (_discoveryHost == null) return;

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(MmsProtocol.DiscoveryDurationSeconds));

        var oldCts = Interlocked.Exchange(ref _hostDiscoveryRefreshCts, cts);
        if (oldCts != null) {
            try {
                oldCts.Cancel();
            } catch (ObjectDisposedException) {
                /*ignored*/
            }

            oldCts.Dispose();
        }

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
        var cts = Interlocked.Exchange(ref _hostDiscoveryRefreshCts, null);
        if (cts == null)
            return;
        try {
            cts.Cancel();
        } catch (ObjectDisposedException) {
            /*ignored*/
        }

        cts.Dispose();
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
    /// Captures the current session token and lobby ID, then clears both fields.
    /// Called during <see cref="CloseLobby"/> to ensure the delete request uses
    /// the correct values even if state is mutated concurrently.
    /// IMPORTANT: Must be called while holding _sessionLock, and with _hostToken non-null.
    /// </summary>
    /// <returns>
    /// A tuple of <c>(hostToken, lobbyId)</c> holding the values that were active
    /// at the moment of the snapshot.
    /// </returns>
    private (string token, string? lobbyId) SnapshotAndClearSessionUnsafe() {
        System.Diagnostics.Debug.Assert(Monitor.IsEntered(_sessionLock));
        var snapshot = (_hostToken!, _currentLobbyId);
        _hostToken = null;
        _currentLobbyId = null;
        return snapshot;
    }

    /// <summary>Validates and records lobby activation. Deletes if disposed mid-flight.</summary>
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
            Logger.Error($"MmsHostSessionService: Invalid {operation} response (length={response.Length}).");
            return false;
        }

        lock (_sessionLock) {
            if (_disposed) {
                _ = SafeDeleteLobbyAsync(hostToken!, lobbyId);
                return false;
            }

            _hostToken = hostToken;
            _currentLobbyId = lobbyId;
            _heartbeatFailureCount = 0;
            StartHeartbeat();
        }

        Logger.Info($"MmsHostSessionService: {operation} succeeded for lobby {lobbyCode}.");
        return true;
    }

    /// <summary>
    /// Stops any existing heartbeat timer and starts a new one that fires
    /// <see cref="SendHeartbeat"/> every <see cref="MmsProtocol.HeartbeatIntervalMs"/>.
    /// IMPORTANT: Caller must hold _sessionLock.
    /// </summary>
    private void StartHeartbeat() {
        StopHeartbeat();
        _heartbeatCts = new CancellationTokenSource();
        _heartbeatTimer = new Timer(
            SendHeartbeat, null, MmsProtocol.HeartbeatIntervalMs, MmsProtocol.HeartbeatIntervalMs
        );
    }

    /// <summary>
    /// Disposes the heartbeat timer. Safe to call when no timer is active.
    /// IMPORTANT: Caller must hold _sessionLock.
    /// </summary>
    private void StopHeartbeat() {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        if (_heartbeatCts == null)
            return;
        try {
            _heartbeatCts.Cancel();
        } catch (ObjectDisposedException) {
            /*ignored*/
        }

        _heartbeatCts.Dispose();
        _heartbeatCts = null;
    }

    /// <summary>
    /// Timer callback that POSTs the current connected-player count to the MMS
    /// heartbeat endpoint. Fire-and-forget with a continuation that tracks and logs consecutive failures.
    /// Failures are not retried but are logged and tracked via a consecutive failure counter.
    /// </summary>
    /// <param name="state">Unused timer state; always <c>null</c>.</param>
    private void SendHeartbeat(object? state) {
        string? token;
        CancellationToken cancellationToken;
        lock (_sessionLock) {
            token = _hostToken;
            if (token == null || _heartbeatCts == null)
                return;
            cancellationToken = _heartbeatCts.Token;
        }

        var players = Interlocked.CompareExchange(ref _connectedPlayers, 0, 0);
        var heartbeatTask = MmsHttpClient.PostJsonAsync(
            $"{_baseUrl}{MmsRoutes.LobbyHeartbeat(token)}",
            BuildHeartbeatJson(players)
        );
        heartbeatTask.ContinueWith(
            task => {
                if (cancellationToken.IsCancellationRequested) return;

                if (task.IsFaulted) {
                    var failures = Interlocked.Increment(ref _heartbeatFailureCount);
                    Logger.Debug($"MmsHostSessionService: heartbeat send faulted ({failures} consecutive failures).");
                    return;
                }

                if (task.Result.Success) {
                    Interlocked.Exchange(ref _heartbeatFailureCount, 0);
                    return;
                }

                var rejectedFailures = Interlocked.Increment(ref _heartbeatFailureCount);
                Logger.Debug(
                    $"MmsHostSessionService: heartbeat rejected or failed ({rejectedFailures} consecutive failures)."
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
    /// <see cref="UdpDiscoveryService.SendUntilCancelledAsync"/>.
    /// </summary>
    /// <param name="hostDiscoveryToken">Token forwarded to <see cref="UdpDiscoveryService"/>.</param>
    /// <param name="sendRawAction">UDP send callback forwarded to <see cref="UdpDiscoveryService"/>.</param>
    /// <param name="cts">The active cancellation token source.</param>
    private async Task RunHostDiscoveryRefreshAsync(
        string hostDiscoveryToken,
        Action<byte[], IPEndPoint> sendRawAction,
        CancellationTokenSource cts
    ) {
        try {
            // Defensive check; normal flow is already guarded in StartHostDiscoveryRefresh
            if (_discoveryHost == null) return;

            await UdpDiscoveryService.SendUntilCancelledAsync(
                _discoveryHost,
                hostDiscoveryToken,
                sendRawAction,
                cts.Token
            );
        } finally {
            // If StopHostDiscoveryRefresh or a new Start request was called concurrently,
            // they will have already swapped out _hostDiscoveryRefreshCts and disposed this cts.
            // CompareExchange checks if we still own it; if so, we clear the field and dispose it ourselves.
            var currentCts = Interlocked.CompareExchange(ref _hostDiscoveryRefreshCts, null, cts);
            if (ReferenceEquals(currentCts, cts)) {
                cts.Dispose();
            }
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
            Logger.Info($"MmsHostSessionService: closed lobby {lobbyId}.");
            return;
        }

        Logger.Warn($"MmsHostSessionService: CloseLobby DELETE failed for lobby {lobbyId}.");
    }

    /// <summary>
    /// Marks the service as disposed, prevents further lobby creation, and closes the active lobby if present.
    /// </summary>
    public void Dispose() {
        Interlocked.Exchange(ref _creationLock, 1);
        lock (_sessionLock) {
            _disposed = true;
        }

        CloseLobby();
    }
}

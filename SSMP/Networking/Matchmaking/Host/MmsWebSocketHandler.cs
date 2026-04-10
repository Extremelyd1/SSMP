using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using SSMP.Logging;
using SSMP.Networking.Matchmaking.Parsing;
using SSMP.Networking.Matchmaking.Protocol;
using SSMP.Networking.Matchmaking.Utilities;

namespace SSMP.Networking.Matchmaking.Host;

/// <summary>
/// Manages the persistent WebSocket connection between a lobby host and MMS.
/// MMS pushes control events over this channel to coordinate matchmaking flow.
/// </summary>
internal sealed class MmsWebSocketHandler : IDisposable {
    /// <summary>The base WebSocket URL of the MMS service.</summary>
    private readonly string _wsBaseUrl;

    /// <summary>Synchronizes swaps of the active socket/CTS pair across overlapping start-stop cycles.</summary>
    private readonly object _stateGate = new();

    /// <summary>The underlying WebSocket client.</summary>
    private ClientWebSocket? _socket;

    /// <summary>Cancellation source for the background listening loop.</summary>
    private CancellationTokenSource? _cts;

    /// <summary>Monotonic version used to invalidate older background runs.</summary>
    private int _runVersion;

    /// <summary>
    /// Raised when MMS asks the host to refresh its NAT mapping.
    /// Arguments: joinId, hostDiscoveryToken, serverTimeMs.
    /// </summary>
    public event Action<string, string, long>? RefreshHostMappingRequested;

    /// <summary>
    /// Raised when MMS signals both sides to start simultaneous hole-punch.
    /// Arguments: joinId, clientIp, clientPort, hostPort, startTimeMs.
    /// </summary>
    public event Action<string, string, int, int, long>? StartPunchRequested;

    /// <summary>
    /// Raised when MMS confirms the host mapping has been learned and refresh
    /// packets can stop.
    /// </summary>
    public event Action? HostMappingReceived;

    /// <summary>
    /// Initialises a new <see cref="MmsWebSocketHandler"/>.
    /// </summary>
    /// <param name="wsBaseUrl">Base WebSocket URL of the MMS service (e.g. <c>wss://mms.example.com</c>).</param>
    public MmsWebSocketHandler(string wsBaseUrl) {
        _wsBaseUrl = wsBaseUrl;
    }

    /// <summary>
    /// Opens the WebSocket connection and begins listening for host push events.
    /// Any previously active connection is stopped first.
    /// Runs on a background thread; returns immediately.
    /// </summary>
    /// <param name="hostToken">Bearer token used to authenticate the WebSocket URL.</param>
    public void Start(string hostToken) {
        var runVersion = InvalidateActiveRun();
        MmsUtilities.RunBackground(
            RunAsync(hostToken, runVersion),
            nameof(MmsWebSocketHandler),
            "host WebSocket listener"
        );
    }

    /// <summary>
    /// Cancels the listening loop and disposes the WebSocket connection.
    /// Safe to call when no connection is active.
    /// </summary>
    public void Stop() {
        InvalidateActiveRun();
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();

    /// <summary>
    /// Entry point for the background task. Connects the socket, runs the
    /// receive loop, then tears down and logs the disconnection.
    /// </summary>
    /// <param name="hostToken">Bearer token used to build the WebSocket URL.</param>
    /// <param name="runVersion">Generation number captured when this run was started.</param>
    private async Task RunAsync(string hostToken, int runVersion) {
        var cts = new CancellationTokenSource();
        var socket = new ClientWebSocket();

        if (!TryRegisterRun(runVersion, socket, cts)) {
            cts.Dispose();
            socket.Dispose();
            return;
        }

        try {
            await ConnectAsync(socket, hostToken, cts.Token);
            await ReceiveLoopAsync(socket, cts.Token);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            Logger.Error($"MmsWebSocketHandler: error - {ex.Message}");
        } finally {
            TearDownSocket(runVersion, socket, cts);
        }
    }

    /// <summary>
    /// Connects <see cref="_socket"/> to the host WebSocket endpoint.
    /// </summary>
    /// <param name="socket">The WebSocket instance owned by the current run.</param>
    /// <param name="hostToken">Token appended to the WebSocket URL path.</param>
    /// <param name="cancellationToken">Cancellation token for the connection attempt.</param>
    private async Task ConnectAsync(ClientWebSocket socket, string hostToken, CancellationToken cancellationToken) {
        var uri = new Uri($"{_wsBaseUrl}{MmsRoutes.HostWebSocket(hostToken)}");
        await socket.ConnectAsync(uri, cancellationToken);
        Logger.Info("MmsWebSocketHandler: connected");
    }

    /// <summary>
    /// Reads messages from <see cref="_socket"/> until the connection closes or
    /// cancellation is requested. Each text frame is forwarded to
    /// <see cref="HandleMessage"/>.
    /// </summary>
    /// <param name="socket">The WebSocket instance owned by the current run.</param>
    /// <param name="cancellationToken">Cancellation token that ends the receive loop.</param>
    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken) {
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested) {
            var (messageType, message) = await MmsUtilities.ReceiveTextMessageAsync(socket, cancellationToken);
            if (messageType == WebSocketMessageType.Close) break;
            if (messageType != WebSocketMessageType.Text || string.IsNullOrEmpty(message)) continue;

            HandleMessage(message);
        }
    }

    /// <summary>
    /// Disposes <see cref="_socket"/> and nulls the reference, then logs the
    /// disconnection. Called from the <c>finally</c> block of <see cref="RunAsync"/>.
    /// </summary>
    /// <param name="runVersion">Generation number for the run being torn down.</param>
    /// <param name="socket">The socket owned by that run.</param>
    /// <param name="cts">The cancellation source owned by that run.</param>
    private void TearDownSocket(int runVersion, ClientWebSocket socket, CancellationTokenSource cts) {
        lock (_stateGate) {
            if (_runVersion == runVersion) {
                if (ReferenceEquals(_socket, socket))
                    _socket = null;

                if (ReferenceEquals(_cts, cts))
                    _cts = null;
            }
        }

        cts.Dispose();
        socket.Dispose();
        Logger.Info("MmsWebSocketHandler: disconnected");
    }

    /// <summary>
    /// Cancels any active run and returns the next valid version number.
    /// </summary>
    /// <returns>The generation number that should be used by the next background run.</returns>
    private int InvalidateActiveRun() {
        CancellationTokenSource? previousCts;
        int nextVersion;

        lock (_stateGate) {
            previousCts = _cts;
            _cts = null;
            _socket = null;
            nextVersion = unchecked(++_runVersion);
        }

        previousCts?.Cancel();
        return nextVersion;
    }

    /// <summary>
    /// Registers the run-local socket and cancellation source if the run is still current.
    /// </summary>
    /// <param name="runVersion">Generation number captured when the run was started.</param>
    /// <param name="socket">Socket allocated for the run.</param>
    /// <param name="cts">Cancellation source allocated for the run.</param>
    /// <returns><see langword="true"/> if the run is still current and has become active.</returns>
    private bool TryRegisterRun(int runVersion, ClientWebSocket socket, CancellationTokenSource cts) {
        lock (_stateGate) {
            if (_runVersion != runVersion)
                return false;

            _socket = socket;
            _cts = cts;
            return true;
        }
    }

    /// <summary>
    /// Extracts the <c>action</c> field from <paramref name="message"/> and
    /// routes it to the appropriate handler method.
    /// Unrecognised actions are silently ignored.
    /// </summary>
    /// <param name="message">Decoded UTF-8 text frame received from MMS.</param>
    private void HandleMessage(string message) {
        var span = message.AsSpan();
        var action = MmsJsonParser.ExtractValue(span, MmsFields.Action);

        switch (action) {
            case MmsActions.RefreshHostMapping: HandleRefreshHostMapping(span); break;
            case MmsActions.StartPunch: HandleStartPunch(span); break;
            case MmsActions.HostMappingReceived: HandleHostMappingReceived(); break;
            case MmsActions.JoinFailed: HandleJoinFailed(message); break;
        }
    }

    /// <summary>
    /// Handles a <c>refresh_host_mapping</c> message by extracting the join ID,
    /// discovery token, and server timestamp, then raising
    /// <see cref="RefreshHostMappingRequested"/>. Silently ignored if any required
    /// field is missing or unparseable.
    /// </summary>
    /// <param name="span">Span over the raw message text.</param>
    private void HandleRefreshHostMapping(ReadOnlySpan<char> span) {
        var joinId = MmsJsonParser.ExtractValue(span, MmsFields.JoinId);
        var token = MmsJsonParser.ExtractValue(span, MmsFields.HostDiscoveryToken);
        var timeStr = MmsJsonParser.ExtractValue(span, MmsFields.ServerTimeMs);

        if (joinId == null || token == null || !long.TryParse(timeStr, out var time))
            return;

        Logger.Info($"MmsWebSocketHandler: {MmsActions.RefreshHostMapping} for join {joinId}");
        RefreshHostMappingRequested?.Invoke(joinId, token, time);
    }

    /// <summary>
    /// Handles a <c>start_punch</c> message by extracting the join ID, client
    /// endpoint, host port, and start timestamp, then raising
    /// <see cref="StartPunchRequested"/>. Silently ignored if any required field
    /// is missing or unparseable.
    /// </summary>
    /// <param name="span">Span over the raw message text.</param>
    private void HandleStartPunch(ReadOnlySpan<char> span) {
        var joinId = MmsJsonParser.ExtractValue(span, MmsFields.JoinId);
        var clientIp = MmsJsonParser.ExtractValue(span, MmsFields.ClientIp);
        var clientPortStr = MmsJsonParser.ExtractValue(span, MmsFields.ClientPort);
        var hostPortStr = MmsJsonParser.ExtractValue(span, MmsFields.HostPort);
        var startTimeStr = MmsJsonParser.ExtractValue(span, MmsFields.StartTimeMs);

        if (joinId == null ||
            clientIp == null ||
            !int.TryParse(clientPortStr, out var clientPort) ||
            !int.TryParse(hostPortStr, out var hostPort) ||
            !long.TryParse(startTimeStr, out var startTimeMs))
            return;

        Logger.Info($"MmsWebSocketHandler: {MmsActions.StartPunch} for join {joinId} -> {clientIp}:{clientPort}");
        StartPunchRequested?.Invoke(joinId, clientIp, clientPort, hostPort, startTimeMs);
    }

    /// <summary>
    /// Handles a <c>host_mapping_received</c> message by logging and raising
    /// <see cref="HostMappingReceived"/>.
    /// </summary>
    private void HandleHostMappingReceived() {
        Logger.Info($"MmsWebSocketHandler: {MmsActions.HostMappingReceived}");
        HostMappingReceived?.Invoke();
    }

    /// <summary>
    /// Handles a <c>join_failed</c> message by logging the full message body
    /// as a warning. No event is raised because the host has no corrective action
    /// beyond surfacing the diagnostic.
    /// </summary>
    /// <param name="message">Full raw message text, logged verbatim for diagnostics.</param>
    private static void HandleJoinFailed(string message) {
        Logger.Warn($"MmsWebSocketHandler: {MmsActions.JoinFailed} - {message}");
    }
}

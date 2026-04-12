using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using SSMP.Logging;
using SSMP.Networking.Matchmaking.Parsing;
using SSMP.Networking.Matchmaking.Protocol;
using SSMP.Networking.Matchmaking.Utilities;

namespace SSMP.Networking.Matchmaking.Join;

/// <summary>Client-side matchmaking coordinator. Drives UDP mapping and awaits hole-punch signals.</summary>
internal sealed class MmsJoinCoordinator {
    /// <summary>Base HTTP URL of the MMS server (e.g. <c>https://mms.example.com</c>).</summary>
    private readonly string _baseUrl;

    /// <summary>
    /// Hostname used for UDP NAT hole-punch discovery, or <c>null</c> if discovery
    /// is unavailable. When <c>null</c>, <c>begin_client_mapping</c> messages are
    /// silently skipped.
    /// </summary>
    private readonly string? _discoveryHost;

    /// <summary>
    /// Initialises a new <see cref="MmsJoinCoordinator"/>.
    /// </summary>
    /// <param name="baseUrl">Base HTTP URL of the MMS server.</param>
    /// <param name="discoveryHost">
    /// Hostname of the MMS UDP discovery endpoint, or <c>null</c> to skip
    /// NAT hole-punch discovery.
    /// </param>
    public MmsJoinCoordinator(string baseUrl, string? discoveryHost) {
        _baseUrl = baseUrl;
        _discoveryHost = discoveryHost;
    }

    /// <summary>
    /// Mutable holder for the active UDP discovery <see cref="CancellationTokenSource"/>,
    /// allowing handler methods to update it without <c>ref</c> parameters.
    /// </summary>
    private sealed class DiscoverySession : IDisposable {
        /// <summary>
        /// The CTS governing the currently running discovery task, or <c>null</c>
        /// if no discovery is active.
        /// </summary>
        public CancellationTokenSource? Cts;

        /// <summary>Cancels <see cref="Cts"/> without disposing it.</summary>
        public void Cancel() {
            Cts?.Cancel();
        }

        /// <summary>Cancels and disposes <see cref="Cts"/> if it is set.</summary>
        public void Dispose() {
            Cts?.Cancel();
            Cts?.Dispose();
            Cts = null;
        }
    }

    /// <summary>Connects to join WebSocket and drives server-directed UDP mapping flow.</summary>
    public async Task<MatchmakingJoinStartResult?> CoordinateAsync(
        string joinId,
        Action<byte[], IPEndPoint> sendRawAction,
        Action<string> onJoinFailed,
        CancellationToken cancellationToken
    ) {
        if (_discoveryHost == null)
            Logger.Warn("MmsJoinCoordinator: discovery host unknown; UDP mapping will be skipped");

        using var socket = new ClientWebSocket();
        using var sessionCts =
            new CancellationTokenSource(TimeSpan.FromMilliseconds(MmsProtocol.MatchmakingWebSocketTimeoutMs));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCts.Token, cancellationToken);
        var discovery = new DiscoverySession();

        try {
            await ConnectAsync(socket, joinId, timeoutCts.Token);
            return await RunMessageLoopAsync(socket, timeoutCts, sendRawAction, discovery, onJoinFailed);
        } catch (OperationCanceledException) {
            onJoinFailed("Timeout.");
        } catch (WebSocketException ex) {
            onJoinFailed(ex.Message);
            Logger.Error($"MmsJoinCoordinator: matchmaking WebSocket error: {ex.Message}");
        } catch (Exception ex) {
            onJoinFailed(ex.Message);
            Logger.Error($"MmsJoinCoordinator: CoordinateAsync failed: {ex.Message}");
        } finally {
            discovery.Dispose();
        }

        return null;
    }

    /// <summary>Connects <paramref name="socket"/> to the MMS join WebSocket URL.</summary>
    private async Task ConnectAsync(ClientWebSocket socket, string joinId, CancellationToken ct) {
        var wsUrl =
            $"{MmsUtilities.ToWebSocketUrl(_baseUrl)}{MmsRoutes.JoinWebSocket(joinId)}" +
            $"?{MmsQueryKeys.MatchmakingVersion}={MmsProtocol.CurrentVersion}";

        await socket.ConnectAsync(new Uri(wsUrl), ct);
    }

    /// <summary>Reads WebSocket frames until terminal signal or timeout.</summary>
    private async Task<MatchmakingJoinStartResult?> RunMessageLoopAsync(
        ClientWebSocket socket,
        CancellationTokenSource timeoutCts,
        Action<byte[], IPEndPoint> sendRaw,
        DiscoverySession discovery,
        Action<string> onJoinFailed
    ) {
        while (socket.State == WebSocketState.Open && !timeoutCts.Token.IsCancellationRequested) {
            WebSocketMessageType messageType;
            string? message;
            try {
                (messageType, message) = await MmsUtilities.ReceiveTextMessageAsync(socket, timeoutCts.Token);
            } catch (InvalidOperationException ex) {
                onJoinFailed($"Matchmaking error: {ex.Message}");
                break;
            }

            if (messageType == WebSocketMessageType.Close) {
                onJoinFailed("Connection closed prematurely by server.");
                break;
            }
            if (messageType != WebSocketMessageType.Text || string.IsNullOrEmpty(message)) continue;

            var outcome = await HandleMessage(message, timeoutCts, sendRaw, discovery, onJoinFailed);
            if (outcome.hasResult) return outcome.result;
        }

        return null;
    }

    /// <summary>Routes message actions to handlers.</summary>
    private async Task<(bool hasResult, MatchmakingJoinStartResult? result)> HandleMessage(
        string message,
        CancellationTokenSource timeoutCts,
        Action<byte[], IPEndPoint> sendRaw,
        DiscoverySession discovery,
        Action<string> onJoinFailed
    ) {
        var action = MmsJsonParser.ExtractValue(message.AsSpan(), MmsFields.Action);

        switch (action) {
            case MmsActions.BeginClientMapping:
                RestartDiscovery(message, sendRaw, discovery);
                break;

            case MmsActions.StartPunch:
                var joinStart = await HandleStartPunchAsync(message, timeoutCts, discovery, onJoinFailed);
                return (true, joinStart);

            case MmsActions.ClientMappingReceived:
                discovery.Cancel();
                break;

            case MmsActions.JoinFailed:
                HandleJoinFailed(message, onJoinFailed);
                return (true, null);

            default:
                Logger.Debug($"MmsJoinCoordinator: Unknown action '{new string(action)}' mapped to message dropping");
                break;
        }

        return (false, null);
    }

    /// <summary>Restarts UDP discovery with new token.</summary>
    private void RestartDiscovery(
        string message,
        Action<byte[], IPEndPoint> sendRaw,
        DiscoverySession discovery
    ) {
        var token = MmsJsonParser.ExtractValue(message.AsSpan(), MmsFields.ClientDiscoveryToken);
        discovery.Cancel();
        discovery.Cts = StartDiscovery(token, sendRaw);
    }

    /// <summary>Stops discovery, parses payload, and delays until <paramref name="message"/> start time.</summary>
    private static async Task<MatchmakingJoinStartResult?> HandleStartPunchAsync(
        string message,
        CancellationTokenSource timeoutCts,
        DiscoverySession discovery,
        Action<string> onJoinFailed
    ) {
        discovery.Cancel();

        var joinStart = MmsResponseParser.ParseStartPunch(message.AsSpan());
        if (joinStart == null) {
            Logger.Warn($"MmsJoinCoordinator: Failed to parse start punch payload: {message}");
            onJoinFailed("Invalid start_punch payload received from server.");
            return null;
        }

        await DelayUntilAsync(joinStart.StartTimeMs, timeoutCts.Token);
        return joinStart;
    }

    /// <summary>
    /// Starts a new UDP discovery task for <paramref name="token"/>.
    /// Returns <c>null</c> without starting anything if <paramref name="token"/>
    /// is null or empty, or if <see cref="_discoveryHost"/> is <c>null</c>.
    /// </summary>
    /// <param name="token">UDP discovery token from the <c>begin_client_mapping</c> message.</param>
    /// <param name="sendRaw">UDP send callback forwarded to <see cref="UdpDiscoveryService"/>.</param>
    /// <returns>
    /// A new <see cref="CancellationTokenSource"/> governing the started discovery
    /// task, or <c>null</c> if discovery was not started.
    /// </returns>
    private CancellationTokenSource? StartDiscovery(string? token, Action<byte[], IPEndPoint> sendRaw) {
        if (string.IsNullOrEmpty(token)) {
            Logger.Warn("MmsJoinCoordinator: begin_client_mapping missing token");
            return null;
        }

        if (_discoveryHost == null)
            return null;

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(MmsProtocol.DiscoveryDurationSeconds));
        MmsUtilities.RunBackground(
            UdpDiscoveryService.SendUntilCancelledAsync(_discoveryHost, token, sendRaw, cts.Token),
            nameof(MmsJoinCoordinator),
            "client UDP discovery"
        );
        return cts;
    }

    /// <summary>
    /// Waits until the specified Unix timestamp (in milliseconds) before returning.
    /// Returns immediately if the target time is already in the past. If the target time
    /// is far in the future, the delay will simply block until <paramref name="ct"/> fires.
    /// </summary>
    /// <param name="targetUnixMs">Target time expressed as milliseconds since the Unix epoch (UTC).</param>
    /// <param name="ct">Cancellation token that can abort the wait early.</param>
    private static async Task DelayUntilAsync(long targetUnixMs, CancellationToken ct) {
        var delayMs = targetUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (delayMs > 0) await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct);
    }

    /// <summary>
    /// Extracts the server-supplied failure reason from a <c>join_failed</c> message,
    /// forwards it to the caller, and records the payload for diagnostics.
    /// </summary>
    /// <param name="message">Raw JSON WebSocket message from MMS.</param>
    /// <param name="onJoinFailed">Callback that updates higher-level matchmaking state.</param>
    private static void HandleJoinFailed(string message, Action<string> onJoinFailed) {
        onJoinFailed(MmsJsonParser.ExtractValue(message.AsSpan(), MmsFields.Reason) ?? "join_failed");
        Logger.Warn($"MmsJoinCoordinator: {MmsActions.JoinFailed} - {message}");
    }
}

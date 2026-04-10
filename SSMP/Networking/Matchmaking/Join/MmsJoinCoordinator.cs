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

/// <summary>
/// Coordinates the client-side MMS matchmaking flow over a WebSocket connection.
/// Drives UDP mapping refresh when instructed by the server and returns the
/// synchronized punch-start data needed to begin NAT hole-punching.
/// </summary>
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

    /// <summary>
    /// Connects to the MMS join WebSocket for <paramref name="joinId"/>, processes
    /// server-driven UDP mapping messages, and returns the punch-start payload once
    /// the server signals it is time to begin hole-punching.
    /// <para>
    /// The method drives the following server message sequence:
    /// <list type="bullet">
    ///   <item><term><c>begin_client_mapping</c></term><description>Starts (or restarts) UDP discovery with the supplied client token.</description></item>
    ///   <item><term><c>client_mapping_received</c></term><description>Stops the active UDP discovery task.</description></item>
    ///   <item><term><c>start_punch</c></term><description>Stops discovery, parses the punch payload, waits until the scheduled start time, then returns.</description></item>
    ///   <item><term><c>join_failed</c></term><description>Invokes <paramref name="onJoinFailed"/> with the server reason and returns <c>null</c>.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="joinId">Unique identifier for this join attempt, used to build the WebSocket URL.</param>
    /// <param name="sendRawAction">
    /// Callback that writes raw bytes through the caller's UDP socket to the given endpoint.
    /// Forwarded to <see cref="UdpDiscoveryService"/> during the mapping phase.
    /// </param>
    /// <param name="onJoinFailed">
    /// Invoked with a human-readable reason string whenever the join attempt fails
    /// (timeout, server rejection, or WebSocket error). Never invoked on success.
    /// </param>
    /// <param name="cancellationToken">Cancellation token that allows backing out of matchmaking mid-flow.</param>
    /// <returns>
    /// A <see cref="MatchmakingJoinStartResult"/> containing peer address and timing
    /// information, or <c>null</c> if the attempt failed or timed out.
    /// </returns>
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

    /// <summary>
    /// Connects <paramref name="socket"/> to the MMS join WebSocket URL for
    /// <paramref name="joinId"/>.
    /// </summary>
    /// <param name="socket">The WebSocket client to connect.</param>
    /// <param name="joinId">Join session ID appended to the WebSocket path.</param>
    /// <param name="ct">Cancellation token; typically the session timeout.</param>
    private async Task ConnectAsync(ClientWebSocket socket, string joinId, CancellationToken ct) {
        var wsUrl =
            $"{MmsUtilities.ToWebSocketUrl(_baseUrl)}{MmsRoutes.JoinWebSocket(joinId)}" +
            $"?{MmsQueryKeys.MatchmakingVersion}={MmsProtocol.CurrentVersion}";

        await socket.ConnectAsync(new Uri(wsUrl), ct);
    }

    /// <summary>
    /// Reads text frames from <paramref name="socket"/> and dispatches each to
    /// <see cref="HandleMessage"/> until the connection closes, the timeout fires,
    /// or a terminal action (<c>start_punch</c> or <c>join_failed</c>) is received.
    /// </summary>
    /// <param name="socket">The connected WebSocket client.</param>
    /// <param name="timeoutCts">Cancellation source governing the overall session timeout.</param>
    /// <param name="sendRaw">UDP send callback forwarded to discovery tasks.</param>
    /// <param name="discovery">Mutable holder for the active discovery CTS.</param>
    /// <param name="onJoinFailed">Failure callback invoked when the server sends a terminal rejection reason.</param>
    /// <returns>
    /// A <see cref="MatchmakingJoinStartResult"/> if <c>start_punch</c> was received
    /// and parsed successfully; <c>null</c> if the loop ended without a result.
    /// </returns>
    private async Task<MatchmakingJoinStartResult?> RunMessageLoopAsync(
        ClientWebSocket socket,
        CancellationTokenSource timeoutCts,
        Action<byte[], IPEndPoint> sendRaw,
        DiscoverySession discovery,
        Action<string> onJoinFailed
    ) {
        while (socket.State == WebSocketState.Open && !timeoutCts.Token.IsCancellationRequested) {
            var (messageType, message) = await MmsUtilities.ReceiveTextMessageAsync(socket, timeoutCts.Token);
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

    /// <summary>
    /// Extracts the <c>action</c> field from <paramref name="message"/> and routes
    /// it to the appropriate handler. Returns a result tuple indicating whether
    /// the loop should terminate.
    /// </summary>
    /// <param name="message">Decoded UTF-8 text frame from MMS.</param>
    /// <param name="timeoutCts">Session timeout source, passed through to <c>start_punch</c> handling.</param>
    /// <param name="sendRaw">UDP send callback, passed through to <c>begin_client_mapping</c> handling.</param>
    /// <param name="discovery">Mutable holder for the active discovery CTS.</param>
    /// <param name="onJoinFailed">Failure callback invoked when the message encodes a terminal join failure.</param>
    /// <returns>
    /// <c>(true, result)</c> when the loop should exit and return the parsed result;
    /// <c>(false, null)</c> to continue reading.
    /// </returns>
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

    /// <summary>
    /// Handles a <c>begin_client_mapping</c> message by extracting the client
    /// discovery token and restarting the UDP discovery task.
    /// </summary>
    /// <param name="message">Raw message text containing the <c>clientDiscoveryToken</c> field.</param>
    /// <param name="sendRaw">UDP send callback forwarded to the new discovery task.</param>
    /// <param name="discovery">Updated with the new discovery CTS.</param>
    private void RestartDiscovery(
        string message,
        Action<byte[], IPEndPoint> sendRaw,
        DiscoverySession discovery
    ) {
        var token = MmsJsonParser.ExtractValue(message.AsSpan(), MmsFields.ClientDiscoveryToken);
        discovery.Cancel();
        discovery.Cts = StartDiscovery(token, sendRaw);
    }

    /// <summary>
    /// Handles a <c>start_punch</c> message by cancelling discovery, parsing the
    /// punch payload, and waiting until the scheduled start time.
    /// </summary>
    /// <param name="message">Raw message text containing the punch payload fields.</param>
    /// <param name="timeoutCts">Used as the cancellation token for the start-time delay.</param>
    /// <param name="discovery">Cancelled immediately on entry.</param>
    /// <param name="onJoinFailed">Invoked if payload parsing fails.</param>
    /// <returns>
    /// The parsed <see cref="MatchmakingJoinStartResult"/>, or <c>null</c> if the
    /// payload could not be parsed.
    /// </returns>
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

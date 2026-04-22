using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using SSMP.Logging;
using SSMP.Networking.Matchmaking.Parsing;
using SSMP.Networking.Matchmaking.Protocol;
using SSMP.Networking.Matchmaking.Utilities;

namespace SSMP.Networking.Matchmaking.Host;

/// <summary>Host-MMS WebSocket manager. No auto-reconnect; manual <see cref="Start"/> required on drop.</summary>
internal sealed class MmsWebSocketHandler : IDisposable, IAsyncDisposable {
    /// <summary>The base WebSocket URL of the MMS service.</summary>
    private readonly string _wsBaseUrl;

    /// <summary>Captures context (usually Unity main thread) for event marshaling.</summary>
    private readonly SynchronizationContext? _mainThreadContext;

    /// <summary>Synchronizes swaps of the active socket/CTS pair across overlapping start-stop cycles.</summary>
    private readonly object _stateGate = new();

    /// <summary>The underlying WebSocket client.</summary>
    private ClientWebSocket? _socket;

    /// <summary>Cancellation source for the background listening loop.</summary>
    private CancellationTokenSource? _cts;

    /// <summary>Generation counter to invalidate stale background runs.</summary>
    private int _runVersion;

    /// <summary>Awaited by <see cref="DisposeAsync"/> to ensure clean exit.</summary>
    private Task _runTask = Task.CompletedTask;
    
    /// <summary>
    /// Maximum time to wait for a graceful WebSocket close handshake before
    /// abandoning and disposing the socket.
    /// </summary>
    private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Raised on NAT refresh request. Marshaled to construction thread.</summary>
    public event Action<string, string, long>? RefreshHostMappingRequested;

    /// <summary>
    /// Raised when MMS signals both sides to start simultaneous hole-punch.
    /// Arguments: joinId, clientIp, clientPort, hostPort, startTimeMs.
    /// <para>
    /// Handlers are always invoked on the <see cref="SynchronizationContext"/> that was
    /// active when this handler was constructed (typically the Unity main thread).
    /// Calling <see cref="Stop"/> or <see cref="Dispose"/> from within a handler is safe.
    /// Calling <see cref="DisposeAsync"/> and awaiting it inline from within a handler
    /// will deadlock; schedule the <c>await</c> on a separate task instead.
    /// </para>
    /// <para>
    /// <b>Ordering:</b> Events are posted to the main-thread queue in arrival order,
    /// but Unity processes posted callbacks at its own schedule (typically the next
    /// frame). Two events received in rapid succession are guaranteed to execute in
    /// order, but may execute across different frames.
    /// </para>
    /// </summary>
    public event Action<string, string, int, int, long>? StartPunchRequested;

    /// <summary>Raised on mapping confirmation. Marshaled to construction thread.</summary>
    public event Action? HostMappingReceived;

    /// <summary>Initializes handler. Capture current <see cref="SynchronizationContext"/> for marshaling.</summary>
    /// <param name="wsBaseUrl">Base WebSocket URL of the MMS service (e.g. <c>wss://mms.example.com</c>).</param>
    public MmsWebSocketHandler(string wsBaseUrl) {
        _wsBaseUrl = wsBaseUrl;
        _mainThreadContext = SynchronizationContext.Current;
    }

    /// <summary>Opens connection and starts listener. Stops existing connection first.</summary>
    /// <param name="hostToken">Bearer token used to authenticate the WebSocket URL.</param>
    public void Start(string hostToken) {
        var runVersion = InvalidateActiveRun();
        _runTask = MmsUtilities.RunBackground(
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
    /// <remarks>
    /// <para>
    /// This overload calls <see cref="Stop"/> but does <strong>not</strong> await the
    /// background task. If the background loop is still running when this returns, any
    /// in-flight I/O may complete after the caller has moved on.
    /// Prefer <see cref="DisposeAsync"/> in async contexts to guarantee the background
    /// task has fully exited before disposal completes.
    /// </para>
    /// </remarks>
    public void Dispose() => Stop();

    /// <summary>Stops and awaits background task. Prefer over <see cref="Dispose"/> in async contexts.</summary>
    public async ValueTask DisposeAsync() {
        Stop();
        await _runTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Entry point for the background task. Connects the socket, runs the receive
    /// loop, then drains any remaining queued events before tearing down.
    /// Each run creates its own isolated event queue so that overlapping start-stop
    /// cycles cannot steal or drop events across runs.
    /// </summary>
    /// <param name="hostToken">Bearer token used to build the WebSocket URL.</param>
    /// <param name="runVersion">Generation number captured when this run was started.</param>
    private async Task RunAsync(string hostToken, int runVersion) {
        var cts = new CancellationTokenSource();
        var socket = new ClientWebSocket();
        var eq = new EventQueue();

        if (!TryRegisterRun(runVersion, socket, cts)) {
            cts.Dispose();
            socket.Dispose();
            eq.Dispose();
            return;
        }

        try {
            await ConnectAsync(socket, hostToken, cts.Token);
            var drainTask = DrainEventQueueAsync(eq);
            await ReceiveLoopAsync(socket, cts.Token, eq);
            // Signal the dispatcher to stop after all queued callbacks are posted.
            eq.Enqueue(null);
            await drainTask;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            Logger.Error($"MmsWebSocketHandler: error - {ex.Message}");
        } finally {
            eq.Dispose();
            await TearDownSocket(runVersion, socket, cts);
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
    /// Reads messages from the supplied <paramref name="socket"/> until the connection closes or
    /// cancellation is requested. Each text frame is forwarded to
    /// <see cref="HandleMessage"/>. Events are enqueued rather than raised directly.
    /// </summary>
    /// <param name="socket">The WebSocket instance owned by the current run.</param>
    /// <param name="cancellationToken">Cancellation token that ends the receive loop.</param>
    /// <param name="eq">Run-local event queue for marshaling event invocations.</param>
    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken, EventQueue eq) {
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested) {
            WebSocketMessageType messageType;
            string? message;
            try {
                (messageType, message) = await MmsUtilities.ReceiveTextMessageAsync(socket, cancellationToken);
            } catch (InvalidOperationException ex) {
                Logger.Error($"MmsWebSocketHandler: disconnecting - {ex.Message}");
                break;
            }

            if (messageType == WebSocketMessageType.Close) break;
            if (messageType != WebSocketMessageType.Text || string.IsNullOrEmpty(message)) continue;

            HandleMessage(message, eq);
        }
    }

    /// <summary>
    /// Waits for items in the run-local <see cref="EventQueue"/> and dispatches them
    /// sequentially until a <see langword="null"/> sentinel is dequeued, indicating
    /// shutdown. Runs unconditionally to completion so that no already-queued events
    /// are dropped on shutdown.
    /// <para>
    /// Each action is posted through <see cref="_mainThreadContext"/> when one was
    /// captured at construction, ensuring all public events are raised on the Unity
    /// main thread. When no context is available the action is invoked directly.
    /// </para>
    /// <para>
    /// <b>Ordering vs. same-frame execution:</b> <see cref="SynchronizationContext.Post"/>
    /// is fire-and-forget -- this loop does <em>not</em> await completion of the posted
    /// callback before dequeuing the next item. Consequently:
    /// <list type="bullet">
    ///   <item>Events are <em>enqueued</em> on the main-thread context in strict arrival
    ///   order.</item>
    ///   <item>Unity drains its posted-callback queue at its own cadence (typically once
    ///   per frame), so two rapidly arriving events are ordered but may execute across
    ///   different frames.</item>
    ///   <item>Using <c>SynchronizationContext.Send</c> instead would enforce same-frame
    ///   execution but risks deadlocking if a handler calls
    ///   <see cref="DisposeAsync"/> inline, so <c>Post</c> is the correct choice here.
    ///   See the <see href="https://docs.unity3d.com/Manual/async-await-support.html">Unity
    ///   Asynchronous Programming</see> docs for background on <c>UnitySynchronizationContext</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="eq">Run-local event queue to drain.</param>
    private async Task DrainEventQueueAsync(EventQueue eq) {
        while (true) {
            await eq.WaitAsync();

            if (!eq.TryDequeue(out var action) || action == null)
                break;

            if (_mainThreadContext != null) {
                // Static lambda variable to prevent association with this class instance
                _mainThreadContext.Post(static a => ((Action) a!).Invoke(), action);
            } else {
                action.Invoke();
            }
        }
    }

    /// <summary>
    /// Attempts a graceful WebSocket close handshake, clears shared references
    /// if this run still owns them, then disposes the socket and cancellation source.
    /// Called from the <c>finally</c> block of <see cref="RunAsync"/>.
    /// </summary>
    /// <param name="runVersion">Generation number for the run being torn down.</param>
    /// <param name="socket">The socket owned by that run.</param>
    /// <param name="cts">The cancellation source owned by that run.</param>
    private async Task TearDownSocket(int runVersion, ClientWebSocket socket, CancellationTokenSource cts) {
        lock (_stateGate) {
            if (_runVersion == runVersion) {
                if (ReferenceEquals(_socket, socket))
                    _socket = null;

                if (ReferenceEquals(_cts, cts))
                    _cts = null;
            }
        }

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) {
            using var closeCts = new CancellationTokenSource(CloseHandshakeTimeout);
            try {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "stopping", closeCts.Token);
            } catch {
                // best-effort only - swallow all exceptions
            }
        }

        cts.Dispose();
        socket.Dispose();
        Logger.Info("MmsWebSocketHandler: disconnected");
    }

    /// <summary>
    /// Cancels any active run and returns the next valid version number.
    /// The cancel is performed inside the lock to prevent a race where
    /// <see cref="TearDownSocket"/> disposes the CTS on the background thread
    /// between the lock release and the <c>Cancel()</c> call on the calling thread.
    /// </summary>
    /// <returns>The generation number that should be used by the next background run.</returns>
    private int InvalidateActiveRun() {
        lock (_stateGate) {
            _cts?.Cancel();
            _cts = null;
            _socket = null;
            return unchecked(++_runVersion);
        }
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
    /// <param name="eq">Run-local event queue for marshalling event invocations.</param>
    private void HandleMessage(string message, EventQueue eq) {
        var action = MmsJsonParser.ExtractValue(message, MmsFields.Action);
        if (action == null) {
            Logger.Debug("MmsWebSocketHandler: invalid message, no defined action");
            return;
        }

        switch (action) {
            case MmsActions.RefreshHostMapping: HandleRefreshHostMapping(message, eq); break;
            case MmsActions.StartPunch: HandleStartPunch(message, eq); break;
            case MmsActions.HostMappingReceived: HandleHostMappingReceived(eq); break;
            case MmsActions.JoinFailed: HandleJoinFailed(message); break;
            default:
                Logger.Debug($"MmsWebSocketHandler: unknown action '{action}' mapped to message dropping");
                break;
        }
    }

    /// <summary>
    /// Handles a <c>refresh_host_mapping</c> message by extracting the join ID,
    /// discovery token, and server timestamp, then enqueuing a raise of
    /// <see cref="RefreshHostMappingRequested"/>. Silently ignored if any required
    /// field is missing or unparseable.
    /// </summary>
    /// <param name="message">The JSON message.</param>
    /// <param name="eq">Run-local event queue for marshalling event invocations.</param>
    private void HandleRefreshHostMapping(string message, EventQueue eq) {
        var joinId = MmsJsonParser.ExtractValue(message, MmsFields.JoinId);
        var token = MmsJsonParser.ExtractValue(message, MmsFields.HostDiscoveryToken);
        var timeStr = MmsJsonParser.ExtractValue(message, MmsFields.ServerTimeMs);

        if (joinId == null || token == null || !long.TryParse(timeStr, out var time))
            return;

        Logger.Info($"MmsWebSocketHandler: {MmsActions.RefreshHostMapping} for join {joinId}");
        eq.Enqueue(() => RefreshHostMappingRequested?.Invoke(joinId, token, time));
    }

    /// <summary>
    /// Handles a <c>start_punch</c> message by extracting the join ID, client
    /// endpoint, host port, and start timestamp, then enqueuing a raise of
    /// <see cref="StartPunchRequested"/>. Silently ignored if any required field
    /// is missing or unparseable.
    /// </summary>
    /// <param name="message">The JSON message.</param>
    /// <param name="eq">Run-local event queue for marshalling event invocations.</param>
    private void HandleStartPunch(string message, EventQueue eq) {
        var joinId = MmsJsonParser.ExtractValue(message, MmsFields.JoinId);
        var clientIp = MmsJsonParser.ExtractValue(message, MmsFields.ClientIp);
        var clientPortStr = MmsJsonParser.ExtractValue(message, MmsFields.ClientPort);
        var hostPortStr = MmsJsonParser.ExtractValue(message, MmsFields.HostPort);
        var startTimeStr = MmsJsonParser.ExtractValue(message, MmsFields.StartTimeMs);

        if (joinId == null ||
            clientIp == null ||
            !int.TryParse(clientPortStr, out var clientPort) ||
            !int.TryParse(hostPortStr, out var hostPort) ||
            !long.TryParse(startTimeStr, out var startTimeMs))
            return;

        Logger.Info($"MmsWebSocketHandler: {MmsActions.StartPunch} for join {joinId} -> {clientIp}:{clientPort}");
        eq.Enqueue(() => StartPunchRequested?.Invoke(joinId, clientIp, clientPort, hostPort, startTimeMs));
    }

    /// <summary>
    /// Handles a <c>host_mapping_received</c> message by logging and enqueuing
    /// a raise of <see cref="HostMappingReceived"/>.
    /// </summary>
    /// <param name="eq">Run-local event queue for marshalling event invocations.</param>
    private void HandleHostMappingReceived(EventQueue eq) {
        Logger.Info($"MmsWebSocketHandler: {MmsActions.HostMappingReceived}");
        eq.Enqueue(() => HostMappingReceived?.Invoke());
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

    /// <summary>
    /// Run-local event queue that pairs a <see cref="ConcurrentQueue{T}"/> with a
    /// <see cref="SemaphoreSlim"/> to provide an async-wait, single-consumer dispatch
    /// channel. Each <see cref="MmsWebSocketHandler.RunAsync"/> creates its own instance so that
    /// overlapping start-stop cycles cannot steal or drop events across runs.
    /// </summary>
    private sealed class EventQueue : IDisposable {
        private readonly ConcurrentQueue<Action?> _queue = new();
        private readonly SemaphoreSlim _semaphore = new(0);

        /// <summary>
        /// Enqueues an action (or <see langword="null"/> sentinel) and releases the
        /// semaphore so the drain loop wakes.
        /// </summary>
        public void Enqueue(Action? action) {
            _queue.Enqueue(action);
            _semaphore.Release();
        }

        /// <summary>Waits until an item is available.</summary>
        public Task WaitAsync() => _semaphore.WaitAsync();

        /// <summary>Attempts to dequeue the next item.</summary>
        public bool TryDequeue(out Action? action) => _queue.TryDequeue(out action);

        /// <inheritdoc/>
        public void Dispose() => _semaphore.Dispose();
    }
}

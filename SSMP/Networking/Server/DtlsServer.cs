using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using SSMP.Logging;

namespace SSMP.Networking.Server;

/// <summary>
/// DTLS implementation for a server-side peer for networking.
/// </summary>
internal class DtlsServer {
    /// <summary>
    /// The maximum packet size for sending and receiving DTLS packets.
    /// </summary>
    public const int MaxPacketSize = 1400;

    /// <summary>
    /// The socket instance for the underlying networking.
    /// The server only uses a single socket for all connections given that with UDP, we cannot create more than one
    /// on the same listening port.
    /// </summary>
    private Socket? _socket;

    /// <summary>
    /// The port that the server is started on.
    /// </summary>
    private int _port;

    // DTLS Protocol Objects
    /// <summary>
    /// The TLS client for communicating supported cipher suites and handling certificates.
    /// </summary>
    private ServerTlsServer? _tlsServer;

    // Threading & State
    /// <summary>
    /// Dictionary mapping IP endpoints to connection info (includes pending handshakes and connected clients).
    /// </summary>
    private readonly ConcurrentDictionary<IPEndPoint, ConnectionInfo> _connections;

    /// <summary>
    /// Token source for cancellation tokens for the accept and receive loop tasks.
    /// </summary>
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// The main socket receive loop thread.
    /// </summary>
    private Thread? _socketReceiveThread;

    /// <summary>
    /// Event that is called when data is received from the given DTLS server client.
    /// </summary>
    public event Action<DtlsServerClient, byte[], int>? DataReceivedEvent;

    /// <summary>
    /// Constructs a new DtlsServer instance.
    /// </summary>
    public DtlsServer() {
        _connections = new ConcurrentDictionary<IPEndPoint, ConnectionInfo>();
    }

    /// <summary>
    /// Start the DTLS server on the given port.
    /// </summary>
    /// <param name="port">The port to start listening on.</param>
    /// <param name="existingSocket">Optional pre-bound socket to reuse (for NAT traversal).</param>
    public void Start(int port, Socket? existingSocket = null) {
        _port = port;
        _tlsServer = new ServerTlsServer(new BcTlsCrypto());
        _cancellationTokenSource = new CancellationTokenSource();

        if (existingSocket != null) {
            _socket = existingSocket;
        } else {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, _port));
        }

        _socketReceiveThread = new Thread(() => SocketReceiveLoop(_cancellationTokenSource.Token)) {
            IsBackground = true
        };
        _socketReceiveThread.Start();
    }

    /// <summary>
    /// Send a raw UDP packet to the given endpoint (for hole punching).
    /// </summary>
    public void SendRaw(byte[] data, IPEndPoint endPoint) {
        _socket?.SendTo(data, endPoint);
    }

    /// <summary>
    /// Stop the DTLS server by disconnecting all clients and cancelling all running threads.
    /// </summary>
    public void Stop() {
        _cancellationTokenSource?.Cancel();

        _socket?.Close();
        _socket = null;

        // Wait for the socket receive thread to exit (short timeout to prevent freezing)
        if (_socketReceiveThread is { IsAlive: true }) {
            if (!_socketReceiveThread.Join(500)) {
                Logger.Warn("Socket receive thread did not exit within timeout, abandoning");
            }
        }

        _socketReceiveThread = null;

        _tlsServer?.Cancel();

        // Disconnect all clients without waiting for threads serially
        // We just cancel tokens and close transports. The threads are background and will die.
        foreach (var kvp in _connections) {
            var connInfo = kvp.Value;
            lock (connInfo.SyncRoot) {
                if (connInfo is { State: ConnectionState.Connected, Client: not null }) {
                    // Signal cancellation but don't join
                    connInfo.Client.ReceiveLoopTokenSource.Cancel();
                    connInfo.Client.DtlsTransport.Close();
                    connInfo.Client.DatagramTransport.Dispose();
                    connInfo.Client.ReceiveLoopTokenSource.Dispose();
                } else {
                    connInfo.DatagramTransport.Close();
                }

                connInfo.State = ConnectionState.Disconnected;
            }
        }

        _connections.Clear();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    /// <summary>
    /// Disconnect the client with the given IP endpoint from the server.
    /// </summary>
    /// <param name="endPoint">The IP endpoint of the client to disconnect.</param>
    public void DisconnectClient(IPEndPoint endPoint) {
        if (!_connections.TryGetValue(endPoint, out var connInfo)) {
            Logger.Warn("Could not find connection to disconnect");
            return;
        }

        lock (connInfo.SyncRoot) {
            if (connInfo.State != ConnectionState.Connected || connInfo.Client == null) {
                Logger.Warn($"Connection {endPoint} not in connected state");
                return;
            }

            connInfo.State = ConnectionState.Disconnecting;

            InternalDisconnectClient(connInfo.Client);
            connInfo.State = ConnectionState.Disconnected;
        }

        _connections.TryRemove(endPoint, out _);
    }

    /// <summary>
    /// Disconnect the given DTLS server client from the server. This will request cancellation of the "receive loop"
    /// for the client and close/cleanup the underlying DTLS objects.
    /// </summary>
    /// <param name="dtlsServerClient">The DTLS server client to disconnect.</param>
    private void InternalDisconnectClient(DtlsServerClient dtlsServerClient) {
        dtlsServerClient.ReceiveLoopTokenSource.Cancel();

        // Wait for the receive loop thread to exit if we can find it
        Thread? receiveThread = null;
        foreach (var kvp in _connections) {
            if (kvp.Value.Client == dtlsServerClient) {
                receiveThread = kvp.Value.ReceiveThread;
                break;
            }
        }

        if (receiveThread is { IsAlive: true }) {
            receiveThread.Join(TimeSpan.FromSeconds(2));
        }

        dtlsServerClient.DtlsTransport.Close();
        dtlsServerClient.DatagramTransport.Dispose();
        dtlsServerClient.ReceiveLoopTokenSource.Dispose();
    }

    /// <summary>
    /// Start a loop that will continuously receive data on the socket for existing and new clients.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for checking whether this task is requested to cancel.
    /// </param>
    private void SocketReceiveLoop(CancellationToken cancellationToken) {
        // Use pooled buffer to reduce GC pressure in hot path
        var buffer = ArrayPool<byte>.Shared.Rent(MaxPacketSize);

        while (!cancellationToken.IsCancellationRequested) {
            if (_socket == null) {
                Logger.Error("Socket was null during receive call.");
                break;
            }

            EndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
            int numReceived;

            try {
                numReceived = _socket.ReceiveFrom(buffer, SocketFlags.None, ref endPoint);
            } catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset) {
                // ICMP Port Unreachable, safe to ignore for UDP server
                continue;
            } catch (SocketException e) when (e.SocketErrorCode == SocketError.Interrupted) {
                break;
            } catch (ObjectDisposedException) {
                break;
            } catch (ThreadAbortException) {
                // Thread is being forcefully terminated during shutdown - exit gracefully
                Logger.Info("SocketReceiveLoop: Thread aborted during shutdown");
                break;
            } catch (Exception e) {
                Logger.Error($"Unexpected exception in SocketReceiveLoop:\n{e}");
                continue;
            }

            // Validate reception
            if (numReceived < 0) break;
            if (numReceived == 0 || cancellationToken.IsCancellationRequested) continue;

            var ipEndPoint = (IPEndPoint) endPoint;

            // Create a precise copy of the buffer for this packet (required for async processing)
            var packetBuffer = new byte[numReceived];
            Array.Copy(buffer, 0, packetBuffer, 0, numReceived);

            ProcessReceivedPacket(ipEndPoint, packetBuffer, numReceived, cancellationToken);
        }

        ArrayPool<byte>.Shared.Return(buffer);

        Logger.Info("SocketReceiveLoop exited");
    }

    /// <summary>
    /// Process a received packet and route it to the appropriate connection or start a new handshake.
    /// </summary>
    /// <param name="ipEndPoint">The IP endpoint the packet was received from.</param>
    /// <param name="buffer">The buffer containing the packet data.</param>
    /// <param name="numReceived">The number of bytes received.</param>
    /// <param name="cancellationToken">The cancellation token for checking whether this task is requested to cancel.
    /// </param>
    private void ProcessReceivedPacket(
        IPEndPoint ipEndPoint,
        byte[] buffer,
        int numReceived,
        CancellationToken cancellationToken
    ) {
        if (_connections.TryGetValue(ipEndPoint, out var connInfo)) {
            if (TryRouteToExistingConnection(connInfo, ipEndPoint, buffer, numReceived, cancellationToken))
                return;

            // Connection was in a terminal or broken state -> evict and fall through to treat as new
            _connections.TryRemove(ipEndPoint, out _);

            if (connInfo.Client != null)
                Task.Run(() => InternalDisconnectClient(connInfo.Client), cancellationToken);
        }

        StartNewConnection(ipEndPoint, buffer, numReceived, cancellationToken);
    }

    /// <summary>
    /// Attempts to route <paramref name="buffer"/> to <paramref name="connInfo"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the datagram was handled (routed or intentionally dropped);
    /// <see langword="false"/> if the connection is in a terminal state and should be evicted.
    /// </returns>
    private static bool TryRouteToExistingConnection(
        ConnectionInfo connInfo,
        IPEndPoint ipEndPoint,
        byte[] buffer,
        int numReceived,
        CancellationToken cancellationToken
    ) {
        lock (connInfo.SyncRoot) {
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (connInfo.State) {
                case ConnectionState.Handshaking:
                    // Enqueue failure means the transport is gone -> signal eviction.
                    if (connInfo.DatagramTransport.TryEnqueueReceivedData(
                            buffer, numReceived, cancellationToken,
                            $"ProcessReceivedPacket(handshaking, endpoint={ipEndPoint})"
                    )) {
                        return true;
                    }

                    Logger.Warn($"Failed to enqueue datagram for handshaking connection {ipEndPoint}. Evicting.");
                    return false;

                case ConnectionState.Connected:
                    if (!connInfo.DatagramTransport.TryEnqueueReceivedData(
                            buffer, numReceived, cancellationToken,
                            $"ProcessReceivedPacket(connected, endpoint={ipEndPoint})"
                    )) {
                        Logger.Warn($"Failed to enqueue datagram for connected client {ipEndPoint}. Packet dropped.");
                    }

                    // Enqueue failure on a connected client is non-fatal -> keep the connection.
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Creates a new transport and connection entry for <paramref name="ipEndPoint"/> and spawns the handshake task.
    /// Handles the race where another thread registered the same endpoint concurrently.
    /// </summary>
    private void StartNewConnection(
        IPEndPoint ipEndPoint,
        byte[] buffer,
        int numReceived,
        CancellationToken cancellationToken
    ) {
        Logger.Debug($"New endpoint {ipEndPoint} ({numReceived} bytes). Starting handshake.");

        var transport = new ServerDatagramTransport(_socket!) { IPEndPoint = ipEndPoint };
        var newConnInfo = new ConnectionInfo {
            DatagramTransport = transport,
            State = ConnectionState.Handshaking,
            Client = null
        };

        if (!_connections.TryAdd(ipEndPoint, newConnInfo)) {
            // Race: another thread registered this endpoint between our lookup and TryAdd.
            transport.Dispose();
            RouteToRaceWinner(ipEndPoint, buffer, numReceived, cancellationToken);
            return;
        }

        if (!transport.TryEnqueueReceivedData(
            buffer, 
            numReceived, 
            cancellationToken, 
            $"ProcessReceivedPacket(new-connection, endpoint={ipEndPoint})"
        )) {
            Logger.Warn($"Failed to enqueue first datagram for new connection {ipEndPoint}. Aborting handshake.");
            _connections.TryRemove(ipEndPoint, out _);
            transport.Dispose();
            return;
        }

        Task.Factory.StartNew(
            () => PerformHandshake(ipEndPoint, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );
    }

    /// <summary>
    /// After losing the TryAdd race, attempts to route the datagram to whichever connection won.
    /// Packet loss here is acceptable since this is an extremely rare race and DTLS handles retransmission.
    /// </summary>
    private void RouteToRaceWinner(
        IPEndPoint ipEndPoint,
        byte[] buffer,
        int numReceived,
        CancellationToken cancellationToken
    ) {
        if (!_connections.TryGetValue(ipEndPoint, out var winner))
            return;

        lock (winner.SyncRoot) {
            if (winner.State == ConnectionState.Handshaking) {
                winner.DatagramTransport.TryEnqueueReceivedData(
                    buffer, numReceived, cancellationToken,
                    $"ProcessReceivedPacket(race-handshaking, endpoint={ipEndPoint})"
                );
            }
        }
    }

    /// <summary>
    /// Performs the DTLS handshake for a new connection.
    /// </summary>
    /// <param name="endPoint">The IP endpoint of the connecting client.</param>
    /// <param name="cancellationToken">The cancellation token for checking whether this task is requested to cancel.</param>
    private void PerformHandshake(IPEndPoint endPoint, CancellationToken cancellationToken) {
        if (!_connections.TryGetValue(endPoint, out var connInfo)) {
            Logger.Error($"Connection info not found for {endPoint}");
            return;
        }

        Logger.Info($"Starting handshake for {endPoint}");

        DtlsTransport? dtlsTransport = null;
        var handshakeSucceeded = false;

        var serverProtocol = new DtlsServerProtocol();

        try {
            var handshakeTask = Task.Run(
                () => serverProtocol.Accept(_tlsServer, connInfo.DatagramTransport), 
                cancellationToken
            );

            try {
                handshakeTask.Wait(cancellationToken);
                dtlsTransport = handshakeTask.Result;
                handshakeSucceeded = dtlsTransport != null;
            } catch (OperationCanceledException) {
                Logger.Warn($"Handshake cancelled for {endPoint}");
            }

            if (handshakeSucceeded) Logger.Info($"Handshake successful for {endPoint}");
            else Logger.Warn($"Handshake failed (or returned null) for {endPoint}");

        } catch (TlsFatalAlert e) {
            Logger.Warn($"TLS Fatal Alert during handshake with {endPoint}: {e.AlertDescription}");
        } catch (AggregateException ae) {
             // Unwrap AggregateException to check for TLS specific alerts
             foreach (var inner in ae.InnerExceptions) {
                 if (inner is TlsFatalAlert tfa) {
                     Logger.Warn($"TLS Fatal Alert during handshake with {endPoint}: {tfa.AlertDescription}");
                 } else {
                     Logger.Error($"Exception during handshake with {endPoint}: {inner.Message}");
                 }
             }
        } catch (Exception e) {
            Logger.Error($"Exception during handshake with {endPoint}: {e.Message}");
        }

        if (!handshakeSucceeded || dtlsTransport == null || cancellationToken.IsCancellationRequested) {
            CleanupFailedHandshake(endPoint);
            return;
        }

        // Transition to connected state
        lock (connInfo.SyncRoot) {
            if (connInfo.State != ConnectionState.Handshaking) {
                Logger.Warn($"Connection {endPoint} no longer in handshaking state");
                dtlsTransport.Close();
                CleanupFailedHandshake(endPoint);
                return;
            }

            var dtlsServerClient = new DtlsServerClient {
                DtlsTransport = dtlsTransport,
                DatagramTransport = connInfo.DatagramTransport,
                EndPoint = endPoint,
                ReceiveLoopTokenSource = new CancellationTokenSource()
            };

            connInfo.Client = dtlsServerClient;
            connInfo.State = ConnectionState.Connected;


            var receiveThread = new Thread(
                () => ClientReceiveLoop(dtlsServerClient, dtlsServerClient.ReceiveLoopTokenSource.Token)
            ) {
                IsBackground = true
            };
            
            connInfo.ReceiveThread = receiveThread;
            receiveThread.Start();
        }
    }

    /// <summary>
    /// Clean up a failed handshake attempt.
    /// </summary>
    /// <param name="endPoint">The IP endpoint of the failed connection.</param>
    private void CleanupFailedHandshake(IPEndPoint endPoint) {
        if (_connections.TryRemove(endPoint, out var connInfo)) {
            connInfo.DatagramTransport.Close();
        }
    }

    /// <summary>
    /// Start a loop for the given DTLS server client that will continuously check whether new data is available
    /// on the DTLS transport for that client. Will evoke the DataReceivedEvent in case data is received for that
    /// client.
    /// </summary>
    /// <param name="dtlsServerClient">The DTLS server client to receive data for.</param>
    /// <param name="cancellationToken">The cancellation token for checking whether this task is requested to cancel.
    /// </param>
    private void ClientReceiveLoop(DtlsServerClient dtlsServerClient, CancellationToken cancellationToken) {
        var dtlsTransport = dtlsServerClient.DtlsTransport;
        var receiveLimit = dtlsTransport.GetReceiveLimit();
    
        // Use pooled buffer to reduce GC pressure in hot path
        var buffer = ArrayPool<byte>.Shared.Rent(receiveLimit);

        try {
            while (!cancellationToken.IsCancellationRequested) {
                var numReceived = dtlsTransport.Receive(buffer, 0, receiveLimit, 100);

                if (numReceived <= 0) continue;

                // Create a precise copy of the buffer for this packet (required for async processing of the event)
                var packetBuffer = new byte[numReceived];
                Array.Copy(buffer, 0, packetBuffer, 0, numReceived);

                DataReceivedEvent?.Invoke(dtlsServerClient, packetBuffer, numReceived);
            }
        } catch (Exception e) {
            // Log only unexpected errors (receive timeouts are normal)
            if (!cancellationToken.IsCancellationRequested) {
                Logger.Error($"Error in DtlsServer receive loop: {e}");
            }
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    
    /// <summary>
    /// Connection states for tracking client lifecycle.
    /// </summary>
    private enum ConnectionState {
        Handshaking,
        Connected,
        Disconnecting,
        Disconnected
    }

    /// <summary>
    /// Wrapper for connection state management.
    /// </summary>
    private class ConnectionInfo {
        /// <summary>
        /// Private synchronization object for mutating connection state.
        /// </summary>
        public object SyncRoot { get; } = new();

        /// <summary>
        /// The datagram transport for this connection.
        /// </summary>
        public required ServerDatagramTransport DatagramTransport { get; init; }

        /// <summary>
        /// The current state of the connection.
        /// </summary>
        public ConnectionState State { get; set; }
    
        /// <summary>
        /// The DTLS server client instance once the connection is established.
        /// </summary>
        public DtlsServerClient? Client { get; set; }

        /// <summary>
        /// The client receive loop thread.
        /// </summary>
        public Thread? ReceiveThread { get; set; }
    }
}

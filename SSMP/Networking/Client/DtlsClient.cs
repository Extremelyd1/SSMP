using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using SSMP.Logging;

namespace SSMP.Networking.Client;

/// <summary>
/// DTLS implementation for a client-side peer for networking.
/// </summary>
internal class DtlsClient {
    /// <summary>
    /// The maximum packet size for sending and receiving DTLS packets.
    /// </summary>
    public const int MaxPacketSize = 1400;

    /// <summary>
    /// The maximum time the DTLS handshake can take in milliseconds before timing out.
    /// </summary>
    public const int DtlsHandshakeTimeoutMillis = 5000;
    
    /// <summary>
    /// IO Control Code for Connection Reset on socket.
    /// </summary>
    private const int SioUDPConnReset = -1744830452; // 0x9800000C

    /// <summary>
    /// The socket instance for the underlying networking.
    /// </summary>
    private Socket? _socket;
    /// <summary>
    /// The TLS client for communicating supported cipher suites and handling certificates.
    /// </summary>
    private ClientTlsClient? _tlsClient;
    /// <summary>
    /// The client datagram transport that provides networking to the DTLS client.
    /// </summary>
    private ClientDatagramTransport? _clientDatagramTransport;

    /// <summary>
    /// Token source for cancellation tokens for the receive task.
    /// </summary>
    private CancellationTokenSource? _receiveTaskTokenSource;



    /// <summary>
    /// Thread running the handshake operation.
    /// </summary>
    private Thread? _handshakeThread;

    /// <summary>
    /// DTLS transport instance from establishing a connection to a server.
    /// </summary>
    public DtlsTransport? DtlsTransport { get; private set; }
    
    /// <summary>
    /// Event that is called when data is received from the server. 
    /// </summary>
    public event Action<byte[], int>? DataReceivedEvent;

    /// <summary>
    /// Try to establish a connection to a server with the given address and port.
    /// </summary>
    /// <param name="address">The address of the server.</param>
    /// <param name="port">The port of the server.</param>
    /// <param name="boundSocket">Optional pre-bound socket for hole punch scenarios.</param>
    /// <exception cref="SocketException">Thrown when the underlying socket fails to connect to the server.</exception>
    /// <exception cref="IOException">Thrown when the DTLS protocol fails to connect to the server.</exception>
    public void Connect(string address, int port, Socket? boundSocket = null) {
        // Clean up any existing connection first
        if (_receiveTaskTokenSource != null) {
            InternalDisconnect();
        }

        // Use provided socket or create new one
        _socket = boundSocket ?? new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // Prevent UDP WSAECONNRESET (10054) from surfacing as exceptions on Windows when the remote endpoint closes
        try {
            _socket.IOControl((IOControlCode) SioUDPConnReset, [0, 0, 0, 0], null);
        } catch (SocketException e) {
            // IOControl may not be supported on all platforms, log but continue
            Logger.Debug($"IOControl SioUDPConnReset not supported: {e.Message}");
        }

        // Only connect if we created the socket (hole punch sockets are already "connected")
        if (boundSocket is null) {
            try { _socket.Connect(address, port); }
            catch (Exception e) { CleanupAndThrow(e); }
        }

        var clientProtocol = new DtlsClientProtocol();
        _tlsClient = new ClientTlsClient(new BcTlsCrypto());
        _clientDatagramTransport = new ClientDatagramTransport(_socket);

        // Create the token source, because we need the token for the receive loop
        _receiveTaskTokenSource = new CancellationTokenSource();
        var cancellationToken = _receiveTaskTokenSource.Token;

        // Start the socket receive loop, since during the DTLS connection, it needs to receive data
        new Thread(() => SocketReceiveLoop(cancellationToken)) { IsBackground = true }.Start();

        // Perform handshake with timeout
        DtlsTransport? dtlsTransport = null;
        var handshakeSucceeded = false;
        Exception? handshakeException = null;

        _handshakeThread = new Thread(() => {
            try {
                dtlsTransport = clientProtocol.Connect(_tlsClient, _clientDatagramTransport);
                handshakeSucceeded = dtlsTransport != null;
            } catch (Exception e) {
                handshakeException = e;
            }
        }) { IsBackground = true };

        _handshakeThread.Start();

        // Wait for handshake to complete or timeout
        // Time-out of 20s for hole punching
        if (!_handshakeThread.Join(20000)) {
            // Handshake timed out - close socket to force handshake thread to abort
            Logger.Error($"DTLS handshake timed out after 20000ms");
            _socket?.Close();
            
            // Give handshake thread a brief moment to exit after socket closure
            _handshakeThread.Join(500);
            
            CleanupAndThrow(new TlsTimeoutException("DTLS handshake timed out"));
        }

        // Handshake completed - check if it succeeded or threw an exception
        if (handshakeException != null) {
            Logger.Error($"DTLS handshake failed with exception: {handshakeException}");
            CleanupAndThrow(handshakeException is IOException ? handshakeException : new IOException("DTLS handshake failed", handshakeException));
        }

        if (!handshakeSucceeded || dtlsTransport == null) {
            InternalDisconnect();
            throw new IOException("Failed to establish DTLS connection");
        }

        DtlsTransport = dtlsTransport;

        // Start DTLS receive loop
        new Thread(() => DtlsReceiveLoop(cancellationToken)) { IsBackground = true }.Start();
    }

    /// <summary>
    /// Helper method to cleanup resources and throw an exception.
    /// </summary>
    private void CleanupAndThrow(Exception exception) {
        _receiveTaskTokenSource?.Cancel();
        InternalDisconnect();
        throw exception;
    }

    /// <summary>
    /// Disconnect the DTLS client from the server. This will cancel, dispose, or close all internal objects to
    /// clean up potential previous connection attempts.
    /// </summary>
    public void Disconnect() {
        InternalDisconnect();
    }
    

    /// <summary>
    /// Internal disconnect implementation without locking (assumes caller holds lock or is called from Connect).
    /// </summary>
    private void InternalDisconnect() {
        _receiveTaskTokenSource?.Cancel();

        DtlsTransport?.Close();
        DtlsTransport = null;

        _clientDatagramTransport?.Dispose();
        _clientDatagramTransport = null;

        _tlsClient?.Cancel();
        _tlsClient = null;

        _socket?.Close();
        _socket = null;

        _receiveTaskTokenSource?.Dispose();
        _receiveTaskTokenSource = null;

        _handshakeThread = null;
    }

    /// <summary>
    /// Continuously tries to receive data from the socket until cancellation is requested.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to cancel the loop.</param>
    private void SocketReceiveLoop(CancellationToken cancellationToken) {
        if (_socket == null) {
            Logger.Error("Socket was null when starting receive loop");
            return;
        }

        if (_clientDatagramTransport == null) {
            Logger.Error("ClientDatagramTransport was null when starting receive loop");
            return;
        }

        var rentedBuffer = ArrayPool<byte>.Shared.Rent(MaxPacketSize);
        while (!cancellationToken.IsCancellationRequested) {
            EndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
            int numReceived;

            try {
                numReceived = _socket.ReceiveFrom(rentedBuffer, SocketFlags.None, ref endPoint);
            } catch (SocketException e) when (
                e.SocketErrorCode is SocketError.Interrupted or SocketError.ConnectionReset
            ) {
                break;
            } catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut) {
                continue;
            } catch (SocketException e) {
                Logger.Error($"Unexpected socket error in receive loop: {e.SocketErrorCode}");
                break;
            } catch (ObjectDisposedException) {
                break;
            }

            // TODO: If SocketReceiveLoop shows up as an allocation hotspot in profiling, consider reusable buffers
            // TODO: (for example ArrayPool<byte> or an owned-memory pattern). For now we copy into a dedicated array
            // TODO: because BouncyCastle's buffer ownership and lifetime expectations are not explicit enough to safely reuse buffers.
            var packetBuffer = new byte[numReceived];
            Array.Copy(rentedBuffer, 0, packetBuffer, 0, numReceived);

            try {
                var added = _clientDatagramTransport.TryEnqueueReceivedData(
                    packetBuffer,
                    numReceived,
                    cancellationToken
                );

                if (!added) break;
            } catch (OperationCanceledException) {
                break;
            }
        }
        
        ArrayPool<byte>.Shared.Return(rentedBuffer);
    }

    /// <summary>
    /// Continuously tries to receive data from the DTLS transport until cancellation is requested.
    /// </summary>
    private void DtlsReceiveLoop(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested && DtlsTransport != null) {
            var buffer = new byte[MaxPacketSize];
            var length = DtlsTransport.Receive(buffer, 0, buffer.Length, 5);
            if (length >= 0) {
                DataReceivedEvent?.Invoke(buffer, length);
            }
        }
    }
}

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using SSMP.Logging;
using SSMP.Networking.Transport.UDP;

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
    /// <exception cref="SocketException">Thrown when the underlying socket fails to connect to the server.</exception>
    /// <exception cref="IOException">Thrown when the DTLS protocol fails to connect to the server.</exception>
    public void Connect(string address, int port) {
        // Clean up any existing connection first
        if (_receiveTaskTokenSource != null) {
            InternalDisconnect();
        }

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // Prevent UDP WSAECONNRESET (10054) from surfacing as exceptions on Windows when the remote endpoint closes
        try {
            _socket.IOControl((IOControlCode) SioUDPConnReset, [0, 0, 0, 0], null);
        } catch (SocketException e) {
            // IOControl may not be supported on all platforms, log but continue
            Logger.Debug($"IOControl SioUDPConnReset not supported: {e.Message}");
        }

        try {
            _socket.Connect(address, port);
        } catch (SocketException e) {
            Logger.Error($"Failed to connect socket to {address}:{port}");
            CleanupAndThrow(e);
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
        if (!_handshakeThread.Join(DtlsHandshakeTimeoutMillis)) {
            // Handshake timed out - close socket to force handshake thread to abort
            Logger.Error($"DTLS handshake timed out after {DtlsHandshakeTimeoutMillis}ms");
            _socket?.Close();
            
            // Give handshake thread a brief moment to exit after socket closure
            _handshakeThread.Join(500);
            
            CleanupAndThrow(new TlsTimeoutException("DTLS handshake timed out"));
        }

        // Handshake completed - check if it succeeded or threw an exception
        if (handshakeException != null) {
            Logger.Error($"DTLS handshake failed with exception");
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
        while (!cancellationToken.IsCancellationRequested) {
            if (_socket == null) {
                Logger.Error("Socket was null during receive call");
                break;
            }

            EndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);

            int numReceived;
            var buffer = new byte[MaxPacketSize];

            try {
                numReceived = _socket.ReceiveFrom(
                    buffer,
                    SocketFlags.None,
                    ref endPoint
                );
            } catch (SocketException e) when (e.SocketErrorCode == SocketError.Interrupted ||
                                              e.SocketErrorCode == SocketError.ConnectionReset) {
                break;
            } catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut) {
                continue;
            } catch (ObjectDisposedException) {
                break;
            }

            if (_clientDatagramTransport == null) {
                break;
            }

            // Create a copy of the buffer for this specific packet. The original buffer will be reused in the next iteration
            var packetBuffer = new byte[numReceived];
            Buffer.BlockCopy(buffer, 0, packetBuffer, 0, numReceived);

            var added = false;
            try {
                // Use the copy, not the original buffer
                _clientDatagramTransport.ReceivedDataCollection.Add(new UdpDatagramTransport.ReceivedData {
                    Buffer = packetBuffer,
                    Length = numReceived
                }, cancellationToken);
                added = true;
            } catch (OperationCanceledException) {
                // Expected during disconnect
            } catch (InvalidOperationException) {
                // Collection might be marked as complete for adding
            }

            // Collection disposed, completed, or cancelled
            if (!added) break;
        }
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

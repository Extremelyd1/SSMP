using System;

namespace SSMP.Networking.Transport.Common;

/// <summary>
/// Interface for a client-side encrypted transport for connection and data exchange with a server.
/// </summary>
internal interface IEncryptedTransport {
    /// <summary>
    /// Event raised when data is received from the server.
    /// </summary>
    event Action<byte[], int>? DataReceivedEvent;
    
    /// <summary>
    /// Connect to remote peer.
    /// </summary>
    /// <param name="address">Address of the remote peer.</param>
    /// <param name="port">Port of the remote peer.</param>
    void Connect(string address, int port);
    
    /// <summary>
    /// Send data to the server.
    /// </summary>
    /// <param name="buffer">The byte array buffer containing the data.</param>
    /// <param name="offset">The offset in the buffer to start sending from.</param>
    /// <param name="length">The number of bytes to send from the buffer.</param>
    void Send(byte[] buffer, int offset, int length);

    /// <summary>
    /// Indicates whether this transport requires application-level congestion management.
    /// Returns false for transports with built-in congestion handling (e.g., Steam P2P).
    /// </summary>
    bool RequiresCongestionManagement { get; }

    /// <summary>
    /// Disconnect from the remote peer.
    /// </summary>
    void Disconnect();
}

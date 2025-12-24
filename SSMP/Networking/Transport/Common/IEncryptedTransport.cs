using System;

namespace SSMP.Networking.Transport.Common;

/// <summary>
/// Base interface defining transport capabilities and wire semantics.
/// Both client and server transports share these properties.
/// </summary>
internal interface IEncryptedTransport {
    /// <summary>
    /// Event raised when data is received from the remote peer.
    /// </summary>
    event Action<byte[], int>? DataReceivedEvent;

    /// <summary>
    /// Connect to remote peer.
    /// </summary>
    /// <param name="address">Address of the remote peer.</param>
    /// <param name="port">Port of the remote peer.</param>
    void Connect(string address, int port);

    /// <summary>
    /// Send data to the remote peer.
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
    /// Indicates whether the application must handle reliability (retransmission).
    /// Returns false for transports with built-in reliable delivery (e.g., Steam P2P).
    /// </summary>
    bool RequiresReliability { get; }

    /// <summary>
    /// Indicates whether the application must handle packet sequencing.
    /// Returns false for transports with built-in ordering (e.g., Steam P2P).
    /// </summary>
    bool RequiresSequencing { get; }

    /// <summary>
    /// Maximum packet size supported by this transport in bytes.
    /// Used for MTU-based fragmentation decisions.
    /// </summary>
    int MaxPacketSize { get; }

    /// <summary>
    /// Disconnect from the remote peer.
    /// </summary>
    void Disconnect();
}

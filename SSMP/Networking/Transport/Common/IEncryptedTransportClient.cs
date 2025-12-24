using System;
using System.Net;

namespace SSMP.Networking.Transport.Common;

/// <summary>
/// Interface for a server-side encrypted transport client that is connected to the server.
/// </summary>
internal interface IEncryptedTransportClient {
    /// <summary>
    /// Returns a human-readable string representation for logging and display.
    /// </summary>
    string ToDisplayString();

    /// <summary>
    /// Returns a unique identifier for this client (e.g., Steam ID or IP address).
    /// </summary>
    string GetUniqueIdentifier();

    /// <summary>
    /// Gets the endpoint used for throttling connection attempts.
    /// Returns null if application-level throttling should be skipped for this client (e.g., Steam).
    /// </summary>
    IPEndPoint? EndPoint { get; }

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
    /// Event raised when data is received from this client.
    /// </summary>
    event Action<byte[], int>? DataReceivedEvent;

    /// <summary>
    /// Send data to this client.
    /// </summary>
    /// <param name="buffer">The byte array buffer containing the data.</param>
    /// <param name="offset">The offset in the buffer to start sending from.</param>
    /// <param name="length">The number of bytes to send from the buffer.</param>
    void Send(byte[] buffer, int offset, int length);
}

using System;

namespace SSMP.Networking.Transport.Common;

/// <summary>
/// Interface for a server-side encrypted transport client that is connected to the server.
/// </summary>
internal interface IEncryptedTransportClient {
    /// <summary>
    /// Unique identifier for the client (e.g. IP address, SteamID).
    /// </summary>
    string ClientIdentifier { get; }

    /// <summary>
    /// Event raised when data is received from this client.
    /// </summary>
    event Action<byte[], int>? DataReceivedEvent;

    /// <summary>
    /// Send data to this client.
    /// </summary>
    /// <param name="buffer">Buffer containing the data to send.</param>
    /// <param name="offset">Offset in the buffer to start sending from.</param>
    /// <param name="length">Number of bytes to send.</param>
    void Send(byte[] buffer, int offset, int length);
}

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
    /// Send data to the connected peer.
    /// </summary>
    /// <param name="buffer">Buffer containing the data to send.</param>
    /// <param name="offset">Offset in the buffer to start sending from.</param>
    /// <param name="length">Number of bytes to send.</param>
    void Send(byte[] buffer, int offset, int length);

    /// <summary>
    /// Receive data from the connected peer.
    /// </summary>
    /// <param name="buffer">Buffer to store received data.</param>
    /// <param name="offset">Offset in the buffer to start storing data.</param>
    /// <param name="length">Maximum number of bytes to receive.</param>
    /// <param name="waitMillis">Time in milliseconds to wait for data.</param>
    /// <returns>Number of bytes received.</returns>
    int Receive(byte[] buffer, int offset, int length, int waitMillis);

    /// <summary>
    /// Disconnect from the remote peer.
    /// </summary>
    void Disconnect();
}

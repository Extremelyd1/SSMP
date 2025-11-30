namespace SSMP.Networking.Transport.Common;

/// <summary>
/// Minimal interface for sending data over a transport.
/// Both <see cref="IEncryptedTransport"/> and <see cref="IEncryptedTransportClient"/> implement this.
/// </summary>
internal interface ITransportSender {
    /// <summary>
    /// Send data.
    /// </summary>
    /// <param name="buffer">Buffer containing the data to send.</param>
    /// <param name="offset">Offset in the buffer to start sending from.</param>
    /// <param name="length">Number of bytes to send.</param>
    void Send(byte[] buffer, int offset, int length);
}

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
    /// <param name="reliable">Whether the packet should be sent reliably (if supported by transport).</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="buffer"/> is <c>null</c>.</exception>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// Thrown if <paramref name="offset"/> or <paramref name="length"/> is negative,
    /// or if <paramref name="offset"/> + <paramref name="length"/> exceeds the length of <paramref name="buffer"/>.
    /// </exception>
    void Send(byte[] buffer, int offset, int length, bool reliable = false);
}

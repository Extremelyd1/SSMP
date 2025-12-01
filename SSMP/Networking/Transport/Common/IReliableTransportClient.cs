namespace SSMP.Networking.Transport.Common;

/// <summary>
/// Interface for a transport client that supports reliable packet sending.
/// </summary>
internal interface IReliableTransportClient : IEncryptedTransportClient {
    /// <summary>
    /// Send data to this client reliably.
    /// </summary>
    /// <param name="buffer">The byte array buffer containing the data.</param>
    /// <param name="offset">The offset in the buffer to start sending from.</param>
    /// <param name="length">The number of bytes to send from the buffer.</param>
    void SendReliable(byte[] buffer, int offset, int length);
}

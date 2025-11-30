namespace SSMP.Networking.Transport.Common;

/// <summary>
/// Minimal interface for sending data over a transport.
/// Both <see cref="IEncryptedTransport"/> and <see cref="IEncryptedTransportClient"/> implement this.
/// </summary>
internal interface ITransportSender {
    /// <summary>
    /// Send a packet.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    void Send(Packet.Packet packet);
}

using System.Net;
using System.Threading;
using Org.BouncyCastle.Tls;

namespace SSMP.Networking.Server;

/// <summary>
/// Data class containing the related object instances for a DTLS server client.
/// </summary>
internal class DtlsServerClient {
    /// <summary>
    /// The DTLS transport instance.
    /// </summary>
    public required DtlsTransport DtlsTransport { get; init; }
    /// <summary>
    /// The server datagram transport. 
    /// </summary>
    public required ServerDatagramTransport DatagramTransport { get; init; }
    /// <summary>
    /// The IP endpoint of the client.
    /// </summary>
    public required IPEndPoint EndPoint { get; init; }

    /// <summary>
    /// The cancellation token source for the "receive loop".
    /// </summary>
    public required CancellationTokenSource ReceiveLoopTokenSource { get; init; }
}

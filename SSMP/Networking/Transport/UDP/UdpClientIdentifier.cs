using System;
using System.Net;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.UDP;

/// <summary>
/// UDP-specific implementation of <see cref="IClientIdentifier"/> that wraps an <see cref="IPEndPoint"/>.
/// </summary>
internal class UdpClientIdentifier : IClientIdentifier {
    /// <summary>
    /// The underlying IP endpoint for this UDP client.
    /// </summary>
    public IPEndPoint EndPoint { get; }

    /// <summary>
    /// Constructs a new UDP client identifier from an IP endpoint.
    /// </summary>
    /// <param name="endPoint">The IP endpoint representing this client.</param>
    /// <exception cref="ArgumentNullException">Thrown if endPoint is null.</exception>
    public UdpClientIdentifier(IPEndPoint endPoint) {
        EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
    }

    /// <inheritdoc />
    public string ToDisplayString() => EndPoint.ToString();

    /// <inheritdoc />
    public object? ThrottleKey => EndPoint.Address;

    /// <inheritdoc />
    public bool Equals(IClientIdentifier? other) {
        return other is UdpClientIdentifier udp && EndPoint.Equals(udp.EndPoint);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as IClientIdentifier);

    /// <inheritdoc />
    public override int GetHashCode() => EndPoint.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => ToDisplayString();
}

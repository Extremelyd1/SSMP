using System;
using System.Net;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.HolePunch;

/// <summary>
/// Hole-punching implementation of <see cref="IClientIdentifier"/> that wraps an <see cref="IPEndPoint"/>.
/// After NAT traversal via the Master Server, hole-punched connections use standard IP endpoints.
/// </summary>
internal class HolePunchClientIdentifier : IClientIdentifier {
    /// <summary>
    /// The underlying IP endpoint for this hole-punched client connection.
    /// </summary>
    public IPEndPoint EndPoint { get; }

    /// <summary>
    /// Constructs a new hole-punch client identifier from an IP endpoint.
    /// </summary>
    /// <param name="endPoint">The IP endpoint representing this client after NAT traversal.</param>
    /// <exception cref="ArgumentNullException">Thrown if endPoint is null.</exception>
    public HolePunchClientIdentifier(IPEndPoint endPoint) {
        EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
    }

    /// <inheritdoc />
    public string ToDisplayString() => $"HolePunch:{EndPoint}";

    /// <inheritdoc />
    public object? ThrottleKey => EndPoint.Address;
    
    /// <inheritdoc />
    public bool NeedsCongestionManagement => true;

    /// <inheritdoc />
    public bool Equals(IClientIdentifier? other) {
        return other is HolePunchClientIdentifier holePunch && EndPoint.Equals(holePunch.EndPoint);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as IClientIdentifier);

    /// <inheritdoc />
    public override int GetHashCode() => EndPoint.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => ToDisplayString();
}

using System.Net.Sockets;

namespace SSMP.Networking.Matchmaking;

/// <summary>
/// Holds a pre-bound socket that was used for STUN discovery,
/// so the HolePunch transport can reuse it for the connection.
/// </summary>
internal static class ClientSocketHolder {
    /// <summary>
    /// The socket used for STUN discovery. Must be set before connecting.
    /// Will be null'd after being consumed by the transport.
    /// </summary>
    public static Socket? PreBoundSocket { get; set; }
}

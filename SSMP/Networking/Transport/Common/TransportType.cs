namespace SSMP.Networking.Transport.Common;

/// <summary>
/// Enum representing the type of transport to use.
/// </summary>
public enum TransportType {
    /// <summary>
    /// UDP transport (Direct Connect).
    /// </summary>
    Udp,

    /// <summary>
    /// Steam P2P transport (Lobby).
    /// </summary>
    Steam,

    /// <summary>
    /// UDP Hole Punch transport (NAT traversal).
    /// </summary>
    HolePunch
}

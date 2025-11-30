using System;

namespace SSMP.Networking.Transport.Common;

/// <summary>
/// Interface for a server-side encrypted transport client that is connected to the server.
/// </summary>
internal interface IEncryptedTransportClient : ITransportSender {
    /// <summary>
    /// Unique identifier for the client.
    /// Implementation depends on transport type:
    /// - UDP: <see cref="UDP.UdpClientIdentifier"/> wrapping <see cref="System.Net.IPEndPoint"/>
    /// - Steam P2P: <see cref="SteamP2P.SteamClientIdentifier"/> wrapping Steam ID (ulong)
    /// - Hole Punch: <see cref="HolePunch.HolePunchClientIdentifier"/> wrapping <see cref="System.Net.IPEndPoint"/>
    /// </summary>
    IClientIdentifier ClientIdentifier { get; }

    /// <summary>
    /// Event raised when data is received from this client.
    /// </summary>
    event Action<byte[], int>? DataReceivedEvent;

    /// <summary>
    /// Send a packet to this client.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    new void Send(Packet.Packet packet);
}

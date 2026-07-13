using System;
using SSMP.Collection;

namespace SSMP.Api.Networking;

/// <summary>
/// Static class for addon network transmitters.
/// </summary>
internal static class AddonNetworkTransmitter {
    /// <summary>
    /// Construct a packet ID lookup given the generic type.
    /// </summary>
    /// <typeparam name="T">The type parameter to construct the lookup with. This should extend the
    /// enum class</typeparam>
    /// <returns>A bi-directional lookup from the generic type to a byte value.</returns>
    public static BiLookup<T, byte> ConstructPacketIdLookup<T>() where T : Enum {
        var packetIdLookup = new BiLookup<T, byte>();

        // We add an entry in the dictionary for each value, so that we have
        // bytes 0, 1, 2, ..., n
        var packetIdValues = Enum.GetValues(typeof(T));
        for (byte i = 0; i < packetIdValues.Length; i++) {
            var packetId = (T) packetIdValues.GetValue(i);

            packetIdLookup.Add(packetId, i);
        }

        return packetIdLookup;
    }
}

/// <summary>
/// Abstract base class for classes that transmit (send/receive) over the network.
/// </summary>
/// <typeparam name="TPacketId">The type of the packet ID enum.</typeparam>
internal abstract class AddonNetworkTransmitter<TPacketId> where TPacketId : Enum {
    /// <summary>
    /// A lookup for packet IDs and corresponding raw byte values.
    /// </summary>
    protected readonly BiLookup<TPacketId, byte> PacketIdLookup =
        AddonNetworkTransmitter.ConstructPacketIdLookup<TPacketId>();

    /// <summary>
    /// Resolve the given addon packet ID to its wire-format byte value.
    /// </summary>
    /// <param name="packetId">The enum packet ID to resolve.</param>
    /// <param name="invalidPacketIdMessage">Exception message used when the packet ID is unknown.</param>
    /// <returns>The byte value for the packet ID.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the packet ID is not part of the lookup.</exception>
    protected byte ResolvePacketId(TPacketId packetId, string invalidPacketIdMessage) {
        return !PacketIdLookup.TryGetValue(packetId, out var idValue)
            ? throw new InvalidOperationException(invalidPacketIdMessage)
            : idValue;
    }
}

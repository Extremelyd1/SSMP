using System;
using SSMP.Networking.Packet.Connection;
using SSMP.Networking.Packet.Data;

namespace SSMP.Networking.Packet;

/// <summary>
/// Builds the connection-packet envelopes used for large chunked addon payloads.
/// </summary>
internal static class ChunkAddonPacketBuilder {
    /// <summary>
    /// Build a chunked addon payload destined for a client.
    /// </summary>
    public static Packet BuildClientBound(byte packetId, byte addonId, IPacketData packetData) {
        return BuildPacket(
            new ClientConnectionPacket(),
            ClientConnectionPacketId.ChunkAddonData,
            packetId,
            addonId,
            packetData
        );
    }

    /// <summary>
    /// Build a chunked addon payload destined for the server.
    /// </summary>
    public static Packet BuildServerBound(byte packetId, byte addonId, IPacketData packetData) {
        return BuildPacket(
            new ServerConnectionPacket(),
            ServerConnectionPacketId.ChunkAddonData,
            packetId,
            addonId,
            packetData
        );
    }

    /// <summary>
    /// Build a chunk-transport connection packet that wraps addon payload data in a
    /// <see cref="ChunkAddonData"/> envelope.
    /// </summary>
    /// <param name="connectionPacket">
    /// The outer connection packet instance used to serialize the chunk addon envelope.
    /// </param>
    /// <param name="chunkAddonPacketId">
    /// The connection-packet ID that identifies the payload as <see cref="ChunkAddonData"/>.
    /// </param>
    /// <param name="packetId">The addon-local packet ID to embed in the envelope.</param>
    /// <param name="addonId">The addon ID that owns the packet payload.</param>
    /// <param name="packetData">The addon payload to serialize and wrap.</param>
    /// <returns>
    /// A serialized packet ready for chunk transport, guaranteed to be larger than
    /// <see cref="ushort.MaxValue"/> and no larger than <see cref="ConnectionManager.MaxChunkSize"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the serialized packet exceeds <see cref="ConnectionManager.MaxChunkSize"/> or when
    /// the payload is small enough to fit in the standard non-chunk addon transport.
    /// </exception>
    private static Packet BuildPacket<TPacketId>(
        BasePacket<TPacketId> connectionPacket,
        TPacketId chunkAddonPacketId,
        byte packetId,
        byte addonId,
        IPacketData packetData
    ) where TPacketId : Enum {
        // TODO: Revisit this path in a dedicated feature branch and remove the intermediate payload array copy.
        var payloadPacket = new Packet();
        packetData.WriteData(payloadPacket);

        connectionPacket.SetSendingPacketData(
            chunkAddonPacketId,
            new ChunkAddonData {
                AddonId = addonId,
                PacketId = packetId,
                Payload = payloadPacket.ToArray()
            }
        );

        var packet = new Packet();
        connectionPacket.CreatePacket(packet);

        return packet.Length switch {
            > ConnectionManager.MaxChunkSize => throw new ArgumentException(
                $"Addon packet data size ({packet.Length} bytes) exceeds the maximum chunk size ({ConnectionManager.MaxChunkSize} bytes).",
                nameof(packetData)
            ),
            <= ushort.MaxValue => throw new ArgumentException(
                $"Addon packet data size ({packet.Length} bytes) is not larger than ushort.MaxValue ({ushort.MaxValue}). " +
                "For payloads smaller than or equal to ushort.MaxValue, please use standard updates instead of chunk transport.",
                nameof(packetData)
            ),
            _ => packet
        };
    }
}

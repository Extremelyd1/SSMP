using System;
using SSMP.Networking.Client;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Connection;

namespace SSMP.Api.Client.Networking;

/// <summary>
/// Implementation of client-side network sender for addons.
/// </summary>
/// <typeparam name="TPacketId">The type of the packet ID enum.</typeparam>
internal class ClientAddonNetworkSender<TPacketId> :
    AddonNetworkTransmitter<TPacketId>,
    IClientAddonNetworkSender<TPacketId>
    where TPacketId : Enum {
    /// <summary>
    /// Message for the exception when the client is not connected.
    /// </summary>
    private const string NotConnectedMsg = "NetClient is not connected, cannot send data";

    /// <summary>
    /// Message for the exception when the given packet ID is invalid.
    /// </summary>
    private const string InvalidPacketIdMsg =
        "Given packet ID was not part of enum when creating this network sender";

    /// <summary>
    /// Message for the exception when the client addon has no ID.
    /// </summary>
    private const string NoClientAddonId = "Cannot send data when client addon has no ID";

    /// <summary>
    /// The net client used to send data.
    /// </summary>
    private readonly NetClient _netClient;

    /// <summary>
    /// The instance of the client addon that this network sender belongs to.
    /// </summary>
    private readonly ClientAddon _clientAddon;

    /// <summary>
    /// The size of the packet ID space.
    /// </summary>
    private readonly byte _packetIdSize;

    public ClientAddonNetworkSender(
        NetClient netClient,
        ClientAddon clientAddon
    ) {
        _netClient = netClient;
        _clientAddon = clientAddon;

        _packetIdSize = (byte) PacketIdLookup.Count;
    }

    /// <inheritdoc/>
    public void SendSingleData(TPacketId packetId, IPacketData packetData) {
        if (!_netClient.IsConnected) {
            throw new InvalidOperationException(NotConnectedMsg);
        }

        if (!PacketIdLookup.TryGetValue(packetId, out var idValue)) {
            throw new InvalidOperationException(
                InvalidPacketIdMsg
            );
        }

        if (!_clientAddon.Id.HasValue) {
            throw new InvalidOperationException(NoClientAddonId);
        }

        _netClient.UpdateManager.SetAddonData(
            _clientAddon.Id.Value,
            idValue,
            _packetIdSize,
            packetData
        );
    }

    /// <inheritdoc/>
    public void SendCollectionData<TPacketData>(
        TPacketId packetId,
        TPacketData packetData
    ) where TPacketData : IPacketData, new() {
        if (!_netClient.IsConnected) {
            throw new InvalidOperationException(NotConnectedMsg);
        }

        if (!PacketIdLookup.TryGetValue(packetId, out var idValue)) {
            throw new InvalidOperationException(
                InvalidPacketIdMsg
            );
        }

        if (!_clientAddon.Id.HasValue) {
            throw new InvalidOperationException(NoClientAddonId);
        }

        _netClient.UpdateManager.SetAddonDataAsCollection(
            _clientAddon.Id.Value,
            idValue,
            _packetIdSize,
            packetData
        );
    }

    /// <inheritdoc/>
    public void SendChunkData(TPacketId packetId, IPacketData packetData) {
        var (idValue, addonId) = ValidateCommon(packetId);
        _netClient.UpdateManager.SendChunkPacket(BuildPacket(idValue, addonId, packetData));
    }

    /// <summary>
    /// Validates the common client-side preconditions required before sending chunk data.
    /// </summary>
    /// <param name="packetId">The addon packet identifier to validate and resolve.</param>
    /// <returns>
    /// The resolved packet ID byte value and the current addon ID.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the client is not connected, the addon has no assigned ID, or the packet ID is invalid.
    /// </exception>
    private (byte idValue, byte addonId) ValidateCommon(TPacketId packetId) {
        if (!_netClient.IsConnected) {
            throw new InvalidOperationException(NotConnectedMsg);
        }

        if (!_clientAddon.Id.HasValue) {
            throw new InvalidOperationException(NoClientAddonId);
        }

        return !PacketIdLookup.TryGetValue(packetId, out var idValue)
            ? throw new InvalidOperationException(InvalidPacketIdMsg)
            : (idValue, _clientAddon.Id.Value);
    }

    /// <summary>
    /// Builds a network packet containing addon packet data for the given addon and packet ID.
    /// </summary>
    /// <param name="idValue">The resolved packet ID byte value.</param>
    /// <param name="addonId">The addon ID that owns the packet data.</param>
    /// <param name="packetData">The packet payload to send.</param>
    /// <returns>
    /// A constructed packet ready to be enqueued for chunked sending.
    /// </returns>
    private Packet BuildPacket(byte idValue, byte addonId, IPacketData packetData) {
        var connectionPacket = new ServerConnectionPacket();
        var addonPacketData = new AddonPacketData(_packetIdSize) {
            PacketData = { [idValue] = packetData }
        };

        connectionPacket.SetSendingAddonPacketData(addonId, addonPacketData);

        var packet = new Packet();
        connectionPacket.CreatePacket(packet);

        if (packet.Length <= ushort.MaxValue) {
            throw new ArgumentException(
                $"Addon packet data size ({packet.Length} bytes) is not larger than ushort.MaxValue ({ushort.MaxValue}). " +
                $"For payloads smaller than or equal to ushort.MaxValue, please use standard updates instead of chunk transport.",
                nameof(packetData)
            );
        }

        return packet;
    }
}

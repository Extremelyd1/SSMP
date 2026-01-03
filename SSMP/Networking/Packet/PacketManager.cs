using System;
using System.Collections.Generic;
using SSMP.Logging;
using SSMP.Networking.Packet.Connection;
using SSMP.Networking.Packet.Data;
using SSMP.Networking.Packet.Update;
using SSMP.Util;

namespace SSMP.Networking.Packet;

/// <summary>
/// Delegate for client packet handlers.
/// </summary>
internal delegate void ClientPacketHandler(IPacketData packet);

/// <summary>
/// Generic client packet handler delegate that has a IPacketData implementation as parameter.
/// </summary>
/// <typeparam name="TPacketData">The type of the packet data that is passed as parameter.</typeparam>
public delegate void GenericClientPacketHandler<in TPacketData>(TPacketData packet) where TPacketData : IPacketData;

/// <summary>
/// Packet handler that only has the client ID as parameter and does not use the packet data.
/// </summary>
internal delegate void EmptyServerPacketHandler(ushort id);

/// <summary>
/// Packet handler for the server that has the client ID and packet data as parameters.
/// </summary>
internal delegate void ServerPacketHandler(ushort id, IPacketData packet);

/// <summary>
/// Generic server packet handler delegate that has a IPacketData implementation and client ID as parameter.
/// </summary>
/// <typeparam name="TPacketData">The type of the packet data that is passed as parameter.</typeparam>
public delegate void GenericServerPacketHandler<in TPacketData>(ushort id, TPacketData packet)
    where TPacketData : IPacketData;

/// <summary>
/// Manages packets that are received by the given NetClient.
/// </summary>
internal class PacketManager {
    // --- Constants ---

    /// <summary>
    /// Maximum allowed packet size (10MB).
    /// Used to prevent allocation attacks or processing of corrupted large packets.
    /// </summary>
    private const int MaxPacketSize = 10 * 1024 * 1024;
    
    // --- Standard Packet Registries ---

    private readonly PacketHandlerRegistry<ClientUpdatePacketId, ClientPacketHandler> _clientUpdateRegistry = new(
        "client update", true
    );
    private readonly PacketHandlerRegistry<ClientConnectionPacketId, ClientPacketHandler> _clientConnectionRegistry = new(
        "client connection", true
    );
    private readonly PacketHandlerRegistry<ServerUpdatePacketId, ServerPacketHandler> _serverUpdateRegistry = new(
        "server update", false
    );
    private readonly PacketHandlerRegistry<ServerConnectionPacketId, ServerPacketHandler> _serverConnectionRegistry = new(
        "server connection", false
    );

    // --- Addon Packet Registries (Nested Dictionaries) ---

    // Addon packet handlers are organized as a two-level structure:
    //  - The outer Dictionary is keyed by addonId (byte).
    //  - For each addonId, a PacketHandlerRegistry<byte, ...> is used as the inner registry,
    //    keyed by the addon-specific packetId (byte).
    // This preserves the (addonId, packetId) addressing model while reusing PacketHandlerRegistry
    // for handler management within each addon.

    private readonly Dictionary<byte, PacketHandlerRegistry<byte, ClientPacketHandler>> _clientAddonUpdateRegistries = new();

    private readonly Dictionary<byte, PacketHandlerRegistry<byte, ClientPacketHandler>>
        _clientAddonConnectionRegistries = new();

    private readonly Dictionary<byte, PacketHandlerRegistry<byte, ServerPacketHandler>> _serverAddonUpdateRegistries = new();

    private readonly Dictionary<byte, PacketHandlerRegistry<byte, ServerPacketHandler>>
        _serverAddonConnectionRegistries = new();

    // Initialize Registries
    // Client handlers dispatch to main thread (true)
    // Server handlers run on unknown threads (false)

    #region Packet Unpacking Helper

    /// <summary>
    /// Unpacks a dictionary of packet data and executes the given action for each.
    /// </summary>
    /// <param name="packetDataDict">The dictionary of packet data.</param>
    /// <param name="handlerAction">The action to execute for each packet data.</param>
    /// <typeparam name="TPacketId">The enum type of the packet ID.</typeparam>
    private static void UnpackPacketDataDict<TPacketId>(
        Dictionary<TPacketId, IPacketData> packetDataDict,
        Action<TPacketId, IPacketData> handlerAction
    ) where TPacketId : notnull {
        foreach (var packetDataPair in packetDataDict) {
            var packetId = packetDataPair.Key;
            var packetData = packetDataPair.Value;

            // Check if this is a collection and if so, execute the handler for each instance in it
            if (packetData is RawPacketDataCollection rawPacketDataCollection) {
                foreach (var dataInstance in rawPacketDataCollection.DataInstances) {
                    handlerAction(packetId, dataInstance);
                }
            } else {
                handlerAction(packetId, packetData);
            }
        }
    }

    #endregion

    #region Client-related update packet handling

    public void HandleClientUpdatePacket(ClientUpdatePacket packet) {
        UnpackPacketDataDict(
            packet.GetPacketData(), (id, data) =>
                _clientUpdateRegistry.Execute(id, handler => handler(data))
        );

        foreach (var pair in packet.GetAddonPacketData()) {
            HandleClientAddonPacket(
                pair.Key, pair.Value.PacketData, _clientAddonUpdateRegistries, "client addon update"
            );
        }
    }

    private void RegisterClientUpdatePacketHandler(ClientUpdatePacketId packetId, ClientPacketHandler handler) =>
        _clientUpdateRegistry.Register(packetId, handler);

    public void RegisterClientUpdatePacketHandler(ClientUpdatePacketId packetId, Action handler) =>
        RegisterClientUpdatePacketHandler(packetId, _ => handler());

    public void RegisterClientUpdatePacketHandler<T>(
        ClientUpdatePacketId packetId,
        GenericClientPacketHandler<T> handler
    )
        where T : IPacketData => RegisterClientUpdatePacketHandler(packetId, iPacket => handler((T) iPacket));

    public void DeregisterClientUpdatePacketHandler(ClientUpdatePacketId packetId) =>
        _clientUpdateRegistry.Deregister(packetId);

    #endregion

    #region Client-related connection packet handling

    public void HandleClientConnectionPacket(ClientConnectionPacket packet) {
        UnpackPacketDataDict(
            packet.GetPacketData(), (id, data) =>
                _clientConnectionRegistry.Execute(id, handler => handler(data))
        );

        foreach (var pair in packet.GetAddonPacketData()) {
            HandleClientAddonPacket(
                pair.Key, pair.Value.PacketData, _clientAddonConnectionRegistries, "client addon connection"
            );
        }
    }

    private void RegisterClientConnectionPacketHandler(ClientConnectionPacketId packetId, ClientPacketHandler handler) =>
        _clientConnectionRegistry.Register(packetId, handler);

    public void RegisterClientConnectionPacketHandler(ClientConnectionPacketId packetId, Action handler) =>
        RegisterClientConnectionPacketHandler(packetId, _ => handler());

    public void RegisterClientConnectionPacketHandler<T>(
        ClientConnectionPacketId packetId,
        GenericClientPacketHandler<T> handler
    )
        where T : IPacketData => RegisterClientConnectionPacketHandler(packetId, iPacket => handler((T) iPacket));

    public void DeregisterClientConnectionPacketHandler(ClientConnectionPacketId packetId) =>
        _clientConnectionRegistry.Deregister(packetId);

    #endregion

    #region Server-related update packet handling

    public void HandleServerUpdatePacket(ushort id, ServerUpdatePacket packet) {
        UnpackPacketDataDict(
            packet.GetPacketData(), (packetId, data) =>
                _serverUpdateRegistry.Execute(packetId, handler => handler(id, data))
        );

        foreach (var pair in packet.GetAddonPacketData()) {
            HandleServerAddonPacket(
                id, pair.Key, pair.Value.PacketData, _serverAddonUpdateRegistries, "server addon update"
            );
        }
    }

    private void RegisterServerUpdatePacketHandler(ServerUpdatePacketId packetId, ServerPacketHandler handler) =>
        _serverUpdateRegistry.Register(packetId, handler);

    public void RegisterServerUpdatePacketHandler(ServerUpdatePacketId packetId, EmptyServerPacketHandler handler) =>
        RegisterServerUpdatePacketHandler(packetId, (id, _) => handler(id));

    public void RegisterServerUpdatePacketHandler<T>(
        ServerUpdatePacketId packetId,
        GenericServerPacketHandler<T> handler
    )
        where T : IPacketData => RegisterServerUpdatePacketHandler(packetId, (id, iPacket) => handler(id, (T) iPacket));

    public void DeregisterServerUpdatePacketHandler(ServerUpdatePacketId packetId) =>
        _serverUpdateRegistry.Deregister(packetId);

    #endregion

    #region Server-related connection packet handling

    public void HandleServerConnectionPacket(ushort id, ServerConnectionPacket packet) {
        UnpackPacketDataDict(
            packet.GetPacketData(), (packetId, data) =>
                _serverConnectionRegistry.Execute(packetId, handler => handler(id, data))
        );

        foreach (var pair in packet.GetAddonPacketData()) {
            HandleServerAddonPacket(
                id, pair.Key, pair.Value.PacketData, _serverAddonConnectionRegistries, "server addon connection"
            );
        }
    }

    private void RegisterServerConnectionPacketHandler(ServerConnectionPacketId packetId, ServerPacketHandler handler) =>
        _serverConnectionRegistry.Register(packetId, handler);

    public void RegisterServerConnectionPacketHandler(
        ServerConnectionPacketId packetId,
        EmptyServerPacketHandler handler
    ) =>
        RegisterServerConnectionPacketHandler(packetId, (id, _) => handler(id));

    public void RegisterServerConnectionPacketHandler<T>(
        ServerConnectionPacketId packetId,
        GenericServerPacketHandler<T> handler
    )
        where T : IPacketData =>
        RegisterServerConnectionPacketHandler(packetId, (id, iPacket) => handler(id, (T) iPacket));

    public void DeregisterServerConnectionPacketHandler(ServerConnectionPacketId packetId) =>
        _serverConnectionRegistry.Deregister(packetId);

    #endregion

    #region Client Addon Helpers

    private static void HandleClientAddonPacket(
        byte addonId,
        Dictionary<byte, IPacketData> packetDataDict,
        Dictionary<byte, PacketHandlerRegistry<byte, ClientPacketHandler>> registryDict,
        string registryName
    ) {
        if (!registryDict.TryGetValue(addonId, out var registry)) {
            Logger.Warn($"There is no {registryName} handler registry for addon ID {addonId}");
            return;
        }

        UnpackPacketDataDict(
            packetDataDict, (packetId, data) =>
                registry.Execute(packetId, handler => handler(data))
        );
    }

    private void RegisterClientAddonHandler(
        byte addonId,
        byte packetId,
        ClientPacketHandler handler,
        Dictionary<byte, PacketHandlerRegistry<byte, ClientPacketHandler>> registryDict,
        string nameType
    ) {
        if (!registryDict.TryGetValue(addonId, out var registry)) {
            registry = new PacketHandlerRegistry<byte, ClientPacketHandler>(
                $"client addon {nameType} (Addon {addonId})", true
            );
            registryDict[addonId] = registry;
        }

        registry.Register(packetId, handler);
    }

    private void DeregisterClientAddonHandler(
        byte addonId,
        byte packetId,
        Dictionary<byte, PacketHandlerRegistry<byte, ClientPacketHandler>> registryDict
    ) {
        if (registryDict.TryGetValue(addonId, out var registry)) {
            if (!registry.Deregister(packetId)) {
                throw new InvalidOperationException("Could not remove nonexistent addon packet handler");
            }
        } else {
            throw new InvalidOperationException(
                "Could not remove nonexistent addon packet handler (Registry not found)"
            );
        }
    }

    #endregion

    #region Server Addon Helpers

    private void HandleServerAddonPacket(
        ushort clientId,
        byte addonId,
        Dictionary<byte, IPacketData> packetDataDict,
        Dictionary<byte, PacketHandlerRegistry<byte, ServerPacketHandler>> registryDict,
        string registryName
    ) {
        if (!registryDict.TryGetValue(addonId, out var registry)) {
            Logger.Warn($"There is no {registryName} handler registry for addon ID {addonId}");
            return;
        }

        UnpackPacketDataDict(
            packetDataDict, (packetId, data) =>
                registry.Execute(packetId, handler => handler(clientId, data))
        );
    }

    private void RegisterServerAddonHandler(
        byte addonId,
        byte packetId,
        ServerPacketHandler handler,
        Dictionary<byte, PacketHandlerRegistry<byte, ServerPacketHandler>> registryDict,
        string nameType
    ) {
        if (!registryDict.TryGetValue(addonId, out var registry)) {
            registry = new PacketHandlerRegistry<byte, ServerPacketHandler>(
                $"server addon {nameType} (Addon {addonId})", false
            );
            registryDict[addonId] = registry;
        }

        registry.Register(packetId, handler);
    }

    private void DeregisterServerAddonHandler(
        byte addonId,
        byte packetId,
        Dictionary<byte, PacketHandlerRegistry<byte, ServerPacketHandler>> registryDict
    ) {
        if (registryDict.TryGetValue(addonId, out var registry)) {
            if (!registry.Deregister(packetId)) {
                throw new InvalidOperationException("Could not remove nonexistent addon packet handler");
            }
        } else {
            throw new InvalidOperationException(
                "Could not remove nonexistent addon packet handler (Registry not found)"
            );
        }
    }

    #endregion

    #region Client Addon Public Methods

    public void RegisterClientAddonUpdatePacketHandler(byte addonId, byte packetId, ClientPacketHandler handler) =>
        RegisterClientAddonHandler(addonId, packetId, handler, _clientAddonUpdateRegistries, "update");

    public void DeregisterClientAddonUpdatePacketHandler(byte addonId, byte packetId) =>
        DeregisterClientAddonHandler(addonId, packetId, _clientAddonUpdateRegistries);

    public void ClearClientAddonUpdatePacketHandlers() => _clientAddonUpdateRegistries.Clear();


    public void RegisterClientAddonConnectionPacketHandler(byte addonId, byte packetId, ClientPacketHandler handler) =>
        RegisterClientAddonHandler(addonId, packetId, handler, _clientAddonConnectionRegistries, "connection");

    public void DeregisterClientAddonConnectionPacketHandler(byte addonId, byte packetId) =>
        DeregisterClientAddonHandler(addonId, packetId, _clientAddonConnectionRegistries);

    public void ClearClientAddonConnectionPacketHandlers() => _clientAddonConnectionRegistries.Clear();

    #endregion

    #region Server Addon Public Methods

    public void RegisterServerAddonUpdatePacketHandler(byte addonId, byte packetId, ServerPacketHandler handler) =>
        RegisterServerAddonHandler(addonId, packetId, handler, _serverAddonUpdateRegistries, "update");

    public void DeregisterServerAddonUpdatePacketHandler(byte addonId, byte packetId) =>
        DeregisterServerAddonHandler(addonId, packetId, _serverAddonUpdateRegistries);

    public void RegisterServerAddonConnectionPacketHandler(byte addonId, byte packetId, ServerPacketHandler handler) =>
        RegisterServerAddonHandler(addonId, packetId, handler, _serverAddonConnectionRegistries, "connection");

    public void DeregisterServerAddonConnectionPacketHandler(byte addonId, byte packetId) =>
        DeregisterServerAddonHandler(addonId, packetId, _serverAddonConnectionRegistries);

    #endregion

    /// <summary>
    /// Parses the raw bytes into a list of packets.
    /// </summary>
    /// <param name="data">The raw data buffer.</param>
    /// <param name="length">The valid length of the data.</param>
    /// <param name="leftoverData">Reference to leftover data buffer.</param>
    /// <returns>List of parsed packets.</returns>
    public static List<Packet> HandleReceivedData(byte[] data, int length, ref byte[]? leftoverData) {
        return ByteArrayToPackets(data, length, ref leftoverData);
    }

    /// <summary>
    /// Converts a byte array into a list of Packets, handling fragmentation/leftovers.
    /// </summary>
    private static List<Packet> ByteArrayToPackets(byte[] data, int length, ref byte[]? leftoverData) {
        var packets = new List<Packet>();
        var readPosition = 0;

        // Prepend leftover data if any
        if (leftoverData != null) {
            // Create a new buffer combining leftover + new data
            // This is unavoidable allocation for stream reassembly
            var combined = new byte[leftoverData.Length + length];
            Buffer.BlockCopy(leftoverData, 0, combined, 0, leftoverData.Length);
            Buffer.BlockCopy(data, 0, combined, leftoverData.Length, length);

            data = combined;
            length = combined.Length;
            leftoverData = null;
        }

        while (readPosition < length) {
            // We need at least 2 bytes for the length
            if (length - readPosition < 2) {
                leftoverData = new byte[length - readPosition];
                Buffer.BlockCopy(data, readPosition, leftoverData, 0, leftoverData.Length);
                break;
            }

            // Read packet length (ushort) from little-endian encoded stream
            // Use explicit little-endian conversion to avoid dependence on system endianness.
            ushort packetLengthValue;
            if (BitConverter.IsLittleEndian) {
                packetLengthValue = BitConverter.ToUInt16(data, readPosition);
            } else {
                // Manual little-endian decode: low byte first, then high byte
                packetLengthValue = (ushort)(data[readPosition] | (data[readPosition + 1] << 8));
            }
            int packetLength = packetLengthValue;

            // Sanity check against allocation attacks or corruption.
            // If the length reads as invalid, we imply that protocol framing is lost (e.g. we are reading garbage as length).
            // In this case, we cannot safely find the next packet in the stream, so we must discard the rest of the buffer.
            if (packetLength <= 0 || packetLength > MaxPacketSize) { 
                 Logger.Warn($"Invalid packet length read: {packetLength}. Discarding buffer to prevent processing garbage.");
                 break; 
            }

            if (length - readPosition < 2 + packetLength) {
                // Incomplete packet
                leftoverData = new byte[length - readPosition];
                Buffer.BlockCopy(data, readPosition, leftoverData, 0, leftoverData.Length);
                break;
            }

            // We have a full packet. 
            // Copy data to ensure packet owns its buffer and is safe from reuse
            var packetData = new byte[packetLength];
            Array.Copy(data, readPosition + 2, packetData, 0, packetLength);
            packets.Add(new Packet(packetData, 0, packetLength));

            readPosition += 2 + packetLength;
        }

        return packets;
    }
}

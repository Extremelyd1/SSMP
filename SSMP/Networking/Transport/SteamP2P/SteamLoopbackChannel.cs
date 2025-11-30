using System;
using System.Buffers;
using SSMP.Logging;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Static channel for handling loopback communication (local client to local server)
/// when hosting a Steam lobby. Steam P2P does not support self-connection.
/// </summary>
internal static class SteamLoopbackChannel {
    private static SteamEncryptedTransportServer? _server;
    private static SteamEncryptedTransport? _client;

    /// <summary>
    /// Registers the server instance to receive loopback packets.
    /// </summary>
    public static void RegisterServer(SteamEncryptedTransportServer server) {
        _server = server;
    }

    /// <summary>
    /// Unregisters the server instance.
    /// </summary>
    public static void UnregisterServer() {
        _server = null;
    }

    /// <summary>
    /// Registers the client instance to receive loopback packets.
    /// </summary>
    public static void RegisterClient(SteamEncryptedTransport client) {
        _client = client;
    }

    /// <summary>
    /// Unregisters the client instance.
    /// </summary>
    public static void UnregisterClient() {
        _client = null;
    }

    /// <summary>
    /// Sends a packet from the client to the server via loopback.
    /// </summary>
    public static void SendToServer(byte[] data, int length) {
        var srv = _server;
        if (srv == null) {
            Logger.Debug("Steam Loopback: Server not registered, dropping packet");
            return;
        }

        // Create a copy of the data to simulate network isolation
        // We cannot use ArrayPool here because the consumer queues the buffer.
        var copy = new byte[length];
        Buffer.BlockCopy(data, 0, copy, 0, length);
        try {
            srv.ReceiveLoopbackPacket(copy, length);
        } catch (Exception e) {
            Logger.Error($"Steam Loopback: Error sending to server: {e}");
        }
    }

    /// <summary>
    /// Sends a packet from the server to the client via loopback.
    /// </summary>
    public static void SendToClient(byte[] data, int length) {
        var client = _client;
        if (client == null) {
            Logger.Debug("Steam Loopback: Client not registered, dropping packet");
            return;
        }

        // Create a copy of the data to simulate network isolation
        // We cannot use ArrayPool here because the consumer queues the buffer.
        var copy = new byte[length];
        Buffer.BlockCopy(data, 0, copy, 0, length);
        try {
            client.ReceiveLoopbackPacket(copy, length);
        } catch (Exception e) {
            Logger.Error($"Steam Loopback: Error sending to client: {e}");
        }
    }
}

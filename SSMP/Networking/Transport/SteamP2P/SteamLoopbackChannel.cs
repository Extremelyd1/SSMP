using System;
using SSMP.Logging;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Static channel for handling loopback communication (local client to local server)
/// when hosting a Steam lobby. Steam P2P does not support self-connection.
/// </summary>
internal static class SteamLoopbackChannel {
    /// <summary>
    /// The server transport for looping communication.
    /// </summary>
    private static SteamEncryptedTransportServer? _server;
    /// <summary>
    /// The client transport for looping communication.
    /// </summary>
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
    public static void SendToServer(byte[] data, int offset, int length) {
        var srv = _server;
        if (srv == null) {
            Logger.Debug("Steam Loopback: Server not registered, dropping packet");
            return;
        }

        // Create exact-sized buffer since Packet constructor assumes entire array is valid
        var copy = new byte[length];
        try {
            Array.Copy(data, offset, copy, 0, length);
            srv.ReceiveLoopbackPacket(copy, length);
        } catch (InvalidOperationException ex) when (ex.Message.Contains("Steamworks is not initialized")) {
            // Steam shut down - ignore silently
        } catch (Exception e) {
            Logger.Error($"Steam Loopback: Error sending to server: {e}");
        }
    }

    /// <summary>
    /// Sends a packet from the server to the client via loopback.
    /// </summary>
    public static void SendToClient(byte[] data, int offset, int length) {
        var client = _client;
        if (client == null) {
            Logger.Debug("Steam Loopback: Client not registered, dropping packet");
            return;
        }

        // Create exact-sized buffer since Packet constructor assumes entire array is valid
        var copy = new byte[length];
        try {
            Array.Copy(data, offset, copy, 0, length);
            client.ReceiveLoopbackPacket(copy, length);
        } catch (InvalidOperationException ex) when (ex.Message.Contains("Steamworks is not initialized")) {
            // Steam shut down - ignore silently
        } catch (Exception e) {
            Logger.Error($"Steam Loopback: Error sending to client: {e}");
        }
    }
}

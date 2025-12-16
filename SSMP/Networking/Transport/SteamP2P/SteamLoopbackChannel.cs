using System;
using SSMP.Logging;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Instance-based channel for handling loopback communication (local client to local server)
/// when hosting a Steam lobby. Steam P2P does not support self-connection.
/// </summary>
internal class SteamLoopbackChannel
{
    /// <summary>
    /// Lock for thread-safe singleton access.
    /// </summary>
    private static readonly object _lock = new();

    /// <summary>
    /// Singleton instance, created on first use.
    /// </summary>
    private static SteamLoopbackChannel? _instance;

    /// <summary>
    /// The server transport for looping communication.
    /// </summary>
    private SteamEncryptedTransportServer? _server;

    /// <summary>
    /// The client transport for looping communication.
    /// </summary>
    private SteamEncryptedTransport? _client;

    /// <summary>
    /// Private constructor for singleton pattern.
    /// </summary>
    private SteamLoopbackChannel()
    {
    }

    /// <summary>
    /// Gets or creates the singleton loopback channel instance.
    /// Thread-safe.
    /// </summary>
    public static SteamLoopbackChannel GetOrCreate()
    {
        lock (_lock)
        {
            return _instance ??= new SteamLoopbackChannel();
        }
    }

    /// <summary>
    /// Releases the singleton instance if both server and client are unregistered.
    /// Thread-safe.
    /// </summary>
    public static void ReleaseIfEmpty()
    {
        lock (_lock)
        {
            if (_instance?._server == null && _instance?._client == null)
            {
                _instance = null;
            }
        }
    }

    /// <summary>
    /// Registers the server instance to receive loopback packets.
    /// </summary>
    public void RegisterServer(SteamEncryptedTransportServer server)
    {
        lock (_lock)
        {
            _server = server;
        }
    }

    /// <summary>
    /// Unregisters the server instance.
    /// </summary>
    public void UnregisterServer()
    {
        lock (_lock)
        {
            _server = null;
        }
    }

    /// <summary>
    /// Registers the client instance to receive loopback packets.
    /// </summary>
    public void RegisterClient(SteamEncryptedTransport client)
    {
        lock (_lock)
        {
            _client = client;
        }
    }

    /// <summary>
    /// Unregisters the client instance.
    /// </summary>
    public void UnregisterClient()
    {
        lock (_lock)
        {
            _client = null;
        }
    }

    /// <summary>
    /// Sends a packet from the client to the server via loopback.
    /// </summary>
    public void SendToServer(byte[] data, int offset, int length)
    {
        SteamEncryptedTransportServer? srv;
        lock (_lock)
        {
            srv = _server;
        }

        if (srv == null)
        {
            Logger.Debug("Steam Loopback: Server not registered, dropping packet");
            return;
        }

        // Create exact-sized buffer since Packet constructor assumes entire array is valid
        var copy = new byte[length];
        try
        {
            Array.Copy(data, offset, copy, 0, length);
            srv.ReceiveLoopbackPacket(copy, length);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Steamworks is not initialized"))
        {
            // Steam shut down - ignore silently
        }
        catch (Exception e)
        {
            Logger.Error($"Steam Loopback: Error sending to server: {e}");
        }
    }

    /// <summary>
    /// Sends a packet from the server to the client via loopback.
    /// </summary>
    public void SendToClient(byte[] data, int offset, int length)
    {
        SteamEncryptedTransport? client;
        lock (_lock)
        {
            client = _client;
        }

        if (client == null)
        {
            Logger.Debug("Steam Loopback: Client not registered, dropping packet");
            return;
        }

        // Create exact-sized buffer since Packet constructor assumes entire array is valid
        var copy = new byte[length];
        try
        {
            Array.Copy(data, offset, copy, 0, length);
            client.ReceiveLoopbackPacket(copy, length);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Steamworks is not initialized"))
        {
            // Steam shut down - ignore silently
        }
        catch (Exception e)
        {
            Logger.Error($"Steam Loopback: Error sending to client: {e}");
        }
    }
}
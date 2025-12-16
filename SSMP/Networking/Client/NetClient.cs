using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Org.BouncyCastle.Tls;
using SSMP.Api.Client;
using SSMP.Api.Client.Networking;
using SSMP.Logging;
using SSMP.Networking.Chunk;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Data;
using SSMP.Networking.Packet.Update;
using SSMP.Networking.Transport.Common;
using SSMP.Util;

namespace SSMP.Networking.Client;

/// <summary>
/// The networking client that manages the UDP client for sending and receiving data. This only
/// manages client side networking, e.g. sending to and receiving from the server.
/// </summary>
internal class NetClient : INetClient
{
    /// <summary>
    /// The packet manager instance.
    /// </summary>
    private readonly PacketManager _packetManager;

    /// <summary>
    /// The client update manager for this net client.
    /// </summary>
    public ClientUpdateManager UpdateManager { get; }

    /// <summary>
    /// Event that is called when the client connects to a server.
    /// </summary>
    public event Action<ServerInfo>? ConnectEvent;

    /// <summary>
    /// Event that is called when the client fails to connect to a server.
    /// </summary>
    public event Action<ConnectionFailedResult>? ConnectFailedEvent;

    /// <summary>
    /// Event that is called when the client disconnects from a server.
    /// </summary>
    public event Action? DisconnectEvent;

    /// <summary>
    /// Event that is called when the client times out from a connection.
    /// </summary>
    public event Action? TimeoutEvent;

    /// <summary>
    /// The connection status of the client.
    /// </summary>
    public ClientConnectionStatus ConnectionStatus { get; private set; } = ClientConnectionStatus.NotConnected;

    /// <inheritdoc />
    public bool IsConnected => ConnectionStatus == ClientConnectionStatus.Connected;

    /// <summary>
    /// The encrypted transport instance for handling encrypted connections.
    /// </summary>
    private IEncryptedTransport? _transport;

    /// <summary>
    /// Chunk sender instance for sending large amounts of data.
    /// </summary>
    private readonly ClientChunkSender _chunkSender;

    /// <summary>
    /// Chunk receiver instance for receiving large amounts of data.
    /// </summary>
    private readonly ClientChunkReceiver _chunkReceiver;

    /// <summary>
    /// The client connection manager responsible for handling sending and receiving connection data.
    /// </summary>
    private readonly ClientConnectionManager _connectionManager;

    /// <summary>
    /// Byte array containing received data that was not included in a packet object yet.
    /// </summary>
    private byte[]? _leftoverData;

    /// <summary>
    /// Lock object for synchronizing connection state changes.
    /// </summary>
    private readonly object _connectionLock = new();

    /// <summary>
    /// Construct the net client with the given packet manager.
    /// </summary>
    /// <param name="packetManager">The packet manager instance.</param>
    public NetClient(PacketManager packetManager)
    {
        _packetManager = packetManager;

        // Create initial update manager with default settings (will be recreated if needed in Connect)
        UpdateManager = new ClientUpdateManager();

        _chunkSender = new ClientChunkSender(UpdateManager);
        _chunkReceiver = new ClientChunkReceiver(UpdateManager);
        _connectionManager = new ClientConnectionManager(_packetManager, _chunkSender, _chunkReceiver);

        _connectionManager.ServerInfoReceivedEvent += OnServerInfoReceived;
    }

    /// <summary>
    /// Starts establishing a connection with the given host on the given port.
    /// </summary>
    /// <param name="address">The address to connect to.</param>
    /// <param name="port">The port to connect to.</param>
    /// <param name="username">The username to connect with.</param>
    /// <param name="authKey">The authentication key to use.</param>
    /// <param name="addonData">A list of addon data that the client has.</param>
    /// <param name="transport">The transport to use.</param>
    public void Connect(
        string address,
        int port,
        string username,
        string authKey,
        List<AddonData> addonData,
        IEncryptedTransport transport
    )
    {
        // Prevent multiple simultaneous connection attempts
        lock (_connectionLock)
        {
            if (ConnectionStatus == ClientConnectionStatus.Connecting)
            {
                Logger.Warn("Connection attempt already in progress, ignoring duplicate request");
                return;
            }

            if (ConnectionStatus == ClientConnectionStatus.Connected)
            {
                Logger.Warn("Already connected, disconnecting first");
                // Don't fire DisconnectEvent when transitioning to a new connection
                InternalDisconnect(shouldFireEvent: false);
            }

            ConnectionStatus = ClientConnectionStatus.Connecting;
        }

        // Start a new thread for establishing the connection, otherwise Unity will hang
        new Thread(() =>
        {
            try
            {
                _transport = transport;
                _transport.DataReceivedEvent += OnReceiveData;
                _transport.Connect(address, port);

                UpdateManager.Transport = _transport;
                UpdateManager.StartUpdates();
                _chunkSender.Start();

                // Only UDP/HolePunch need timeout management (Steam has built-in connection tracking)
                if (_transport.RequiresCongestionManagement)
                {
                    UpdateManager.TimeoutEvent += OnConnectTimedOut;
                }

                _connectionManager.StartConnection(username, authKey, addonData);
            }
            catch (TlsTimeoutException)
            {
                Logger.Info("DTLS connection timed out");
                HandleConnectFailed(new ConnectionFailedResult { Reason = ConnectionFailedReason.TimedOut });
            }
            catch (SocketException e)
            {
                Logger.Error($"Failed to connect due to SocketException:\n{e}");
                HandleConnectFailed(new ConnectionFailedResult { Reason = ConnectionFailedReason.SocketException });
            }
            catch (Exception e) when (e is IOException)
            {
                Logger.Error($"Failed to connect due to IOException:\n{e}");
                HandleConnectFailed(new ConnectionFailedResult { Reason = ConnectionFailedReason.IOException });
            }
            catch (Exception e)
            {
                Logger.Error($"Unexpected error during connection:\n{e}");
                HandleConnectFailed(new ConnectionFailedResult { Reason = ConnectionFailedReason.IOException });
            }
        }) { IsBackground = true }.Start();
    }


    /// <summary>
    /// Disconnect from the current server.
    /// </summary>
    public void Disconnect()
    {
        lock (_connectionLock)
        {
            InternalDisconnect();
        }
    }

    /// <summary>
    /// Internal disconnect implementation without locking (assumes caller holds lock).
    /// </summary>
    /// <param name="shouldFireEvent">Whether to fire DisconnectEvent. Set to false when cleaning up an old connection
    /// before immediately starting a new one.</param>
    private void InternalDisconnect(bool shouldFireEvent = true)
    {
        if (ConnectionStatus == ClientConnectionStatus.NotConnected)
        {
            return;
        }

        var wasConnectedOrConnecting = ConnectionStatus != ClientConnectionStatus.NotConnected;

        try
        {
            UpdateManager.StopUpdates();
            UpdateManager.TimeoutEvent -= OnConnectTimedOut;
            UpdateManager.TimeoutEvent -= OnUpdateTimedOut;
            _chunkSender.Stop();
            _chunkReceiver.Reset();

            if (_transport != null)
            {
                _transport.DataReceivedEvent -= OnReceiveData;
                _transport.Disconnect();
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Error in NetClient.InternalDisconnect: {e}");
        }

        ConnectionStatus = ClientConnectionStatus.NotConnected;

        // Clear all client addon packet handlers, because their IDs become invalid
        _packetManager.ClearClientAddonUpdatePacketHandlers();

        // Clear leftover data
        _leftoverData = null;

        // Fire DisconnectEvent on main thread for all disconnects (internal or explicit)
        // This provides a consistent notification for observers to clean up resources
        if (shouldFireEvent && wasConnectedOrConnecting)
        {
            ThreadUtil.RunActionOnMainThread(() =>
            {
                try
                {
                    DisconnectEvent?.Invoke();
                }
                catch (Exception e)
                {
                    Logger.Error($"Error in DisconnectEvent: {e}");
                }
            });
        }
    }

    /// <summary>
    /// Callback method for when the DTLS client receives data. This will update the update manager that we have
    /// received data, handle packet creation from raw data, handle login responses, and forward received packets to
    /// the packet manager.
    /// </summary>
    /// <param name="buffer">Byte array containing the received bytes.</param>
    /// <param name="length">The number of bytes in the <paramref name="buffer"/>.</param>
    private void OnReceiveData(byte[] buffer, int length)
    {
        if (ConnectionStatus == ClientConnectionStatus.NotConnected)
        {
            Logger.Error("Client is not connected to a server, but received data, ignoring");
            return;
        }

        var packets = PacketManager.HandleReceivedData(buffer, length, ref _leftoverData);

        foreach (var packet in packets)
        {
            try
            {
                var clientUpdatePacket = new ClientUpdatePacket();
                if (!clientUpdatePacket.ReadPacket(packet))
                {
                    // If ReadPacket returns false, we received a malformed packet, which we simply ignore for now
                    continue;
                }

                // Route all transports through UpdateManager for sequence/ACK tracking
                // UpdateManager will skip UDP-specific logic for Steam transports
                UpdateManager.OnReceivePacket<ClientUpdatePacket, ClientUpdatePacketId>(clientUpdatePacket);

                // First check for slice or slice ack data and handle it separately by passing it onto either the chunk 
                // sender or chunk receiver
                var packetData = clientUpdatePacket.GetPacketData();

                if (packetData.Remove(ClientUpdatePacketId.Slice, out var sliceData))
                {
                    _chunkReceiver.ProcessReceivedData((SliceData)sliceData);
                }

                if (packetData.Remove(ClientUpdatePacketId.SliceAck, out var sliceAckData))
                {
                    _chunkSender.ProcessReceivedData((SliceAckData)sliceAckData);
                }

                // Then, if we are already connected to a server,
                // we let the packet manager handle the rest of the packet data
                if (ConnectionStatus == ClientConnectionStatus.Connected)
                {
                    _packetManager.HandleClientUpdatePacket(clientUpdatePacket);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Error processing incoming packet: {e}");
            }
        }
    }

    private void OnServerInfoReceived(ServerInfo serverInfo)
    {
        if (serverInfo.ConnectionResult == ServerConnectionResult.Accepted)
        {
            Logger.Debug("Connection to server accepted");

            // De-register the "connect failed" and register the actual timeout handler if we time out
            UpdateManager.TimeoutEvent -= OnConnectTimedOut;
            UpdateManager.TimeoutEvent += OnUpdateTimedOut;

            lock (_connectionLock)
            {
                ConnectionStatus = ClientConnectionStatus.Connected;
            }

            ThreadUtil.RunActionOnMainThread(() =>
            {
                try
                {
                    ConnectEvent?.Invoke(serverInfo);
                }
                catch (Exception e)
                {
                    Logger.Error($"Error in ConnectEvent: {e}");
                }
            });
            return;
        }

        // Connection rejected
        var result = serverInfo.ConnectionResult == ServerConnectionResult.InvalidAddons
            ? new ConnectionInvalidAddonsResult
            {
                Reason = ConnectionFailedReason.InvalidAddons,
                AddonData = serverInfo.AddonData
            }
            : (ConnectionFailedResult)new ConnectionFailedMessageResult
            {
                Reason = ConnectionFailedReason.Other,
                Message = serverInfo.ConnectionRejectedMessage
            };

        HandleConnectFailed(result);
    }

    /// <summary>
    /// Callback method for when the client connection fails.
    /// </summary>
    private void OnConnectTimedOut() => HandleConnectFailed(new ConnectionFailedResult
    {
        Reason = ConnectionFailedReason.TimedOut
    });

    /// <summary>
    /// Callback method for when the client times out while connected.
    /// </summary>
    private void OnUpdateTimedOut()
    {
        ThreadUtil.RunActionOnMainThread(() => { TimeoutEvent?.Invoke(); });
    }

    /// <summary>
    /// Handles a failed connection with the given result.
    /// </summary>
    /// <param name="result">The connection failed result containing failure details.</param>
    private void HandleConnectFailed(ConnectionFailedResult result)
    {
        lock (_connectionLock)
        {
            InternalDisconnect();
        }

        ThreadUtil.RunActionOnMainThread(() => { ConnectFailedEvent?.Invoke(result); });
    }

    /// <inheritdoc />
    public IClientAddonNetworkSender<TPacketId> GetNetworkSender<TPacketId>(
        ClientAddon addon
    ) where TPacketId : Enum
    {
        ValidateAddon(addon);

        // Check whether there already is a network sender for the given addon
        if (addon.NetworkSender != null)
        {
            if (!(addon.NetworkSender is IClientAddonNetworkSender<TPacketId> addonNetworkSender))
            {
                throw new InvalidOperationException(
                    "Cannot request network senders with differing generic parameters");
            }

            return addonNetworkSender;
        }

        // Otherwise create one, store it and return it
        var newAddonNetworkSender = new ClientAddonNetworkSender<TPacketId>(this, addon);
        addon.NetworkSender = newAddonNetworkSender;

        return newAddonNetworkSender;
    }

    /// <summary>
    /// Validates that an addon is non-null and has requested network access.
    /// </summary>
    private static void ValidateAddon(ClientAddon addon)
    {
        if (addon == null)
        {
            throw new ArgumentNullException(nameof(addon));
        }

        if (!addon.NeedsNetwork)
        {
            throw new InvalidOperationException("Addon has not requested network access through property");
        }
    }

    /// <inheritdoc />
    public IClientAddonNetworkReceiver<TPacketId> GetNetworkReceiver<TPacketId>(
        ClientAddon addon,
        Func<TPacketId, IPacketData> packetInstantiator
    ) where TPacketId : Enum
    {
        ValidateAddon(addon);

        if (packetInstantiator == null)
        {
            throw new ArgumentNullException(nameof(packetInstantiator));
        }

        ClientAddonNetworkReceiver<TPacketId>? networkReceiver = null;

        // Check whether an existing network receiver exists
        if (addon.NetworkReceiver == null)
        {
            networkReceiver = new ClientAddonNetworkReceiver<TPacketId>(addon, _packetManager);
            addon.NetworkReceiver = networkReceiver;
        }
        else if (addon.NetworkReceiver is not IClientAddonNetworkReceiver<TPacketId>)
        {
            throw new InvalidOperationException(
                "Cannot request network receivers with differing generic parameters");
        }

        networkReceiver?.AssignAddonPacketInfo(packetInstantiator);

        return (addon.NetworkReceiver as IClientAddonNetworkReceiver<TPacketId>)!;
    }
}
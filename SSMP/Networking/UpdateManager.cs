using System;
using System.Timers;
using SSMP.Concurrency;
using SSMP.Logging;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Data;
using SSMP.Networking.Packet.Update;
using SSMP.Networking.Transport.Common;
using Timer = System.Timers.Timer;

namespace SSMP.Networking;

/// <summary>
/// Class that manages sending the update packet. Has a simple congestion avoidance system to
/// avoid flooding the channel.
/// </summary>
internal abstract class UpdateManager {
    /// <summary>
    /// The number of ack numbers from previous packets to store in the packet. 
    /// </summary>
    public const int AckSize = 64;
}

/// <inheritdoc />
internal abstract class UpdateManager<TOutgoing, TPacketId> : UpdateManager
    where TOutgoing : UpdatePacket<TPacketId>, new()
    where TPacketId : Enum {
    /// <summary>
    /// The time in milliseconds to disconnect after not receiving any updates.
    /// </summary>
    private const int ConnectionTimeout = 5000;

    /// <summary>
    /// The MTU (maximum transfer unit) to use to send packets with. If the length of a packet exceeds this, we break
    /// it up into smaller packets before sending. This ensures that we control the breaking of packets in most
    /// cases and do not rely on smaller network devices for the breaking up as this could impact performance.
    /// This size is lower than the limit for DTLS packets, since there is a slight DTLS overhead for packets.
    /// </summary>
    private const int PacketMtu = 1200;

    /// <summary>
    /// The number of sequence numbers to store in the received queue to construct ack fields with and
    /// to check against resent data.
    /// </summary>
    private const int ReceiveQueueSize = AckSize;
    
    /// <summary>
    /// The UDP congestion manager instance. Null if congestion management is disabled.
    /// </summary>
    private readonly CongestionManager<TOutgoing, TPacketId>? _udpCongestionManager;

    /// <summary>
    /// The last sent sequence number.
    /// </summary>
    private ushort _localSequence;

    /// <summary>
    /// The last received sequence number.
    /// </summary>
    private ushort _remoteSequence;

    /// <summary>
    /// Fixed-size queue containing sequence numbers that have been received.
    /// </summary>
    private readonly ConcurrentFixedSizeQueue<ushort> _receivedQueue;

    /// <summary>
    /// Object to lock asynchronous accesses.
    /// </summary>
    protected readonly object Lock = new();

    /// <summary>
    /// The current instance of the update packet.
    /// </summary>
    protected TOutgoing CurrentUpdatePacket;

    /// <summary>
    /// Timer for keeping track of when to send an update packet.
    /// </summary>
    private readonly Timer _sendTimer;

    /// <summary>
    /// Timer for keeping track of the connection timing out.
    /// </summary>
    private readonly Timer _heartBeatTimer;

    /// <summary>
    /// The last used send rate for the send timer. Used to check whether the interval of the timers needs to be
    /// updated.
    /// </summary>
    private int _lastSendRate;

    /// <summary>
    /// Whether this update manager is actually updating and sending packets.
    /// </summary>
    private bool _isUpdating;
    
    /// <summary>
    /// The transport sender instance to use to send packets.
    /// Can be either IEncryptedTransport (client-side) or IEncryptedTransportClient (server-side).
    /// </summary>
    private volatile object? _transportSender;
    
    /// <summary>
    /// Gets or sets the transport for client-side communication.
    /// </summary>
    public IEncryptedTransport? Transport {
        get => _transportSender as IEncryptedTransport;
        set => _transportSender = value;
    }
    
    /// <summary>
    /// Sets the transport client for server-side communication.
    /// </summary>
    public IEncryptedTransportClient? TransportClient {
        set => _transportSender = value;
    }

    /// <summary>
    /// The current send rate in milliseconds between sending packets.
    /// </summary>
    public int CurrentSendRate { get; set; } = CongestionManager<TOutgoing, TPacketId>.HighSendRate;

    /// <summary>
    /// Moving average of round trip time (RTT) between sending and receiving a packet.
    /// Returns 0 if congestion management is disabled.
    /// </summary>
    public int AverageRtt => _udpCongestionManager != null 
        ? (int) System.Math.Round(_udpCongestionManager.AverageRtt) 
        : 0;

    /// <summary>
    /// Event that is called when the client times out.
    /// </summary>
    public event Action? TimeoutEvent;

    /// <summary>
    /// Construct the update manager with a UDP socket.
    /// </summary>
    protected UpdateManager() {
        _udpCongestionManager = new CongestionManager<TOutgoing, TPacketId>(this);
        _receivedQueue = new ConcurrentFixedSizeQueue<ushort>(ReceiveQueueSize);

        CurrentUpdatePacket = new TOutgoing();

        _sendTimer = new Timer {
            AutoReset = true,
            Interval = CurrentSendRate
        };
        _sendTimer.Elapsed += OnSendTimerElapsed;

        _heartBeatTimer = new Timer {
            AutoReset = false,
            Interval = ConnectionTimeout
        };
        _heartBeatTimer.Elapsed += OnHeartBeatTimerElapsed;
    }

    /// <summary>
    /// Start the update manager. This will start the send and heartbeat timers, which will respectively trigger
    /// sending update packets and trigger on connection timing out.
    /// </summary>
    public void StartUpdates() {
        _lastSendRate = CurrentSendRate;
        _sendTimer.Start();
        _heartBeatTimer.Start();

        _isUpdating = true;
    }

    /// <summary>
    /// Stop sending the periodic UDP update packets after sending the current one.
    /// </summary>
    public void StopUpdates() {
        if (!_isUpdating) {
            return;
        }

        _isUpdating = false;
        
        Logger.Debug("Stopping UDP updates, sending last packet");
        
        CreateAndSendUpdatePacket();

        _sendTimer.Stop();
        _heartBeatTimer.Stop();
    }

    /// <summary>
    /// Callback method for when a packet is received.
    /// </summary>
    /// <param name="packet"></param>
    /// <typeparam name="TIncoming"></typeparam>
    /// <typeparam name="TOtherPacketId"></typeparam>
    public void OnReceivePacket<TIncoming, TOtherPacketId>(TIncoming packet)
        where TIncoming : UpdatePacket<TOtherPacketId>
        where TOtherPacketId : Enum {
        
        _heartBeatTimer.Stop();
        _heartBeatTimer.Start();

        // Steam transports have built-in reliability and connection tracking,
        // so they bypass UDP-specific sequence/ACK/congestion logic
        if (IsSteamTransport()) {
            return;
        }

        // UDP/HolePunch path: Handle congestion, sequence tracking, and deduplication
        _udpCongestionManager?.OnReceivePackets<TIncoming, TOtherPacketId>(packet);

        var sequence = packet.Sequence;
        _receivedQueue.Enqueue(sequence);

        packet.DropDuplicateResendData(_receivedQueue.GetCopy());

        if (IsSequenceGreaterThan(sequence, _remoteSequence)) {
            _remoteSequence = sequence;
        }
    }

    /// <summary>
    /// Creates an update packet with current data and sends it through the transport.
    /// For UDP/HolePunch: handles sequence numbers, ACK fields, and congestion management.
    /// For Steam: bypasses reliability features and sends packet directly.
    /// Automatically fragments packets that exceed MTU size.
    /// </summary>
    private void CreateAndSendUpdatePacket() {
        var packet = new Packet.Packet();
        TOutgoing updatePacket;

        lock (Lock) {
            // UDP/HolePunch path: Configure sequence and ACK data
            if (!IsSteamTransport()) {
                CurrentUpdatePacket.Sequence = _localSequence;
                CurrentUpdatePacket.Ack = _remoteSequence;
                PopulateAckField();
            }

            try {
                CurrentUpdatePacket.CreatePacket(packet);
            } catch (Exception e) {
                Logger.Error($"Failed to create packet: {e}");
                return;
            }

            updatePacket = CurrentUpdatePacket;
            CurrentUpdatePacket = new TOutgoing();
        }

        // UDP/HolePunch path: Track for congestion management and increment sequence
        if (!IsSteamTransport()) {
            _udpCongestionManager?.OnSendPacket(_localSequence, updatePacket);
            _localSequence++;
        }

        SendPacketWithFragmentation(packet, updatePacket.ContainsReliableData);
    }

    /// <summary>
    /// Populates the ACK field with acknowledgment bits for recently received packets.
    /// Each bit indicates whether a packet with that sequence number was received.
    /// Only used for UDP/HolePunch transports.
    /// </summary>
    private void PopulateAckField() {
        var receivedQueue = _receivedQueue.GetCopy();

        for (ushort i = 0; i < AckSize; i++) {
            var pastSequence = (ushort) (_remoteSequence - i - 1);
            CurrentUpdatePacket.AckField[i] = receivedQueue.Contains(pastSequence);
        }
    }

    /// <summary>
    /// Sends a packet, fragmenting it into smaller chunks if it exceeds the MTU size.
    /// Fragments are sent sequentially to ensure they can be reassembled by the receiver.
    /// </summary>
    /// <param name="packet">The packet to send, which may be fragmented if too large.</param>
    /// <param name="isReliable">Whether the packet data needs to be delivered reliably.</param>
    private void SendPacketWithFragmentation(Packet.Packet packet, bool isReliable) {
        if (packet.Length <= PacketMtu) {
            SendPacket(packet, isReliable);
            return;
        }

        var byteArray = packet.ToArray();
        var index = 0;

        while (index < byteArray.Length) {
            var length = System.Math.Min(byteArray.Length - index, PacketMtu);
            var fragment = new byte[length];
            Array.Copy(byteArray, index, fragment, 0, length);

            // Fragmented packets are only reliable if the original packet was, and we only 
            // set reliability for the first fragment or all? 
            // In this implementation logic, it seems we treated the whole packet as reliable or not.
            // However, typical fragmentation reliability depends on transport. 
            // Assuming for now that if the main packet is reliable, we want to try and send fragments reliably too.
            SendPacket(new Packet.Packet(fragment), isReliable);
            index += length;
        }
    }

    /// <summary>
    /// Determines if the current transport is Steam, which has built-in reliability
    /// and does not require manual congestion management or sequence tracking.
    /// </summary>
    /// <returns>True if using Steam transport, false for UDP/HolePunch.</returns>
    protected bool IsSteamTransport() {
        if (_transportSender is IEncryptedTransport transport) {
            return !transport.RequiresCongestionManagement;
        }

        if (_transportSender is IEncryptedTransportClient transportClient) {
            // Steam clients have null EndPoint as they don't use IP/Port addressing
            return transportClient.EndPoint == null;
        }

        return false;
    }

    /// <summary>
    /// Callback method for when the send timer elapses. Will create and send a new update packet and update the
    /// timer interval in case the send rate changes.
    /// </summary>
    private void OnSendTimerElapsed(object sender, ElapsedEventArgs elapsedEventArgs) {
        CreateAndSendUpdatePacket();

        if (_lastSendRate != CurrentSendRate) {
            _sendTimer.Interval = CurrentSendRate;
            _lastSendRate = CurrentSendRate;
        }
    }

    /// <summary>
    /// Callback method for when the heart beat timer elapses. Will invoke the timeout event.
    /// </summary>
    private void OnHeartBeatTimerElapsed(object sender, ElapsedEventArgs elapsedEventArgs) {
        TimeoutEvent?.Invoke();
    }

    /// <summary>
    /// Check whether the first given sequence number is greater than the second given sequence number.
    /// Accounts for sequence number wrap-around, by inverse comparison if differences are larger than half
    /// of the sequence number space.
    /// </summary>
    /// <param name="sequence1">The first sequence number to compare.</param>
    /// <param name="sequence2">The second sequence number to compare.</param>
    /// <returns>True if the first sequence number is greater than the second sequence number.</returns>
    private bool IsSequenceGreaterThan(ushort sequence1, ushort sequence2) {
        return sequence1 > sequence2 && sequence1 - sequence2 <= 32768
               || sequence1 < sequence2 && sequence2 - sequence1 > 32768;
    }

    /// <summary>
    /// Resend the given packet that was (supposedly) lost by adding data that needs to be reliable to the
    /// current update packet.
    /// </summary>
    /// <param name="lostPacket">The packet instance that was lost.</param>
    public abstract void ResendReliableData(TOutgoing lostPacket);

    /// <summary>
    /// Sends the given packet over the corresponding medium.
    /// </summary>
    /// <param name="packet">The raw packet instance.</param>
    /// <param name="isReliable">Whether the packet contains reliable data.</param>
    private void SendPacket(Packet.Packet packet, bool isReliable) {
        var buffer = packet.ToArray();

        switch (_transportSender) {
            case IReliableTransport reliableTransport when isReliable:
                reliableTransport.SendReliable(buffer, 0, buffer.Length);
                break;

            case IEncryptedTransport transport:
                transport.Send(buffer, 0, buffer.Length);
                break;

            case IReliableTransportClient reliableTransportClient when isReliable:
                reliableTransportClient.SendReliable(buffer, 0, buffer.Length);
                break;

            case IEncryptedTransportClient transportClient:
                transportClient.Send(buffer, 0, buffer.Length);
                break;
        }
    }

    /// <summary>
    /// Either get or create an AddonPacketData instance for the given addon.
    /// </summary>
    /// <param name="addonId">The ID of the addon.</param>
    /// <param name="packetIdSize">The size of the packet ID size.</param>
    /// <returns>The instance of AddonPacketData already in the packet or a new one if no such instance
    /// exists</returns>
    private AddonPacketData GetOrCreateAddonPacketData(byte addonId, byte packetIdSize) {
        lock (Lock) {
            if (!CurrentUpdatePacket.TryGetSendingAddonPacketData(
                    addonId,
                    out var addonPacketData
                )) {
                addonPacketData = new AddonPacketData(packetIdSize);
                CurrentUpdatePacket.SetSendingAddonPacketData(addonId, addonPacketData);
            }

            return addonPacketData;
        }
    }

    /// <summary>
    /// Set (non-collection) addon data to be networked for the addon with the given ID.
    /// </summary>
    /// <param name="addonId">The ID of the addon.</param>
    /// <param name="packetId">The ID of the packet data.</param>
    /// <param name="packetIdSize">The size of the packet ID space.</param>
    /// <param name="packetData">The packet data to send.</param>
    public void SetAddonData(
        byte addonId,
        byte packetId,
        byte packetIdSize,
        IPacketData packetData
    ) {
        lock (Lock) {
            var addonPacketData = GetOrCreateAddonPacketData(addonId, packetIdSize);

            addonPacketData.PacketData[packetId] = packetData;
        }
    }

    /// <summary>
    /// Set addon data as a collection to be networked for the addon with the given ID.
    /// </summary>
    /// <param name="addonId">The ID of the addon.</param>
    /// <param name="packetId">The ID of the packet data.</param>
    /// <param name="packetIdSize">The size of the packet ID space.</param>
    /// <param name="packetData">The packet data to send.</param>
    /// <typeparam name="TPacketData">The type of the packet data in the collection.</typeparam>
    /// <exception cref="InvalidOperationException">Thrown if the packet data could not be added.</exception>
    public void SetAddonDataAsCollection<TPacketData>(
        byte addonId,
        byte packetId,
        byte packetIdSize,
        TPacketData packetData
    ) where TPacketData : IPacketData, new() {
        lock (Lock) {
            var addonPacketData = GetOrCreateAddonPacketData(addonId, packetIdSize);

            if (!addonPacketData.PacketData.TryGetValue(packetId, out var existingPacketData)) {
                existingPacketData = new PacketDataCollection<TPacketData>();
                addonPacketData.PacketData[packetId] = existingPacketData;
            }

            if (!(existingPacketData is RawPacketDataCollection existingDataCollection)) {
                throw new InvalidOperationException("Could not add addon data with existing non-collection data");
            }

            if (packetData is RawPacketDataCollection packetDataAsCollection) {
                existingDataCollection.DataInstances.AddRange(packetDataAsCollection.DataInstances);
            } else {
                existingDataCollection.DataInstances.Add(packetData);
            }
        }
    }
}

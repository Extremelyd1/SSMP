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
internal abstract class UdpUpdateManager {
    /// <summary>
    /// The number of ack numbers from previous packets to store in the packet. 
    /// </summary>
    public const int AckSize = 64;
}

/// <inheritdoc />
internal abstract class UdpUpdateManager<TOutgoing, TPacketId> : UdpUpdateManager
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
    private readonly UdpCongestionManager<TOutgoing, TPacketId>? _udpCongestionManager;

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
    protected readonly object Lock = new object();

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
    private volatile ITransportSender? _transportSender;
    
    /// <summary>
    /// Sets the transport for client-side communication.
    /// </summary>
    public IEncryptedTransport? Transport {
        set {
            if (value != null && value is not ITransportSender) {
                throw new InvalidOperationException(
                    $"Transport {value.GetType().Name} does not implement ITransportSender");
            }

            _transportSender = value;
        }
    }
    
    /// <summary>
    /// Sets the transport client for server-side communication.
    /// </summary>
    public IEncryptedTransportClient? TransportClient {
        set {
            if (value != null && value is not ITransportSender) {
                throw new InvalidOperationException(
                    $"Transport client {value.GetType().Name} does not implement ITransportSender");
            }
            _transportSender = value;
        }
    }


    /// <summary>
    /// The current send rate in milliseconds between sending packets.
    /// </summary>
    public int CurrentSendRate { get; set; } = UdpCongestionManager<TOutgoing, TPacketId>.HighSendRate;

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
    /// <param name="enableCongestionManagement">Whether to enable congestion management. 
    /// Set to false for transports with built-in congestion handling (e.g., Steam P2P).</param>
    protected UdpUpdateManager() {
        _udpCongestionManager = new UdpCongestionManager<TOutgoing, TPacketId>(this);
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
        _udpCongestionManager?.OnReceivePackets<TIncoming, TOtherPacketId>(packet);

        var sequence = packet.Sequence;
        _receivedQueue.Enqueue(sequence);

        packet.DropDuplicateResendData(_receivedQueue.GetCopy());

        if (IsSequenceGreaterThan(sequence, _remoteSequence)) {
            _remoteSequence = sequence;
        }

        _heartBeatTimer.Stop();
        _heartBeatTimer.Start();
    }

    /// <summary>
    /// Create and send the current update packet.
    /// </summary>
    private void CreateAndSendUpdatePacket() {
        var sender = _transportSender;
        if (sender == null) {
            Logger.Warn("UdpUpdateManager: Cannot send update, transport sender is null");
            return;
        }

        var packet = new Packet.Packet();
        TOutgoing updatePacket;

        lock (Lock) {
            CurrentUpdatePacket.Sequence = _localSequence;
            CurrentUpdatePacket.Ack = _remoteSequence;

            var receivedQueue = _receivedQueue.GetCopy();

            for (ushort i = 0; i < AckSize; i++) {
                var pastSequence = (ushort) (_remoteSequence - i - 1);

                CurrentUpdatePacket.AckField[i] = receivedQueue.Contains(pastSequence);
            }

            try {
                CurrentUpdatePacket.CreatePacket(packet);
            } catch (Exception e) {
                Logger.Error($"An error occurred while trying to create packet:\n{e}");
                return;
            }

            updatePacket = CurrentUpdatePacket;
            CurrentUpdatePacket = new TOutgoing();
        }

        _udpCongestionManager?.OnSendPacket(_localSequence, updatePacket);

        _localSequence++;

        if (packet.Length > PacketMtu) {
            var byteArray= packet.ToArray();
            
            var index = 0;
            while (index < byteArray.Length) {
                var length = System.Math.Min(byteArray.Length - index, PacketMtu);
                var newBytes = new byte[length];
                Array.Copy(byteArray, index, newBytes, 0, length);

                SendPacket(new Packet.Packet(newBytes) { ContainsReliableData = updatePacket.ContainsReliableData }, sender);

                index += length;
            }

            return;
        }
        
        SendPacket(packet, sender);
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
    /// Send the given packet over the corresponding medium.
    /// </summary>
    /// <param name="packet">The raw packet instance.</param>
    /// <param name="sender">The transport sender to use.</param>
    private void SendPacket(Packet.Packet packet, ITransportSender sender) {
        sender.Send(packet);
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

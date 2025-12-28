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
internal abstract class UpdateManager<TOutgoing, TPacketId>
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
    private const int ReceiveQueueSize = ConnectionManager.AckSize;

    /// <summary>
    /// Threshold for sequence number wrap-around detection.
    /// </summary>
    private const ushort SequenceWrapThreshold = 32768;

    /// <summary>
    /// The RTT tracker for measuring round-trip times.
    /// Lazily initialized only when transport requires sequencing.
    /// </summary>
    private RttTracker? _rttTracker;

    /// <summary>
    /// The reliability manager for packet loss detection and resending.
    /// Lazily initialized only when transport requires reliability.
    /// </summary>
    private ReliabilityManager<TOutgoing, TPacketId>? _reliabilityManager;

    /// <summary>
    /// The UDP congestion manager instance. Null if congestion management is disabled.
    /// Lazily initialized only when transport requires sequencing.
    /// </summary>
    private CongestionManager<TOutgoing, TPacketId>? _congestionManager;

    /// <summary>
    /// Fixed-size queue containing sequence numbers that have been received.
    /// Lazily initialized only when transport requires sequencing.
    /// </summary>
    private ConcurrentFixedSizeQueue<ushort>? _receivedQueue;

    /// <summary>
    /// Timer for keeping track of when to send an update packet.
    /// </summary>
    private readonly Timer _sendTimer;

    /// <summary>
    /// Timer for keeping track of the connection timing out.
    /// </summary>
    private readonly Timer _heartBeatTimer;

    /// <summary>
    /// Cached capability: whether the transport requires application-level sequencing.
    /// </summary>
    private bool _requiresSequencing = true;

    /// <summary>
    /// Whether the transport requires application-level reliability.
    /// </summary>
    protected bool RequiresReliability { get; private set; } = true;

    /// <summary>
    /// The last sent sequence number.
    /// </summary>
    private ushort _localSequence;

    /// <summary>
    /// The last received sequence number.
    /// </summary>
    private ushort _remoteSequence;

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
    /// The current update packet being assembled.
    /// </summary>
    protected TOutgoing CurrentUpdatePacket { get; private set; }

    /// <summary>
    /// Lock object for synchronizing packet assembly. Protected for subclass access.
    /// </summary>
    protected object Lock { get; } = new();

    /// <summary>
    /// Gets or sets the transport for client-side communication.
    /// Captures transport capabilities when set.
    /// </summary>
    public IEncryptedTransport? Transport {
        set {
            _transportSender = value;
            if (value == null) return;

            _requiresSequencing = value.RequiresSequencing;
            RequiresReliability = value.RequiresReliability;
            InitializeManagersIfNeeded();
        }
    }

    /// <summary>
    /// Sets the transport client for server-side communication.
    /// Captures transport capabilities when set.
    /// </summary>
    public IEncryptedTransportClient? TransportClient {
        set {
            _transportSender = value;
            if (value == null) return;

            _requiresSequencing = value.RequiresSequencing;
            RequiresReliability = value.RequiresReliability;
            InitializeManagersIfNeeded();
        }
    }

    /// <summary>
    /// Lazily initializes managers only when the transport requires them.
    /// This saves memory for Steam connections that don't need sequencing/reliability/congestion managers.
    /// </summary>
    private void InitializeManagersIfNeeded() {
        if (_requiresSequencing) {
            _rttTracker ??= new RttTracker();
            _receivedQueue ??= new ConcurrentFixedSizeQueue<ushort>(ReceiveQueueSize);
            _congestionManager ??= new CongestionManager<TOutgoing, TPacketId>(this, _rttTracker);
        }

        if (RequiresReliability && _rttTracker != null) {
            _reliabilityManager ??= new ReliabilityManager<TOutgoing, TPacketId>(this, _rttTracker);
        }
    }

    /// <summary>
    /// The current send rate in milliseconds between sending packets.
    /// </summary>
    public int CurrentSendRate { get; set; } = CongestionManager<TOutgoing, TPacketId>.HighSendRate;

    /// <summary>
    /// Moving average of round trip time (RTT) between sending and receiving a packet.
    /// Uses RttTracker when available, returns 0 if not initialized (e.g., Steam transport).
    /// </summary>
    public int AverageRtt => _rttTracker != null ? (int) System.Math.Round(_rttTracker.AverageRtt) : 0;

    /// <summary>
    /// Event that is called when the client times out.
    /// </summary>
    public event Action? TimeoutEvent;

    /// <summary>
    /// Construct the update manager with a UDP socket.
    /// </summary>
    protected UpdateManager() {
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
        _heartBeatTimer.Elapsed += (_, _) => TimeoutEvent?.Invoke();
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

        CreateAndSendPacket();

        _sendTimer.Stop();
        _heartBeatTimer.Stop();
    }

    /// <summary>
    /// Callback method for when a packet is received.
    /// </summary>
    /// <param name="packet">The received packet.</param>
    /// <typeparam name="TIncoming">The type of the incoming packet.</typeparam>
    /// <typeparam name="TOtherPacketId">The packet ID type of the incoming packet.</typeparam>
    public void OnReceivePacket<TIncoming, TOtherPacketId>(TIncoming packet)
        where TIncoming : UpdatePacket<TOtherPacketId>
        where TOtherPacketId : Enum {
        // Reset the connection timeout timer
        _heartBeatTimer.Stop();
        _heartBeatTimer.Start();

        // Transports with built-in sequencing (e.g., Steam P2P) bypass app-level sequence/ACK/congestion logic
        if (!_requiresSequencing) {
            return;
        }

        // Transports requiring sequencing: Handle congestion, sequence tracking, and deduplication
        // Notify RTT tracker and reliability manager of received ACKs
        NotifyAckReceived(packet.Ack);

        // Process ACK field efficiently with cached reference
        var ackField = packet.AckField;
        for (ushort i = 0; i < ConnectionManager.AckSize; i++) {
            if (ackField[i]) {
                var sequenceToCheck = (ushort) (packet.Ack - i - 1);
                NotifyAckReceived(sequenceToCheck);
            }
        }

        _congestionManager?.OnReceivePacket();

        var sequence = packet.Sequence;
        // _receivedQueue is guaranteed non-null here:
        // InitializeManagersIfNeeded() initializes it when _requiresSequencing is true
        _receivedQueue!.Enqueue(sequence);

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
    private void CreateAndSendPacket() {
        var rawPacket = new Packet.Packet();
        TOutgoing packetToSend;

        lock (Lock) {
            // Transports requiring sequencing: Configure sequence and ACK data
            if (_requiresSequencing) {
                CurrentUpdatePacket.Sequence = _localSequence;
                CurrentUpdatePacket.Ack = _remoteSequence;
                PopulateAckField();
            }

            try {
                CurrentUpdatePacket.CreatePacket(rawPacket);
            } catch (Exception e) {
                Logger.Error($"Failed to create packet: {e}");
                return;
            }

            // Reset the packet by creating a new instance,
            // but keep the original instance for reliability data re-sending
            packetToSend = CurrentUpdatePacket;
            CurrentUpdatePacket = new TOutgoing();
        }

        // Transports requiring sequencing: Track for RTT, reliability
        if (_requiresSequencing) {
            // _rttTracker is guaranteed non-null here:
            // InitializeManagersIfNeeded() initializes it when _requiresSequencing is true
            _rttTracker!.OnSendPacket(_localSequence);
            if (RequiresReliability) {
                // _reliabilityManager is guaranteed non-null here: InitializeManagersIfNeeded() initializes it
                // when RequiresReliability is true and _rttTracker is non-null (which it is per above)
                _reliabilityManager!.OnSendPacket(_localSequence, packetToSend);
            }

            _localSequence++;
        }

        SendWithFragmentation(rawPacket, packetToSend.ContainsReliableData);
    }

    /// <summary>
    /// Populates the ACK field with acknowledgment bits for recently received packets.
    /// Each bit indicates whether a packet with that sequence number was received.
    /// Only used for UDP/HolePunch transports.
    /// </summary>
    private void PopulateAckField() {
        // _receivedQueue is guaranteed non-null here: this method is only called inside _requiresSequencing blocks,
        // and InitializeManagersIfNeeded() initializes _receivedQueue when _requiresSequencing is true
        var receivedQueue = _receivedQueue!.GetCopy();
        var ackField = CurrentUpdatePacket.AckField;

        for (ushort i = 0; i < ConnectionManager.AckSize; i++) {
            var pastSequence = (ushort) (_remoteSequence - i - 1);
            ackField[i] = receivedQueue.Contains(pastSequence);
        }
    }

    /// <summary>
    /// Sends a packet, fragmenting it into smaller chunks if it exceeds the MTU size.
    /// Fragments are sent sequentially to ensure they can be reassembled by the receiver.
    /// </summary>
    /// <param name="packet">The packet to send, which may be fragmented if too large.</param>
    /// <param name="isReliable">Whether the packet data needs to be delivered reliably.</param>
    private void SendWithFragmentation(Packet.Packet packet, bool isReliable) {
        if (packet.Length <= PacketMtu) {
            SendPacket(packet, isReliable);
            return;
        }

        var data = packet.ToArray();
        var remaining = data.Length;
        var offset = 0;

        while (remaining > 0) {
            var chunkSize = System.Math.Min(remaining, PacketMtu);
            var fragment = new byte[chunkSize];

            Array.Copy(data, offset, fragment, 0, chunkSize);

            // Fragmented packets are only reliable if the original packet was, and we only 
            // set reliability for the first fragment or all? 
            // In this implementation logic, it seems we treated the whole packet as reliable or not.
            // However, typical fragmentation reliability depends on transport. 
            // Assuming for now that if the main packet is reliable, we want to try and send fragments reliably too.
            SendPacket(new Packet.Packet(fragment), isReliable);

            offset += chunkSize;
            remaining -= chunkSize;
        }
    }

    /// <summary>
    /// Notifies RTT tracker and reliability manager that an ACK was received for the given sequence.
    /// </summary>
    /// <param name="sequence">The acknowledged sequence number.</param>
    private void NotifyAckReceived(ushort sequence) {
        _rttTracker?.OnAckReceived(sequence);
        _reliabilityManager?.OnAckReceived(sequence);
    }

    /// <summary>
    /// Callback method for when the send timer elapses. Will create and send a new update packet and update the
    /// timer interval in case the send rate changes.
    /// </summary>
    private void OnSendTimerElapsed(object sender, ElapsedEventArgs elapsedEventArgs) {
        CreateAndSendPacket();

        if (_lastSendRate != CurrentSendRate) {
            _sendTimer.Interval = CurrentSendRate;
            _lastSendRate = CurrentSendRate;
        }
    }

    /// <summary>
    /// Check whether the first given sequence number is greater than the second given sequence number.
    /// Accounts for sequence number wrap-around, by inverse comparison if differences are larger than half
    /// of the sequence number space.
    /// </summary>
    /// <param name="sequence1">The first sequence number to compare.</param>
    /// <param name="sequence2">The second sequence number to compare.</param>
    /// <returns>True if the first sequence number is greater than the second sequence number.</returns>
    private static bool IsSequenceGreaterThan(ushort sequence1, ushort sequence2) {
        return (sequence1 > sequence2 && sequence1 - sequence2 <= SequenceWrapThreshold) ||
               (sequence1 < sequence2 && sequence2 - sequence1 > SequenceWrapThreshold);
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
        var length = buffer.Length;

        switch (_transportSender) {
            case IReliableTransport reliableTransport when isReliable:
                reliableTransport.SendReliable(buffer, 0, length);
                break;

            case IEncryptedTransport transport:
                transport.Send(buffer, 0, length);
                break;

            case IReliableTransportClient reliableTransportClient when isReliable:
                reliableTransportClient.SendReliable(buffer, 0, length);
                break;

            case IEncryptedTransportClient transportClient:
                transportClient.Send(buffer, 0, length);
                break;
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

            if (existingPacketData is not RawPacketDataCollection existingDataCollection) {
                throw new InvalidOperationException("Could not add addon data with existing non-collection data");
            }

            if (packetData is RawPacketDataCollection packetDataAsCollection) {
                existingDataCollection.DataInstances.AddRange(packetDataAsCollection.DataInstances);
            } else {
                existingDataCollection.DataInstances.Add(packetData);
            }
        }
    }

    /// <summary>
    /// Either get or create an AddonPacketData instance for the given addon.
    /// </summary>
    /// <param name="addonId">The ID of the addon.</param>
    /// <param name="packetIdSize">The size of the packet ID space.</param>
    /// <returns>The addon packet data instance.</returns>
    private AddonPacketData GetOrCreateAddonPacketData(byte addonId, byte packetIdSize) {
        if (!CurrentUpdatePacket.TryGetSendingAddonPacketData(addonId, out var addonPacketData)) {
            addonPacketData = new AddonPacketData(packetIdSize);
            CurrentUpdatePacket.SetSendingAddonPacketData(addonId, addonPacketData);
        }

        return addonPacketData;
    }
}

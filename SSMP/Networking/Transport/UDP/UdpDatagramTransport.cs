using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Org.BouncyCastle.Tls;
using SSMP.Logging;

namespace SSMP.Networking.Transport.UDP;

/// <summary>
/// Abstract base class of the client and server datagram transports for DTLS over UDP.
/// </summary>
internal abstract class UdpDatagramTransport : DatagramTransport {

    /// <summary>
    /// Token source for cancelling the blocking call on the received data collection.
    /// </summary>
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    /// <summary>
    /// Cached fully qualified type name used in diagnostics so logging does not repeatedly resolve it via reflection.
    /// </summary>
    private const string TypeName = nameof(UdpEncryptedTransport);
    
    /// <summary>
    /// A thread-safe queue of complete UDP datagrams handed off from the socket receive loop to DTLS.
    /// Each entry represents exactly one received datagram and must never be treated as a stream fragment.
    /// </summary>
    private BlockingCollection<ReceivedData> ReceivedDataCollection { get; } = new();

    /// <summary>
    /// Called by the DTLS stack to dequeue a single datagram and copy it into <paramref name="buf"/>.
    /// If no datagram is available within <paramref name="waitMillis"/>, or if the transport is shutting down,
    /// the method returns <c>-1</c>.
    /// <para><b>Contract:</b></para>
    /// <list type="bullet">
    ///   <item><description>Each <see cref="ReceivedData"/> entry is one full UDP datagram.</description></item>
    ///   <item><description>Callers must pass a buffer of at least <see cref="GetReceiveLimit"/> bytes.</description></item>
    ///   <item><description>Producers must enqueue datagrams no larger than <see cref="GetReceiveLimit"/>.</description></item>
    /// </list>
    /// <para><b>Edge case behavior:</b></para>
    /// <list type="bullet">
    ///   <item><description>If <paramref name="len"/> is smaller than the queued datagram, only the fitting prefix is copied.</description></item>
    ///   <item><description>The remaining bytes are discarded and logged loudly and not re-queued.</description></item>
    /// </list>
    /// <para>
    /// Discard is intentional: UDP/DTLS has datagram semantics, not stream semantics. Re-queuing a tail would
    /// splice it into the next read and corrupt the receive flow. In normal operation this branch is never hit,
    /// because outbound packets are fragmented below 1200 bytes to leave headroom for DTLS overhead under the
    /// cap defined by <see cref="Networking.Client.DtlsClient.MaxPacketSize"/>.
    /// </para>
    /// </summary>
    /// <param name="buf">Destination buffer for the received datagram bytes.</param>
    /// <param name="off">Offset within <paramref name="buf"/> at which to begin writing.</param>
    /// <param name="len">Usable capacity of <paramref name="buf"/> starting at <paramref name="off"/>.</param>
    /// <param name="waitMillis">Milliseconds to block waiting for a datagram before timing out.</param>
    /// <returns>
    /// Bytes copied into <paramref name="buf"/>, or <c>-1</c> if the wait timed out or the transport
    /// was canceled/disposed.
    /// </returns>
    public int Receive(byte[] buf, int off, int len, int waitMillis)
    {
        if (_cancellationTokenSource.IsCancellationRequested)
            return -1;

        try
        {
            if (!ReceivedDataCollection.TryTake(out var data, waitMillis, _cancellationTokenSource.Token))
                return -1;

            if (data.Length > len) {
                Logger.Error(
                    $"DTLS receive buffer smaller than queued datagram in {GetType().FullName}. " +
                    $"Truncating datagram to preserve UDP semantics. packetLength={data.Length}, " +
                    $"availableLength={len}, bufferLength={buf.Length}, offset={off}, " +
                    $"receiveLimit={GetReceiveLimit()}, sendLimit={GetSendLimit()}, " +
                    $"threadId={Environment.CurrentManagedThreadId}"
                );
            }

            // Clamp to available buffer space and drop any excess. Re-queuing the remainder would turn a datagram
            // transport into a fake byte stream and corrupt the next DTLS read.
            var bytesToCopy = System.Math.Min(data.Length, len);
            Array.Copy(data.Buffer, 0, buf, off, bytesToCopy);
            return bytesToCopy;
        }
        catch (OperationCanceledException)  { return -1; }
        /* Mono bug: BlockingCollection can throw ArgumentNullException instead of ObjectDisposedException when disposed during TryTake. */
        catch (ArgumentNullException)       { return -1; }
        catch (ObjectDisposedException)     { return -1; }
    }

    /// <summary>
    /// Validates and enqueues one complete received datagram for later DTLS consumption.
    /// This is the producer-side choke point for enforcing the receive contract.
    /// </summary>
    /// <param name="buffer">Backing buffer containing the datagram bytes.</param>
    /// <param name="length">Number of valid bytes in <paramref name="buffer"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the enqueue operation.</param>
    /// <param name="source">Human-readable source context for diagnostics.</param>
    /// <returns><see langword="true"/> if the datagram was enqueued; otherwise <see langword="false"/>.</returns>
    public bool TryEnqueueReceivedData(byte[] buffer, int length, CancellationToken cancellationToken, [CallerMemberName] string source = "")
    {
        // Unigned cast collapses the negative-length and exceeds-buffer checks into one branch.
        // A negative length wraps to a large uint, which is always bigger than the buffer.Length.
        if ((uint)length > (uint)buffer.Length)
        {
            Logger.Error(
                $"Refusing to enqueue datagram with invalid length in {TypeName}. " +
                $"source={source}, length={length}, bufferLength={buffer.Length}"
            );
            return false;
        }

        var receiveLimit = GetReceiveLimit();
        if (length > receiveLimit)
        {
            Logger.Error(
                $"Refusing to enqueue datagram larger than receive limit in {TypeName}. " +
                $"source={source}, length={length}, receiveLimit={receiveLimit}"
            );
            return false;
        }

        try
        {
            ReceivedDataCollection.Add(new ReceivedData { Buffer = buffer, Length = length }, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (InvalidOperationException)  { return false; }
    }
    
    /// <summary>
    /// The maximum number of bytes to receive in a single call to <see cref="Receive"/>.
    /// </summary>
    /// <returns>The maximum number of bytes that can be received.</returns>
    public abstract int GetReceiveLimit();

    /// <summary>
    /// The maximum number of bytes to send in a single call to <see cref="Send"/>.
    /// </summary>
    /// <returns>The maximum number of bytes that can be sent.</returns>
    public abstract int GetSendLimit();

    /// <summary>
    /// This method is called whenever the corresponding DtlsTransport's Send is called.
    /// </summary>
    /// <param name="buf">Byte array containing the bytes to send.</param>
    /// <param name="off">The offset in the buffer at which to start sending bytes.</param>
    /// <param name="len">The number of bytes to send.</param>
    public abstract void Send(byte[] buf, int off, int len);

    /// <summary>
    /// Cleanup login for when this transport channel should be closed.
    /// </summary>
    public void Close() {
        _cancellationTokenSource.Cancel();
    }
    
    /// <summary>
    /// Dispose of the underlying unmanaged resources.
    /// </summary>
    public void Dispose() {
        _cancellationTokenSource.Dispose();
        ReceivedDataCollection.Dispose();
    }
    
    /// <summary>
    /// One received UDP datagram.
    /// <see cref="Length"/> may be smaller than <see cref="Buffer"/>.Length, but it must never describe bytes from
    /// more than one datagram.
    /// </summary>
    public class ReceivedData {
        /// <summary>
        /// Byte array containing the data.
        /// </summary>
        public required byte[] Buffer { get; init; }
        /// <summary>
        /// The number of bytes in the buffer.
        /// </summary>
        public required int Length { get; init; }
    }
}

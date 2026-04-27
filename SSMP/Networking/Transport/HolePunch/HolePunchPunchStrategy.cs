namespace SSMP.Networking.Transport.HolePunch;

/// <summary>
/// Client-side UDP hole-punch cadence used before and during DTLS connection establishment.
/// </summary>
internal enum HolePunchPunchStrategy {
    /// <summary>
    /// Sends a small warmup burst immediately, then continues with steady punching while DTLS handshakes.
    /// </summary>
    WarmupBurstThenSteady,

    /// <summary>
    /// Sends the full punch window as an evenly spaced steady stream before DTLS starts.
    /// </summary>
    LegacySteadyStream
}

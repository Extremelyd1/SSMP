namespace MMS.Models;

/// <summary>
/// Shared constants for the strict matchmaking rendezvous protocol.
/// </summary>
internal static class MatchmakingProtocol {
    /// <summary>
    /// The current matchmaking protocol version required for MMS matchmaking operations.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Error code returned when a client must update before using matchmaking.
    /// </summary>
    public const string UpdateRequiredErrorCode = "update_required";

    /// <summary>
    /// Offset applied before issuing synchronized <c>start_punch</c> instructions.
    /// </summary>
    public const int PunchTimingOffsetMs = 250;
}

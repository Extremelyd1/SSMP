using System;

namespace SSMP.Networking.Transport.Common;

/// <summary>
/// Represents a unique identifier for a client connection.
/// Each transport provides its own implementation (e.g., IPEndPoint for UDP, SteamID for Steam P2P).
/// Implementing this interface allows type-safe client identification without string parsing.
/// </summary>
internal interface IClientIdentifier : IEquatable<IClientIdentifier> {
    /// <summary>
    /// Returns a human-readable string representation for logging and display.
    /// </summary>
    string ToDisplayString();

    /// <summary>
    /// Gets a key used for throttling connection attempts (e.g., IP address).
    /// Returns null if application-level throttling should be skipped for this client.
    /// </summary>
    object? ThrottleKey { get; }
}

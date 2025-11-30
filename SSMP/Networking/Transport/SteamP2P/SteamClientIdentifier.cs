using System;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking.Transport.SteamP2P;

/// <summary>
/// Steam P2P implementation of <see cref="IClientIdentifier"/> that wraps a Steam ID.
/// </summary>
internal class SteamClientIdentifier : IClientIdentifier {
    /// <summary>
    /// The underlying Steam ID for this client.
    /// Steam IDs are 64-bit unsigned integers uniquely identifying Steam users.
    /// </summary>
    public ulong SteamId { get; }

    /// <summary>
    /// Constructs a new Steam client identifier from a Steam ID.
    /// </summary>
    /// <param name="steamId">The 64-bit Steam ID representing this client.</param>
    /// <exception cref="ArgumentException">Thrown if steamId is 0 (invalid).</exception>
    public SteamClientIdentifier(ulong steamId) {
        if (steamId == 0) {
            throw new ArgumentException("Steam ID cannot be 0", nameof(steamId));
        }
        SteamId = steamId;
    }

    /// <inheritdoc />
    public string ToDisplayString() => $"Steam:{SteamId}";

    /// <inheritdoc />
    public object? ThrottleKey => null;

    /// <inheritdoc />
    public bool Equals(IClientIdentifier? other) {
        return other is SteamClientIdentifier steam && SteamId == steam.SteamId;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as IClientIdentifier);

    /// <inheritdoc />
    public override int GetHashCode() => SteamId.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => ToDisplayString();
}

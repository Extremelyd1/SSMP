using MMS.Models;

namespace MMS.Features.Matchmaking;

/// <summary>
/// Shared matchmaking protocol version validation helpers.
/// </summary>
internal static class MatchmakingVersionValidation
{
    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="matchmakingVersion"/> matches the current protocol version.
    /// </summary>
    /// <param name="matchmakingVersion">The client-reported matchmaking protocol version.</param>
    public static bool Validate(int? matchmakingVersion) =>
        matchmakingVersion == MatchmakingProtocol.CurrentVersion;

    /// <summary>
    /// Parses and validates a matchmaking version from a query string value.
    /// </summary>
    /// <param name="matchmakingVersion">Raw query string value.</param>
    /// <returns><see langword="true"/> if the version is present and matches the current protocol.</returns>
    public static bool TryValidate(string? matchmakingVersion) =>
        int.TryParse(matchmakingVersion, out var parsedVersion) &&
        parsedVersion == MatchmakingProtocol.CurrentVersion;
}

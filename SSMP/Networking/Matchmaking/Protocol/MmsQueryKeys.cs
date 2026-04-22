namespace SSMP.Networking.Matchmaking.Protocol;

/// <summary>
/// Query-string parameter keys appended to MMS request URLs.
/// </summary>
internal static class MmsQueryKeys {
    /// <summary>The type of lobby (e.g. Matchmaking or Steam).</summary>
    public const string Type = "type";

    /// <summary>The version of the matchmaking protocol being used by the client.</summary>
    public const string MatchmakingVersion = "matchmakingVersion";
}

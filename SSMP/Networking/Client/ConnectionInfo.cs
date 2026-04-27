namespace SSMP.Networking.Client;

/// <summary>
/// Contains connection information for matchmaking lobbies, including fallback capability.
/// </summary>
internal readonly struct ConnectionInfo {
    public string PrimaryIp { get; }
    public int PrimaryPort { get; }
    public string? FallbackAddress { get; }
    public string FeedbackMessage { get; }
    public bool UseLanDirectPath { get; }

    public ConnectionInfo(
        string primaryIp,
        int primaryPort,
        string? fallbackAddress,
        string feedbackMessage,
        bool useLanDirectPath
    ) {
        PrimaryIp = primaryIp;
        PrimaryPort = primaryPort;
        FallbackAddress = fallbackAddress;
        FeedbackMessage = feedbackMessage;
        UseLanDirectPath = useLanDirectPath;
    }
}

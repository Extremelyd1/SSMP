using System;

namespace SSMP.Networking.Matchmaking;

/// <summary>
/// Static bridge for coordinating punch-back between MmsClient and server transport.
/// </summary>
internal static class PunchCoordinator {
    /// <summary>
    /// Event fired when a client needs punch-back.
    /// Parameters: clientIp, clientPort
    /// </summary>
    public static event Action<string, int>? PunchClientRequested;

    /// <summary>
    /// Request punch to a client endpoint.
    /// </summary>
    public static void RequestPunch(string clientIp, int clientPort) {
        PunchClientRequested?.Invoke(clientIp, clientPort);
    }
}

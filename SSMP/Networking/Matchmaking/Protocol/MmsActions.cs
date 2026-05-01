namespace SSMP.Networking.Matchmaking.Protocol;

/// <summary>
/// WebSocket <c>action</c> field values exchanged between the MMS server
/// and both host and client connections.
/// </summary>
internal static class MmsActions {
    /// <summary>Sent by the server to a joining client to initiate its NAT mapping process.</summary>
    public const string BeginClientMapping = "begin_client_mapping";

    /// <summary>Sent by the server to a host to request it to start hole punching towards a client.</summary>
    public const string StartPunch = "start_punch";

    /// <summary>Sent by the client to the server once its NAT mapping has been successfully determined.</summary>
    public const string ClientMappingReceived = "client_mapping_received";

    /// <summary>Sent by the server to a client when a join request has failed.</summary>
    public const string JoinFailed = "join_failed";

    /// <summary>Sent by the server to a host to request a refresh of its NAT mapping.</summary>
    public const string RefreshHostMapping = "refresh_host_mapping";

    /// <summary>Sent by the host to the server once its NAT mapping has been successfully refreshed or determined.</summary>
    public const string HostMappingReceived = "host_mapping_received";
}

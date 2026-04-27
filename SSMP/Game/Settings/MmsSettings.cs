namespace SSMP.Game.Settings;

/// <summary>
/// Settings related to the MatchMaking Server (MMS).
/// </summary>
internal class MmsSettings {
    /// <summary>
    /// The URL of the MatchMaking Server (MMS).
    /// Default points to a domain name for the standard MMS.
    /// </summary>
    public string MmsUrl { get; set; } = "https://mms.ssmp.gg";

    /// <summary>The UDP port used for NAT discovery.</summary>
    public int UdpDiscoveryPort { get; set; } = 5001;

    /// <summary>
    /// Optional local IPv4 address to bind gameplay UDP sockets to before discovery and DTLS.
    /// Leave empty to let the OS choose the local interface.
    /// </summary>
    public string? LocalBindIp { get; set; }

    /// <summary>
    /// Whether the client should prefer a LAN address returned by MMS over the public matchmaking endpoint.
    /// Disable this for NAT simulation labs where clients must always use the routed/public path.
    /// </summary>
    public bool PreferLanFastPath { get; set; } = true;

    /// <summary>
    /// Optional LAN IPv4 address to advertise to MMS for same-network fast-path connections.
    /// Leave empty to auto-detect from the OS-selected outbound interface.
    /// </summary>
    public string? HostLanIpOverride { get; set; }

    /// <summary>
    /// Optional IPv4 address sent only during matchmaking lobby creation to override the host endpoint MMS stores.
    /// This is an advanced multi-NIC / NAT-lab escape hatch for environments where the HTTP connection source IP is
    /// not the address other clients should target. Leave empty to let MMS infer the host IP from the create-lobby
    /// request connection as usual.
    /// </summary>
    public string? HostIpOverride { get; set; }

    /// <summary>
    /// The version of the MMS URL entry. This version will be updated in this variable when a new domain name
    /// is being used for the MMS server. Then, if the version that is saved in the mod settings on disk is older than
    /// the default version and the URL is the old one, we can safely update the URL.
    /// If the user however uses a different value for the URL, but the version is outdated, we cannot update the URL
    /// and only update the version to the new one. The responsibility is then on the user to put the URL to the
    /// new one if they want to use it.
    /// </summary>
    public int Version { get; set; } = 1;
}

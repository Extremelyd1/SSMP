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

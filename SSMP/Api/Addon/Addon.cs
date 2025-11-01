namespace SSMP.Api.Addon;

/// <summary>
/// Abstract base class for addons.
/// </summary>
public abstract class Addon {
    /// <summary>
    /// The version of the API that is currently exposed by SSMP.
    /// </summary>
    public const uint CurrentApiVersion = 1; 

    /// <summary>
    /// The maximum length of the name string for an addon.
    /// </summary>
    public const int MaxNameLength = 20;

    /// <summary>
    /// The maximum length of the version string for an addon.
    /// </summary>
    public const int MaxVersionLength = 10;

    /// <summary>
    /// The internal ID assigned to this addon.
    /// </summary>
    internal byte? Id { get; set; }

    /// <summary>
    /// The network sender object if it has been registered.
    /// </summary>
    internal object? NetworkSender;

    /// <summary>
    /// The network receiver object if it has been registered.
    /// </summary>
    internal object? NetworkReceiver;

    /// <summary>
    /// The version of the API that this addon targets. If SSMP deems this too low (i.e. too outdated), it will not
    /// load the addon. The current version of the API that SSMP exposes is referenced in
    /// <see cref="CurrentApiVersion"/>. Do not reference that value in this property directly. This property is to
    /// ensure that outdated addons that target APIs with removed/changed features are not loaded anymore to prevent
    /// issues.
    /// </summary>
    public abstract uint ApiVersion { get; }
}

namespace MMS.Bootstrap;

/// <summary>
/// Stores runtime application state that needs to be shared across startup helpers and endpoint mappings.
/// </summary>
internal static class ProgramState {
    /// <summary>
    /// Gets or sets a value indicating whether the application is running in a development environment.
    /// </summary>
    public static bool IsDevelopment { get; internal set; }

    /// <summary>
    /// Gets or sets the application-level logger after the host has been built.
    /// </summary>
    public static ILogger Logger { get; internal set; } = null!;

    public static int DiscoveryPort => 5001;
}

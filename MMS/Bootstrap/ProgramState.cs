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
    /// Gets or sets the shared application logger.
    /// Assigned by <see cref="Program"/> before HTTPS configuration runs and
    /// later replaced with the built host logger after application startup completes.
    /// </summary>
    public static ILogger Logger { get; internal set; } = null!;

    /// <summary>
    /// Gets the fixed UDP port used for discovery packets.
    /// </summary>
    public static int DiscoveryPort => 5001;
}

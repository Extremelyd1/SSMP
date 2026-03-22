using MMS.Bootstrap;
using System.Net;

namespace MMS.Services.Utility;

/// <summary>
/// Provides utilities for formatting network endpoints in a way that is
/// safe for logging — full detail in development, redacted in all other environments.
/// </summary>
public static class PrivacyFormatter {
    /// <summary>
    /// Gets a value indicating whether the application is running in the development environment.
    /// </summary>
    private static bool IsDevelopment => ProgramState.IsDevelopment;

    /// <summary>
    /// Gets a redaction placeholder string. Returns a random humorous variant with
    /// 1% probability; otherwise returns <c>[Redacted]</c>.
    /// </summary>
    private static string RedactedPlaceholder => "[Redacted]";

    /// <summary>
    /// Formats an <see cref="IPEndPoint"/> for logging.
    /// Returns the full <c>address:port</c> string in development;
    /// otherwise returns <c>[Redacted]</c>.
    /// </summary>
    /// <param name="endPoint">The endpoint to format.</param>
    /// <returns>A log-safe string representation of the endpoint.</returns>
    public static string Format(IPEndPoint? endPoint) =>
        IsDevelopment
            ? endPoint?.ToString() ?? "<null>"
            : RedactedPlaceholder;

    /// <summary>
    /// Formats a raw endpoint string (e.g. a hostname or connection string excerpt)
    /// for logging.
    /// </summary>
    /// <param name="endPoint">A string representation of the endpoint.</param>
    /// <returns>A log-safe string representation of the endpoint.</returns>
    public static string Format(string? endPoint) =>
        IsDevelopment
            ? endPoint ?? "<null>"
            : RedactedPlaceholder;
}

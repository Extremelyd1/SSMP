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
    /// A collection of humorous placeholder strings used as redaction substitutes.
    /// </summary>
    private static readonly string[] RedactedVariants = [
        "[None of your business]",
        "[Nice try]",
        "[A place far, far away]",
        "[A galaxy far, far away]",
        "[Over the rainbow]",
        "[Somewhere out there]",
        "[If I told you, I'd have to delete you]",
        "[Ask your mom]",
        "[Classified]",
        "[Behind you]",
        "[¯\\_(ツ)_/¯]",
        "[What are you looking for?]",
        "[Error 404: Address Not Found]",
        "[It's a secret... to everybody]",
        "[Have you tried turning it off and on again?]",
        "[SomewhereOnEarth™]",
        "[Undisclosed location]",
        "[The void]",
        "[Mind your own business.exe]",
        "[This information self-destructed]",
        "[The FBI knows, but they won't tell you either]",
        "[Schrodinger's Endpoint: both here and not here]",
        "[Currently unavailable due to something idk]",
        "[My other endpoint is a Porsche]",
        "[Would you like to know more? Too bad :)]",
        "[Somewhere between 0.0.0.0 and 255.255.255.255]",
        "[Location redacted by the order of the cats]",
        "[Lost in the abyss that lies in-between the couch cushions]",
        "[In a parallel universe, slightly to the left]",
        "[sudo show address -> Permission denied]",
        "[This endpoint does not exist. Never did. Move along.]",
        "[Carrier pigeon lost en route]",
        "[The address is a lie]",
        "[Currently on vacation. Please try again never.]",
        "[I am not the endpoint you are looking for]",
        "[¿Qué? No hablo 'your concern'.]",
        "[It's giving... nothing. IT'S GIVING NOTHING!]",
        "[Endpoint entered witness protection]",
        "[We asked it nicely to stay hidden]",
        "[Loading... just kidding]",
        "[no]",
        "[This message will self-destruct in... oh wait, it already did]",
        "[Somewhere, where the WiFi is better]",
        "[Behind seven proxies]",
        "[In the cloud (no, not that cloud, the fluffy kind)]",
        "[Ask Clippy]",
        "[Encrypted with vibes]",
        "[This field intentionally left blank - lol no it isn't]",
        "[IP? More like... never mind.]",
        "[Gone fishing 🎣]",
        "[The address got up and walked away]",
        "[We hid it really well this time]",
        "[Not on this network. Possibly not on this planet.]",
        "[git blame won't help you here]",
        "[According to my lawyer, no comment]",
        "[Why do you ask? 👀]",
        "[Hidden in plain sight... except not plain, and not in sight]",
        "[Bounced through 47 VPNs and counting...48..49]"
    ];

    /// <summary>
    /// Gets a redaction placeholder string. Returns a random humorous variant with
    /// 1% probability; otherwise returns <c>[Redacted]</c>.
    /// </summary>
    private static string RedactedPlaceholder =>
        Random.Shared.NextDouble() < 0.50
            ? RedactedVariants[Random.Shared.Next(RedactedVariants.Length)]
            : "[Redacted]";

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

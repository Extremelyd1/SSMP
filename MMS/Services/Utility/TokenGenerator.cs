using System.Security.Cryptography;

namespace MMS.Services.Utility;

/// <summary>
/// Generates random tokens and lobby codes using a cryptographically secure RNG.
/// </summary>
internal static class TokenGenerator {
    private const string TokenChars = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const string LobbyCodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>Fixed length of all generated lobby codes.</summary>
    private const int LobbyCodeLength = 6;

    /// <summary>
    /// Generates a random URL-safe token of the requested length.
    /// Characters are drawn from lowercase alphanumerics (<c>a-z0-9</c>).
    /// </summary>
    /// <param name="length">Number of characters in the returned token.</param>
    /// <returns>A random lowercase alphanumeric string of <paramref name="length"/> characters.</returns>
    public static string GenerateToken(int length) =>
        string.Create(
            length, 0, (span, _) => {
                for (var i = 0; i < span.Length; i++)
                    span[i] = TokenChars[RandomNumberGenerator.GetInt32(TokenChars.Length)];
            }
        );

    /// <summary>
    /// Generates a unique <see cref="LobbyCodeLength"/>-character lobby code that does not
    /// already exist in <paramref name="existingCodes"/>.
    /// Characters are drawn from uppercase alphanumerics (<c>A-Z0-9</c>).
    /// </summary>
    /// <param name="existingCodes">The current set of live lobby codes used for collision detection.</param>
    /// <returns>A unique uppercase alphanumeric lobby code.</returns>
    public static string GenerateUniqueLobbyCode(IReadOnlySet<string> existingCodes) {
        const int maxRetries = 100;

        string code;
        var retries = 0;
        do {
            if (retries++ >= maxRetries)
                throw new InvalidOperationException("Failed to generate a unique lobby code after 100 attempts.");

            code = string.Create(
                LobbyCodeLength, 0, (span, _) => {
                    for (var i = 0; i < span.Length; i++)
                        span[i] = LobbyCodeChars[RandomNumberGenerator.GetInt32(LobbyCodeChars.Length)];
                }
            );
        } while (existingCodes.Contains(code));

        return code;
    }
}

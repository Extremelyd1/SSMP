using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MMS.Bootstrap;

/// <summary>
/// Configures Kestrel HTTPS bindings from PEM certificate files in the working directory.
/// </summary>
internal static class HttpsCertificateConfigurator {
    /// <summary>
    /// The filename of the PEM-encoded certificate.
    /// </summary>
    private const string CertFile = "cert.pem";

    /// <summary>
    /// The filename of the PEM-encoded private key.
    /// </summary>
    private const string KeyFile = "key.pem";

    /// <summary>
    /// Static logger instance for the configurator.
    /// </summary>
    private static readonly ILogger Logger;

    /// <summary>
    /// Initializes static members of the <see cref="HttpsCertificateConfigurator"/> class.
    /// </summary>
    static HttpsCertificateConfigurator() {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(o => {
                    o.SingleLine = true;
                    o.IncludeScopes = false;
                    o.TimestampFormat = "HH:mm:ss ";
                }
            )
        );
        Logger = loggerFactory.CreateLogger(nameof(HttpsCertificateConfigurator));
    }

    /// <summary>
    /// Reads <c>cert.pem</c> and <c>key.pem</c> from the working directory and configures
    /// Kestrel to terminate TLS with that certificate on port 5000.
    /// </summary>
    /// <param name="builder">The web application builder to configure.</param>
    /// <returns>
    /// <see langword="true"/> if the certificate was loaded and Kestrel was configured;
    /// <see langword="false"/> if either file is missing, unreadable, or malformed.
    /// </returns>
    public static bool TryConfigure(WebApplicationBuilder builder) {
        if (!TryReadPemFiles(out var pem, out var key))
            return false;

        if (!TryCreateCertificate(pem, key, out var certificate))
            return false;

        builder.WebHost.ConfigureKestrel(server =>
            server.ListenAnyIP(5000, listen => listen.UseHttps(certificate!))
        );

        return true;
    }

    /// <summary>
    /// Reads the PEM certificate and key files from the working directory.
    /// </summary>
    /// <param name="pem">The contents of <c>cert.pem</c> if successful.</param>
    /// <param name="key">The contents of <c>key.pem</c> if successful.</param>
    /// <returns>
    /// <see langword="true"/> if both files were read successfully; otherwise <see langword="false"/>.
    /// </returns>
    private static bool TryReadPemFiles(out string pem, out string key) {
        pem = key = string.Empty;

        if (!File.Exists(CertFile)) {
            Logger.LogError("Certificate file '{File}' does not exist", CertFile);
            return false;
        }

        if (!File.Exists(KeyFile)) {
            Logger.LogError("Key file '{File}' does not exist", KeyFile);
            return false;
        }

        try {
            pem = File.ReadAllText(CertFile);
            key = File.ReadAllText(KeyFile);
            return true;
        } catch (Exception e) {
            Logger.LogError(e, "Could not read '{CertFile}' or '{KeyFile}'", CertFile, KeyFile);
            return false;
        }
    }

    /// <summary>
    /// Attempts to construct an <see cref="X509Certificate2"/> from PEM-encoded certificate
    /// and key material.
    /// </summary>
    /// <param name="pem">The PEM-encoded certificate.</param>
    /// <param name="key">The PEM-encoded private key.</param>
    /// <param name="certificate">The resulting certificate if successful; otherwise <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> if the certificate was created successfully otherwise <see langword="false"/>.
    /// </returns>
    private static bool TryCreateCertificate(string pem, string key, out X509Certificate2? certificate) {
        certificate = null;
        try {
            using var ephemeralCertificate = X509Certificate2.CreateFromPem(pem, key);
            var pkcs12 = ephemeralCertificate.Export(X509ContentType.Pkcs12);
            certificate = X509CertificateLoader.LoadPkcs12(
                pkcs12,
                password: null,
                X509KeyStorageFlags.PersistKeySet |
                X509KeyStorageFlags.MachineKeySet |
                X509KeyStorageFlags.Exportable
            );
            return true;
        } catch (CryptographicException e) {
            Logger.LogError(e, "Could not create certificate from PEM files");
            return false;
        }
    }
}

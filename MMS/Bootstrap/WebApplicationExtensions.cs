namespace MMS.Bootstrap;

/// <summary>
/// Extension methods for configuring the MMS middleware pipeline.
/// </summary>
internal static class WebApplicationExtensions
{
    /// <summary>
    /// Applies the MMS middleware pipeline and binds the listener URL.
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    /// <param name="isDevelopment">Whether the app is running in development.</param>
    /// <returns>The same web application for chaining.</returns>
    public static void UseMmsPipeline(this WebApplication app, bool isDevelopment)
    {
        if (isDevelopment)
            app.UseHttpLogging();
        else
            app.UseExceptionHandler("/error");

        app.UseForwardedHeaders();
        app.UseRateLimiter();
        app.UseWebSockets();
        app.Urls.Add(isDevelopment ? "http://0.0.0.0:5000" : "https://0.0.0.0:5000");
    }

    /// <summary>
    /// Configures HTTPS for MMS when not running in development.
    /// </summary>
    /// <param name="builder">The web application builder to configure.</param>
    /// <param name="isDevelopment">Whether the app is running in development.</param>
    /// <returns>
    /// <see langword="true"/> when startup can continue; otherwise <see langword="false"/>.
    /// </returns>
    public static bool TryConfigureMmsHttps(this WebApplicationBuilder builder, bool isDevelopment) =>
        isDevelopment || HttpsCertificateConfigurator.TryConfigure(builder);
}

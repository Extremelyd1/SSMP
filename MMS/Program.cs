using MMS.Bootstrap;
using MMS.Features;

namespace MMS;

/// <summary>
/// Entry point and composition root for the MatchMaking Server.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class Program {
    /// <summary>
    /// Application entry point.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args) {
        var builder = WebApplication.CreateBuilder(args);
        var isDevelopment = builder.Environment.IsDevelopment();
        using var startupLoggerFactory = CreateStartupLoggerFactory();

        ProgramState.IsDevelopment = isDevelopment;
        ProgramState.Logger = startupLoggerFactory.CreateLogger(nameof(Program));

        builder.Services.AddMmsCoreServices();
        builder.Services.AddMmsInfrastructure(builder.Configuration, isDevelopment);

        if (!builder.TryConfigureMmsHttps(isDevelopment)) {
            ProgramState.Logger.LogCritical("MMS HTTPS configuration failed, exiting");
            return;
        }

        var app = builder.Build();
        ProgramState.Logger = app.Logger;

        app.UseMmsPipeline(isDevelopment);
        app.MapMmsEndpoints();
        app.Run();
    }

    /// <summary>
    /// Creates the temporary logger factory used before the ASP.NET Core host logger is available.
    /// </summary>
    /// <returns>A simple console logger factory for early startup diagnostics.</returns>
    private static ILoggerFactory CreateStartupLoggerFactory() {
        return LoggerFactory.Create(logging => logging.AddSimpleConsole(options => {
                    options.SingleLine = true;
                    options.IncludeScopes = false;
                    options.TimestampFormat = "HH:mm:ss ";
                }
            )
        );
    }
}

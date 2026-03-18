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

        // ProgramState is initialized once during startup
        // and treated as read-only thereafter.
        ProgramState.IsDevelopment = isDevelopment;

        builder.Services.AddMmsCoreServices();
        builder.Services.AddMmsInfrastructure(builder.Configuration, isDevelopment);

        if (!builder.TryConfigureMmsHttps(isDevelopment)) {
            using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole());
            loggerFactory.CreateLogger(nameof(Program))
                         .LogCritical("MMS HTTPS configuration failed, exiting");
            return;
        }

        var app = builder.Build();
        // ProgramState.Logger is assigned once after the host is built,
        // it should only be read after this point.
        ProgramState.Logger = app.Logger;

        app.UseMmsPipeline(isDevelopment);
        app.MapMmsEndpoints();
        app.Run();
    }
}

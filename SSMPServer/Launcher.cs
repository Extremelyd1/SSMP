using SSMP.Game.Settings;
using SSMP.Logging;
using SSMP.Networking.Packet;
using SSMP.Networking.Server;
using SSMPServer.Command;
using SSMPServer.Logging;

namespace SSMPServer;

/// <summary>
/// Launcher class with the entry point for the program.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class Launcher {
    /// <summary>
    /// Main entry point for the SSMP Server program.
    /// </summary>
    /// <param name="args">Command line arguments for the server.</param>
    public static void Main(string[] args) {
        var consoleInputManager = new ConsoleInputManager();
        var consoleLogger = new ConsoleLogger(consoleInputManager);
        Logger.AddLogger(consoleLogger);
        Logger.AddLogger(new RollingFileLogger());

        var hasPortArg = false;
        var port = -1;

        if (args.Length > 0) {
            if (string.IsNullOrEmpty(args[0]) || !ParsePort(args[0], out port)) {
                Logger.Info("Invalid port, should be an integer between 0 and 65535");
                return;
            }

            hasPortArg = true;
        }

        var loadedServerSettings = ConfigManager.LoadServerSettings(out var serverSettings);
        serverSettings ??= new ServerSettings();

        if (!loadedServerSettings) {
            Logger.Info("Server settings did not exist yet, creating new server settings file");

            ConfigManager.SaveServerSettings(serverSettings);
        }

        // Load the console settings and note whether they existed or not
        var consoleSettingsExisted = ConfigManager.LoadConsoleSettings(out var consoleSettings);
        consoleSettings ??= new ConsoleSettings();
        
        // If the user supplied a port on the arguments to the program, we override the loaded settings with
        // the port
        if (hasPortArg) {
            consoleSettings.Port = port;
        }

        // If the settings did not yet exist, we now save the settings possibly with the argument provided port
        if (!consoleSettingsExisted) {
            Logger.Info("Console settings did not exist yet, creating new console settings file");

            ConfigManager.SaveConsoleSettings(consoleSettings);
        }

        StartServer(consoleSettings, serverSettings, consoleInputManager, consoleLogger);
    }

    /// <summary>
    /// Will start the server with the given port and server settings.
    /// </summary>
    /// <param name="consoleSettings">The console settings for the program.</param>
    /// <param name="serverSettings">The server settings for the server.</param>
    /// <param name="consoleInputManager">The input manager for command-line input.</param>
    /// <param name="consoleLogger">The logging class for logging to console.</param>
    private static void StartServer(
        ConsoleSettings consoleSettings,
        ServerSettings serverSettings,
        ConsoleInputManager consoleInputManager,
        ConsoleLogger consoleLogger
    ) {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
        var version = fvi.FileVersion;
        
        Logger.Info($"Starting server v{version}");

        var packetManager = new PacketManager();

        var netServer = new NetServer(packetManager);

        var serverManager = new ConsoleServerManager(netServer, packetManager, serverSettings, consoleLogger);
        serverManager.Initialize();
        serverManager.Start(consoleSettings.Port, consoleSettings.FullSynchronisation);

        // TODO: make an event in ServerManager that we can register for so we know when the server shuts down
        consoleInputManager.ConsoleInputEvent += input => {
            Logger.Info(input);
            if (!serverManager.TryProcessCommand(new ConsoleCommandSender(), "/" + input)) {
                Logger.Info($"Unknown command: {input}");
            }
        };
        consoleInputManager.Start();
    }

    /// <summary>
    /// Try to parse the given input as a networking port.
    /// </summary>
    /// <param name="input">The string to parse.</param>
    /// <param name="port">Will be set to the parsed port if this method returns true, or 0 if the method
    /// returns false.</param>
    /// <returns>True if the given input was parsed as a valid port, false otherwise.</returns>
    private static bool ParsePort(string input, out int port) {
        if (!int.TryParse(input, out port)) {
            return false;
        }

        if (!IsValidPort(port)) {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true if the given port is a valid networking port.
    /// </summary>
    /// <param name="port">The port to check.</param>
    /// <returns>True if the port is valid, false otherwise.</returns>
    private static bool IsValidPort(int port) {
        return port is >= 0 and <= 65535;
    }
}

using SSMP.Logging;
using SSMPServer.Command;

namespace SSMPServer.Logging;

/// <summary>
/// Logger implementation to log to console.
/// </summary>
internal class ConsoleLogger : BaseLogger {
    /// <summary>
    /// The console input manager for managing console input while writing output.
    /// </summary>
    private readonly ConsoleInputManager _consoleInputManager;

    /// <summary>
    /// The log levels that will be logged to the console.
    /// </summary>
    public readonly HashSet<Level> LoggableLevels;

    public ConsoleLogger(ConsoleInputManager consoleInputManager) {
        _consoleInputManager = consoleInputManager;
        LoggableLevels = [
            Level.Error,
            Level.Warn,
            Level.Info
        ];
    }

    /// <summary>
    /// Format a message for console output by turning its color codes into ANSI codes, followed by a reset
    /// code so the color does not carry over to the next output.
    /// </summary>
    private string FormatForConsole(string message) {
        return ColorCodeParser.ParseToAnsi(message) + "\e[0m";
    }

    /// <summary>
    /// Write a message to the console if the given level is loggable.
    /// </summary>
    /// <param name="level">The log level of the message.</param>
    /// <param name="message">The message to log.</param>
    private void Log(Level level, string message) {
        if (!LoggableLevels.Contains(level) || !ShouldLogMessage(message)) {
            return;
        }

        var tag = level.ToString().ToUpperInvariant();
#if DEBUG
        _consoleInputManager.WriteLine($"[{tag}] [{GetOriginClassName()}] {FormatForConsole(message)}");
#else
        _consoleInputManager.WriteLine($"[{tag}] {FormatForConsole(message)}");
#endif
    }

    /// <inheritdoc />
    public override void Info(string message) => Log(Level.Info, message);

    /// <inheritdoc />
    public override void Message(string message) => Log(Level.Message, message);

    /// <inheritdoc />
    public override void Debug(string message) => Log(Level.Debug, message);

    /// <inheritdoc />
    public override void Warn(string message) => Log(Level.Warn, message);

    /// <inheritdoc />
    public override void Error(string message) => Log(Level.Error, message);

    public enum Level {
        Error,
        Warn,
        Info,
        Message,
        Debug
    }
}

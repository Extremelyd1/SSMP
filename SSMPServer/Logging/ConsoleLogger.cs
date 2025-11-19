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

    /// <summary>
    /// Whether to enable color/formatting code parsing for console output.
    /// </summary>
    private bool _enableColorParsing = true;

    public ConsoleLogger(ConsoleInputManager consoleInputManager) {
        _consoleInputManager = consoleInputManager;
        LoggableLevels = [
            Level.Error,
            Level.Warn,
            Level.Info
        ];
    }

    /// <summary>
    /// Set whether to enable color parsing. Set to false if your console doesn't support ANSI codes.
    /// </summary>
    /// <param name="enabled">Whether to enable color parsing.</param>
    public void SetColorParsingEnabled(bool enabled) {
        _enableColorParsing = enabled;
    }

    /// <summary>
    /// Format a message for console output, applying ANSI color codes if enabled.
    /// Always appends a reset code when colors are used.
    /// When disabled, color codes are stripped to avoid raw tokens in output.
    /// </summary>
    private string FormatForConsole(string message) {
        if (_enableColorParsing) {
            return ColorCodeParser.ParseToAnsi(message) + "\x1b[0m";
        }
        return ColorCodeParser.StripColorCodes(message);
    }

    /// <inheritdoc />
    public override void Info(string message) {
        if (!LoggableLevels.Contains(Level.Info) || !ShouldLogMessage(message)) {
            return;
        }

#if DEBUG
        _consoleInputManager.WriteLine($"[INFO] [{GetOriginClassName()}] {FormatForConsole(message)}");
#else
        _consoleInputManager.WriteLine($"[INFO] {FormatForConsole(message)}");
#endif
    }

    /// <inheritdoc />
    public override void Message(string message) {
        if (!LoggableLevels.Contains(Level.Message) || !ShouldLogMessage(message)) {
            return;
        }

#if DEBUG
        _consoleInputManager.WriteLine($"[MESSAGE] [{GetOriginClassName()}] {FormatForConsole(message)}");
#else
        _consoleInputManager.WriteLine($"[MESSAGE] {FormatForConsole(message)}");
#endif
    }

    /// <inheritdoc />
    public override void Debug(string message) {
        if (!LoggableLevels.Contains(Level.Debug) || !ShouldLogMessage(message)) {
            return;
        }

#if DEBUG
        _consoleInputManager.WriteLine($"[DEBUG] [{GetOriginClassName()}] {FormatForConsole(message)}");
#else
        _consoleInputManager.WriteLine($"[DEBUG] {FormatForConsole(message)}");
#endif
    }

    /// <inheritdoc />
    public override void Warn(string message) {
        if (!LoggableLevels.Contains(Level.Warn) || !ShouldLogMessage(message)) {
            return;
        }

#if DEBUG
        _consoleInputManager.WriteLine($"[WARN] [{GetOriginClassName()}] {FormatForConsole(message)}");
#else
        _consoleInputManager.WriteLine($"[WARN] {FormatForConsole(message)}");
#endif
    }

    /// <inheritdoc />
    public override void Error(string message) {
        if (!LoggableLevels.Contains(Level.Error) || !ShouldLogMessage(message)) {
            return;
        }

#if DEBUG
        _consoleInputManager.WriteLine($"[ERROR] [{GetOriginClassName()}] {FormatForConsole(message)}");
#else
        _consoleInputManager.WriteLine($"[ERROR] {FormatForConsole(message)}");
#endif
    }

    public enum Level {
        Error,
        Warn,
        Info,
        Message,
        Debug
    }
}

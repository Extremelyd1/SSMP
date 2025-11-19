using System;
using System.Diagnostics;

namespace SSMP.Logging;

/// <summary>
/// Abstract base class for loggers that prepends messages with their log level and origin class.
/// </summary>
public abstract class BaseLogger : ILogger {
    /// <summary>
    /// Whether to enable color/formatting code parsing. Default is true.
    /// </summary>
    protected bool EnableColorParsing { get; set; } = true;

    /// <summary>
    /// Get the class name of the object that called the log function in which this method is used. Will skip
    /// classes in the stack frame that are within the "SSMP.Logging" namespace.
    /// Note that this method is prone to breaking if namespace changes or stack frame changes for logging occur.
    /// </summary>
    /// <returns>The full class name of the origin object or name of the method if no such object exists.</returns>
    protected static string GetOriginClassName() {
        string typeString;
        Type? declaringType;
        var skipFrames = 3;

        do {
            var methodBase = new StackFrame(skipFrames, false).GetMethod();

            declaringType = methodBase.DeclaringType;
            if (declaringType == null) {
                return methodBase.Name;
            }

            skipFrames++;
            typeString = declaringType.ToString();
        } while (
            declaringType.Module.Name.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase)
            || (declaringType.Namespace != null && declaringType.Namespace.StartsWith("SSMP.Logging"))
        );

        return typeString;
    }

    /// <summary>
    /// Process a message for color/formatting codes if enabled.
    /// Automatically appends a reset code at the end to prevent color bleeding.
    /// </summary>
    /// <param name="message">The raw message.</param>
    /// <returns>The processed message.</returns>
    protected string ProcessMessage(string message) {
        if (!EnableColorParsing) {
            return message;
        }
        
        var processed = ColorCodeParser.ParseToAnsi(message);
        // Always append ANSI reset at the end to prevent color bleeding
        return processed + "\x1b[0m";
    }

    /// <summary>
    /// Check if a message should be logged (not null, empty, or whitespace).
    /// </summary>
    /// <param name="message">The message to check.</param>
    /// <returns>True if the message should be logged, false otherwise.</returns>
    protected static bool ShouldLogMessage(string message) {
        return !string.IsNullOrWhiteSpace(message);
    }

    /// <summary>
    /// Strip color codes from a message.
    /// </summary>
    /// <param name="message">The message with color codes.</param>
    /// <returns>The message without color codes.</returns>
    protected string StripColorCodes(string message) {
        return ColorCodeParser.StripColorCodes(message);
    }

    /// <inheritdoc />
    public abstract void Info(string message);

    /// <inheritdoc />
    public abstract void Message(string message);

    /// <inheritdoc />
    public abstract void Debug(string message);

    /// <inheritdoc />
    public abstract void Warn(string message);

    /// <inheritdoc />
    public abstract void Error(string message);
}

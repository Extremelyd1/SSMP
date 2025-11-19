using SSMP;

namespace SSMP.Logging;

/// <summary>
/// Logger class for logging to BepInEx.
/// </summary>
public class BepInExLogger : BaseLogger {
    /// <summary>
    /// The BepInEx manual log source to log information to.
    /// </summary>
    private readonly BepInEx.Logging.ManualLogSource _logSource;

    public BepInExLogger() {
        _logSource = BepInEx.Logging.Logger.CreateLogSource(SSMPPlugin.Name);
    }

    /// <summary>
    /// Set whether to enable color parsing. Set to false if your console doesn't support ANSI codes.
    /// </summary>
    /// <param name="enabled">Whether to enable color parsing.</param>
    public void SetColorParsingEnabled(bool enabled) {
        EnableColorParsing = enabled;
    }
    
    /// <inheritdoc />
    public override void Info(string message) {
        if (!ShouldLogMessage(message)) {
            return;
        }
        
        _logSource.LogInfo($"[{GetOriginClassName()}] {ProcessMessage(message)}");
    }

    /// <inheritdoc />
    public override void Message(string message) {
        if (!ShouldLogMessage(message)) {
            return;
        }
        
        _logSource.LogMessage($"[{GetOriginClassName()}] {ProcessMessage(message)}");
    }

    /// <inheritdoc />
    public override void Debug(string message) {
        if (!ShouldLogMessage(message)) {
            return;
        }
        
        _logSource.LogDebug($"[{GetOriginClassName()}] {ProcessMessage(message)}");
    }

    /// <inheritdoc />
    public override void Warn(string message) {
        if (!ShouldLogMessage(message)) {
            return;
        }
        
        _logSource.LogWarning($"[{GetOriginClassName()}] {ProcessMessage(message)}");
    }

    /// <inheritdoc />
    public override void Error(string message) {
        if (!ShouldLogMessage(message)) {
            return;
        }
        
        _logSource.LogError($"[{GetOriginClassName()}] {ProcessMessage(message)}");
    }
}

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
    
    /// <inheritdoc />
    public override void Info(string message) {
        _logSource.LogInfo($"[{GetOriginClassName()}] {message}");
    }

    /// <inheritdoc />
    public override void Message(string message) {
        _logSource.LogMessage($"[{GetOriginClassName()}] {message}");
    }

    /// <inheritdoc />
    public override void Debug(string message) {
        _logSource.LogDebug($"[{GetOriginClassName()}] {message}");
    }

    /// <inheritdoc />
    public override void Warn(string message) {
        _logSource.LogWarning($"[{GetOriginClassName()}] {message}");
    }

    /// <inheritdoc />
    public override void Error(string message) {
        _logSource.LogError($"[{GetOriginClassName()}] {message}");
    }
}
namespace SSMP.Api.Command;

/// <summary>
/// Interface for client and server-side commands.
/// </summary>
public interface ICommand {
    /// <summary>
    /// The trigger for this command, can include command prefix (such as "/").
    /// </summary>
    string Trigger { get; }

    /// <summary>
    /// Aliases for this command, can include command prefix (such as "/").
    /// </summary>
    string[] Aliases { get; }

    /// <summary>
    /// The description of this command.
    /// </summary>
    string Description => "";
}

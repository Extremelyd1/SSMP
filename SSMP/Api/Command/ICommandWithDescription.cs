namespace SSMP.Api.Command;

/// <summary>
/// Optional capability interface for commands that provide a description.
/// Implement on your command to expose a description; otherwise consumers should fallback to empty.
/// </summary>
public interface ICommandWithDescription
{
    /// <summary>
    /// The description of this command.
    /// </summary>
    string Description { get; }
}

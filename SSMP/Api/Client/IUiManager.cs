namespace SSMP.Api.Client;

/// <summary>
/// UI manager that handles all UI related interaction.
/// </summary>
public interface IUiManager {
    /// <summary>
    /// The message box that shows information related to SSMP.
    /// </summary>
    IChatBox ChatBox { get; }
}


namespace SSMP.Api.Client;

/// <summary>
/// The message box in the bottom right of the screen that shows information related to SSMP.
/// </summary>
public interface IChatBox {
    /// <summary>
    /// Whether the chat is currently open.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Add a message to the chat box.
    /// </summary>
    /// <param name="message">The string containing the message.</param>
    void AddMessage(string message);
}

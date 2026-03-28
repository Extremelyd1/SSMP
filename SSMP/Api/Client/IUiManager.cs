using System;

namespace SSMP.Api.Client;

/// <summary>
/// UI manager that handles all UI related interaction.
/// </summary>
public interface IUiManager {
    /// <summary>
    /// The message box that shows information related to SSMP.
    /// </summary>
    IChatBox ChatBox { get; }

    /// <summary>
    /// Fired when the multiplayer button is pressed, before any blocking hooks run.
    /// Use this for fire-and-forget reactions such as logging or showing a notification.
    /// </summary>
    event Action? MultiplayerButtonPressed;

    /// <summary>
    /// Registers a hook that is invoked when the multiplayer button is pressed.
    /// </summary>
    /// <remarks>
    /// Hooks are executed in reverse order (last registered runs first).
    ///
    /// Example (informational hook):
    /// <code>
    /// RegisterMultiplayerMenuHook(next => {
    ///     MyNonMandatoryDependencyPopup.Show(
    ///         "Addon X unavailable",
    ///         onAccept:  _ => {continue;  },
    ///         onDecline: _ => { continue; });
    ///     });
    /// </code>
    ///
    /// Example (blocking hook):
    /// <code>
    /// RegisterMultiplayerMenuHook(next => {
    ///     MyMandatoryDependencyPopup.Show(
    ///         "Addon X unavailable",
    ///         onConfirm: agreed => { if (agreed) continue; });
    ///         onDecline: agreed => { if (!agreed) return;  });
    ///     });
    /// </code>
    /// </remarks>
    /// <param name="hook">
    /// The hook to register. Receives a callback to invoke when ready to proceed.
    /// </param>
    void RegisterMultiplayerMenuHook(Action<Action> hook);
}

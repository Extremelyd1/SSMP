using SSMP.Api.Command;
using SSMP.Api.Command.Client;
using SSMP.Ui;

namespace SSMP.Game.Command.Client;

/// <summary>
/// Command to open Steam's invite dialog for sending lobby invites.
/// Primarily used for private lobbies where friends can't "Join Game".
/// </summary>
internal class InviteCommand : IClientCommand, ICommandWithDescription {
    /// <inheritdoc />
    public string Trigger => "/invite";

    /// <inheritdoc />
    public string[] Aliases => ["/inv"];
    
    /// <inheritdoc />
    public string Description => "Open Steam's invite dialog to invite friends to your lobby.";

    /// <inheritdoc />
    public void Execute(string[] arguments) {
        if (!SteamManager.IsInitialized) {
            UiManager.InternalChatBox.AddMessage("Steam is not available.");
            return;
        }

        if (!SteamManager.IsHostingLobby) {
            UiManager.InternalChatBox.AddMessage("You must be hosting a Steam lobby to invite players.");
            return;
        }

        SteamManager.OpenInviteDialog();
        UiManager.InternalChatBox.AddMessage("Opening Steam invite dialog...");
    }
}

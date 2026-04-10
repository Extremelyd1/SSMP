using System;
using SSMP.Game;

namespace SSMP.Api.Client;

/// <summary>
/// Player manager that handles information about players, such as the local player's team or changes to other players'
/// teams.
/// </summary>
public interface IPlayerManager {
    /// <summary>
    /// The team that our local player is on.
    /// </summary>
    Team LocalPlayerTeam { get; }

    /// <summary>
    /// Event that is called when the local player's team changes.
    /// </summary>
    event Action<Team>? LocalPlayerTeamChangeEvent;

    /// <summary>
    /// Event that is called when any player's (including the local player's) team changes.
    /// </summary>
    event Action<IClientPlayer, Team>? PlayerTeamChangeEvent;
}

using System;
using System.Collections.Generic;

namespace SSMP.Api.Client;

/// <summary>
/// Settings related to the client/mod that are accessible to addons.
/// </summary>
public interface IModSettings {
    /// <summary>
    /// Event triggered whenever any of the mod settings are changed.
    /// </summary>
    event Action<string>? ChangedEvent;

    /// <summary>
    /// The last used address to join a server.
    /// </summary>
    string ConnectAddress { get; }

    /// <summary>
    /// The last used port to join a server.
    /// </summary>
    int ConnectPort { get; }

    /// <summary>
    /// The username of the player.
    /// </summary>
    string Username { get; }

    /// <summary>
    /// Whether to display a UI element for the ping.
    /// </summary>
    bool DisplayPing { get; }

    /// <summary>
    /// Whether full synchronization of bosses, enemies, worlds, and saves is enabled.
    /// </summary>
    bool FullSynchronisation { get; }
}

using SSMP.Api.Server;
using SSMP.Game.Server.Auth;
using SSMP.Internals;
using SSMP.Math;

namespace SSMP.Game.Server;

/// <inheritdoc />
internal class ServerPlayerData : IServerPlayer {
    /// <inheritdoc />
    public ushort Id { get; }

    /// <inheritdoc />
    public string UniqueClientIdentifier { get; }

    /// <inheritdoc />
    public string AuthKey { get; }

    /// <inheritdoc />
    public bool IsAuthorized => _authorizedList.Contains(AuthKey);

    /// <inheritdoc />
    public string Username { get; }

    /// <inheritdoc />
    public string CurrentScene { get; set; }

    /// <inheritdoc />
    public Vector2? Position { get; set; }

    /// <inheritdoc />
    public bool Scale { get; set; }

    /// <inheritdoc />
    public bool HasMapIcon { get; set; }

    /// <inheritdoc />
    public Vector2? MapPosition { get; set; }

    /// <inheritdoc />
    public ushort AnimationId { get; set; }

    /// <inheritdoc />
    public Team Team { get; set; } = Team.None;

    /// <inheritdoc />
    public byte SkinId { get; set; }

    /// <inheritdoc />
    public CrestType CrestType { get; set; } = CrestType.Hunter;
    
    /// <summary>
    /// Whether this player is the host of their current scene.
    /// </summary>
    public bool IsSceneHost { get; set; }

    /// <summary>
    /// Reference of the authorized list for checking whether this player is authorized.
    /// </summary>
    private readonly AuthKeyList _authorizedList;

    /// <summary>
    /// Constructs new server player data given ID, name, auth key and reference of authorized list.
    /// </summary>
    /// <param name="id">The ID of the player.</param>
    /// <param name="uniqueClientIdentifier">The SteamID or IP Address of the client as a string.</param>
    /// <param name="username">The username of the player.</param>
    /// <param name="authKey">The authentication key of the player.</param>
    /// <param name="authorizedList">A reference to the authorized list of the server.</param>
    public ServerPlayerData(
        ushort id,
        string uniqueClientIdentifier,
        string username,
        string authKey,
        AuthKeyList authorizedList
    ) {
        Id = id;
        UniqueClientIdentifier = uniqueClientIdentifier;
        Username = username;
        AuthKey = authKey;

        CurrentScene = "";

        _authorizedList = authorizedList;
    }
}

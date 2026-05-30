using SSMP.Api.Client;
using SSMP.Internals;
using UnityEngine;

namespace SSMP.Game.Client;

/// <inheritdoc />
internal class ClientPlayerData : IClientPlayer {
    /// <inheritdoc />
    public required ushort Id { get; init; }

    /// <inheritdoc />
    public required string Username { get; init; }

    /// <inheritdoc />
    public bool IsInLocalScene { get; set; }

    /// <inheritdoc />
    public GameObject? PlayerContainer { get; set; }

    /// <inheritdoc />
    public GameObject? PlayerObject { get; set; }

    /// <inheritdoc />
    public Team Team { get; set; }

    /// <inheritdoc />
    public byte SkinId { get; set; }

    /// <inheritdoc />
    public CrestType CrestType { get; set; }
}

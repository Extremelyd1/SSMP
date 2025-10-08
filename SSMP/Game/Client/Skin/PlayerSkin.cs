using UnityEngine;

namespace SSMP.Game.Client.Skin;

/// <summary>
/// Data class for player skin textures.
/// </summary>
internal class PlayerSkin {
    /// <summary>
    /// Whether this skin contains the hornet texture.
    /// </summary>
    public bool HasHornetTexture { get; private set; }

    /// <summary>
    /// The hornet texture for the skin, or null if it does not have it.
    /// </summary>
    public Texture? HornetTexture { get; private set; }

    /// <summary>
    /// Whether this skin contains the sprint texture.
    /// </summary>
    public bool HasSprintTexture { get; private set; }

    /// <summary>
    /// The sprint texture for the skin, or null if it does not have it.
    /// </summary>
    public Texture? SprintTexture { get; private set; }

    /// <summary>
    /// Set the hornet texture for the skin.
    /// </summary>
    /// <param name="hornetTexture">The hornet texture.</param>
    public void SetHornetTexture(Texture hornetTexture) {
        HornetTexture = hornetTexture;
        HasHornetTexture = true;
    }

    /// <summary>
    /// Set the sprint texture for the skin.
    /// </summary>
    /// <param name="sprintTexture">The sprint texture.</param>
    public void SetSprintTexture(Texture sprintTexture) {
        SprintTexture = sprintTexture;
        HasSprintTexture = true;
    }
}

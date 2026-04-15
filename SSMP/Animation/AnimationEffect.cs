using SSMP.Game.Settings;
using SSMP.Internals;
using UnityEngine;

namespace SSMP.Animation;

/// <summary>
/// Abstract base class for animation effects.
/// </summary>
internal abstract class AnimationEffect : IAnimationEffect {
    /// <summary>
    /// The current <see cref="ServerSettings"/> instance.
    /// </summary>
    protected ServerSettings ServerSettings = null!;

    /// <inheritdoc/>
    public abstract void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo);

    /// <inheritdoc/>
    public abstract byte[]? GetEffectInfo();

    /// <inheritdoc/>
    public void SetServerSettings(ServerSettings serverSettings) {
        ServerSettings = serverSettings;
    }

    /// <summary>
    /// Locate the damages_enemy FSM and change the attack direction to the given direciton. This will ensure that
    /// enemies are getting knocked back in the correct direction from remote player's attacks.
    /// </summary>
    /// <param name="targetObject">The target GameObject to change.</param>
    /// <param name="direction">The direction in float that the damage is coming from.</param>
    protected static void ChangeAttackDirection(GameObject targetObject, float direction) {
        var damageFsm = targetObject.LocateMyFSM("damages_enemy");
        if (damageFsm == null) {
            return;
        }
        
        // Find the variable that controls the slash direction for damaging enemies
        var directionVar = damageFsm.FsmVariables.GetFsmFloat("direction");
        directionVar.Value = direction;
    }

    /// <summary>
    /// "Hides" the player character by stopping its animation and setting its sprite to a small texture
    /// </summary>
    /// <param name="playerObject">The player to be hidden.</param>
    protected static void HidePlayer(GameObject playerObject) {
        // "hide" the player (assign a very small texture)
        playerObject.GetComponent<tk2dSpriteAnimator>().Stop();
        playerObject.GetComponent<tk2dSprite>().SetSprite("wall_puff0004");
    }
}

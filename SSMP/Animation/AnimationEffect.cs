using SSMP.Game.Settings;
using SSMP.Internals;
using SSMP.Util;
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
    /// Locate the damages_enemy FSM and change the attack direction to the given direction. This will ensure that
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

    /// <summary>
    /// Gets the Effects object for a given player
    /// </summary>
    /// <param name="playerObject">The player using the effect</param>
    /// <returns>The player's effects object</returns>
    protected static GameObject GetPlayerEffects(GameObject playerObject) {
        var effects = playerObject.FindGameObjectInChildren("Effects");
        if (effects == null) {
            effects = new GameObject("Effects");
            effects.transform.SetParentReset(playerObject.transform);
        }

        return effects;
    }

    /// <summary>
    /// Attempts to get or create an effect from the Effects sub-object
    /// </summary>
    /// <param name="playerObject">The player using the effect</param>
    /// <param name="effectName">The name of the effect object</param>
    /// <param name="effect">The effect, if found or created</param>
    /// <returns>true if created, false otherwise</returns>
    protected static bool TryGetEffect(GameObject playerObject, string effectName, out GameObject? effect) {
        // Find or create effects for player
        var effects = GetPlayerEffects(playerObject);

        // Find existing effect
        effect = effects.FindGameObjectInChildren(effectName);
        if (effect != null) {
            return false;
        }

        // Create new effect
        var localEffects = HeroController.instance.gameObject.FindGameObjectInChildren("Effects");
        if (localEffects == null) {
            return false;
        }

        var localEffect = localEffects.FindGameObjectInChildren(effectName);
        if (localEffect == null) {
            return false;
        }

        effect = Object.Instantiate(localEffect, effects.transform);
        effect.name = effectName;

        return true;
    }
}

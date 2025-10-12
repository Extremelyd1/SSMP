using SSMP.Internals;
using UnityEngine;
using UnityEngine.Events;

namespace SSMP.Animation;

/// <summary>
/// Abstract base class for animation effects that can deal damage to other players.
/// </summary>
internal abstract class DamageAnimationEffect : AnimationEffect {
    /// <summary>
    /// Whether this effect should deal damage.
    /// </summary>
    protected bool ShouldDoDamage;

    /// <inheritdoc/>
    public abstract override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo);

    /// <inheritdoc/>
    public abstract override byte[]? GetEffectInfo();

    /// <summary>
    /// Sets whether this animation effect should deal damage.
    /// </summary>
    /// <param name="shouldDoDamage">The new boolean value.</param>
    public void SetShouldDoDamage(bool shouldDoDamage) {
        ShouldDoDamage = shouldDoDamage;
    }

    /// <summary>
    /// Adds a <see cref="DamageHero"/> component to the given game object that deals the given damage when the player
    /// collides with it.
    /// </summary>
    /// <param name="target">The target game object to attach the component to.</param>
    /// <param name="damage">The number of mask of damage it should deal.</param>
    protected static void AddDamageHeroComponent(GameObject target, int damage = 1) {
        var damageHero = target.AddComponent<DamageHero>();
        damageHero.damageDealt = damage;
        damageHero.OnDamagedHero = new UnityEvent();
    }
}

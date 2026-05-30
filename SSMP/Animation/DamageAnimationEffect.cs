using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using UnityEngine.Events;

namespace SSMP.Animation;

/// <summary>
/// Abstract base class for animation effects that can deal damage to other players.
/// </summary>
internal abstract class DamageAnimationEffect : AnimationEffect {
    /// <summary>
    /// The object layer for attacks.
    /// </summary>
    protected const int AttackLayer = (int) GlobalEnums.PhysLayers.HERO_ATTACK;

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
    /// collides with it. Also adds a <see cref="EffectOwnerComponent"/> component that indicates the owner of this
    /// object.
    /// </summary>
    /// <param name="target">The target game object to attach the component to.</param>
    /// <param name="damage">The number of mask of damage it should deal.</param>
    /// <returns>The <see cref="DamageHero"/> component that was added to the game object</returns>
    protected static DamageHero AddDamageHeroComponent(GameObject target, int damage = 1) {
        var damageHero = target.AddComponentIfNotPresent<DamageHero>();
        damageHero.damageDealt = damage;
        damageHero.OnDamagedHero = new UnityEvent();

        var identifier = target.AddComponentIfNotPresent<EffectOwnerComponent>();
        identifier.Owner = target;

        return damageHero;
    }

    /// <summary>
    /// Removes a <see cref="DamageHero"/> component from the given game object.
    /// </summary>
    /// <param name="target">The target game object to detach the component from.</param>
    private static void RemoveDamageHeroComponent(GameObject target) {
        target.DestroyComponent<DamageHero>();
    }

    /// <summary>
    /// Adds or removes a <see cref="DamageHero"/> component from the given game object,
    /// depending on the PVP and team settings.
    /// </summary>
    /// <param name="target">The target game object to attach or remove the component from.</param>
    /// <param name="damage">The number of mask of damage it should deal.</param>
    /// <returns>The <see cref="DamageHero"/> component that was added if PVP was turned on</returns>
    protected DamageHero? SetDamageHeroState(GameObject target, int damage = 1) {
        return SetDamageHeroState(target, ServerSettings.IsPvpEnabled && ShouldDoDamage, damage);
    }

    /// <summary>
    /// Adds or removes a <see cref="DamageHero"/> component from the given game object,
    /// depending on the PVP and team settings.
    /// </summary>
    /// <param name="target">The target game object to attach or remove the component from.</param>
    /// <param name="damage">The number of mask of damage it should deal.</param>
    /// <param name="doDamage">If the damager should be enabled or not</param>
    /// <returns>The <see cref="DamageHero"/> component that was added if PVP was turned on</returns>
    public static DamageHero? SetDamageHeroState(GameObject target, bool doDamage, int damage = 1) {
        if (doDamage && damage > 0) {
            return AddDamageHeroComponent(target, damage);
        }

        RemoveDamageHeroComponent(target);
        return null;
    }

    /// <summary>
    /// Fixes a remote attack's <see cref="DamageEnemies"/> component by disabling various properties that would
    /// interfere with the local player.
    /// </summary>
    /// <param name="target">The object with the <see cref="DamageEnemies"/> component.</param>
    protected static void FixDamageEnemies(GameObject target) {
        // Add if we want to disable enemy damage
        //if (!ServerSettings.AllowDamageEnemies) {
        //    target.DestroyComponent<DamageEnemies>();
        //}

        if (target.TryGetComponent<DamageEnemies>(out var damageEnemies)) {
            damageEnemies.doesNotTink = true;
            damageEnemies.doesNotTinkThroughWalls = true;
            damageEnemies.doesNotParry = true;
            damageEnemies.silkGeneration = HitSilkGeneration.None;
        }
    }
}

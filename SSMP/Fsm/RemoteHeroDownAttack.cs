using System.Collections.Generic;
using GlobalEnums;
using UnityEngine;

namespace SSMP.Fsm;

/// <summary>
/// Variant of <see cref="HeroDownAttack"/> as a component on remote player attacks.
/// Copied from <see cref="HeroDownAttack"/> and modified to remove code that causes side-effects on the local player.
/// </summary>
public class RemoteHeroDownAttack : MonoBehaviour {
    private DamageEnemies _damageEnemies = null!;
    private Collider2D? _lastCollider;
    private readonly HashSet<GameObject> _damagedGameObjects = [];

    private void Awake() {
        _damageEnemies = GetComponent<DamageEnemies>();
        
        _damageEnemies.HitResponded += OnHitResponded;
    }

    private void OnTriggerEnter2D(Collider2D otherCollider) {
        if (!otherCollider) return;

        var otherColliderGo = otherCollider.gameObject;
        var layer = (PhysLayers) otherColliderGo.layer;
        var flag = _damageEnemies.manualTrigger && layer == PhysLayers.ENEMIES;
        if ((flag || layer == PhysLayers.INTERACTIVE_OBJECT || layer == PhysLayers.TERRAIN ||
             otherColliderGo.GetComponent<TinkEffect>() ||
             otherColliderGo.GetComponentInParent<ToolBoomerang>()) &&
            !flag && layer != PhysLayers.INTERACTIVE_OBJECT && layer != PhysLayers.HERO_ATTACK) {
            return;
        }

        _lastCollider = otherCollider;
        ContinueBounceTrigger(otherCollider.gameObject);
        _lastCollider = null;
    }

    private void ContinueBounceTrigger(GameObject otherObj) {
        if (HeroDownAttack.IsNonBounce(otherObj)) return;

        var component = otherObj.GetComponent<DamageHero>();
        if (component) {
            if (component.hazardType == HazardType.SPIKES) return;

            if (_damageEnemies) {
                if (_lastCollider != null) {
                    _damageEnemies.TryDoDamage(_lastCollider);
                } else if (_damagedGameObjects.Add(component.gameObject)) {
                    _damageEnemies.DoDamage(component.gameObject);
                }
            }
        }
    }

    private void OnHitResponded(DamageEnemies.HitResponse hitResponse) {
        switch ((PhysLayers) hitResponse.Target.layer) {
            case PhysLayers.TERRAIN:
            case PhysLayers.SOFT_TERRAIN:
                break;
            default:
                if (HeroDownAttack.IsNonBounce(hitResponse.Target))
                    break;

                ContinueBounceTrigger(hitResponse.Target);
                break;
        }
    }
}

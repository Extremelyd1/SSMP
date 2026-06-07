using System;
using System.Collections.Generic;
using System.Linq;
using SSMP.Util;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

#pragma warning disable CS0618 // Type or member is obsolete

namespace SSMP.Game.Client.Entity;

/// <summary>
/// Finds scene objects that may match entity registry entries.
/// </summary>
internal class EntitySceneDiscovery {
    /// <summary>
    /// Find candidate game objects to check for entity registration in the given scene.
    /// </summary>
    /// <param name="scene">The scene to scan.</param>
    /// <returns>Candidate game objects in the scene.</returns>
    public List<GameObject> FindCandidates(Scene scene) {
        var enemyObjects = Object.FindObjectsOfType<EnemyDeathEffects>()
                                 .Where(e => e.gameObject.scene == scene)
                                 .SelectMany(GetEnemyObjects);

        var fsmObjects = Object.FindObjectsOfType<PlayMakerFSM>(true)
                               .Where(fsm => fsm.gameObject.scene == scene)
                               .Select(fsm => fsm.gameObject);

        var expandedObjects = enemyObjects
                              .Concat(fsmObjects)
                              .SelectMany(obj => obj == null ? [] : obj.GetChildren().Prepend(obj));

        var componentObjects =
            Object.FindObjectsOfType<Climber>(true).Select(c => c.gameObject)
                  .Concat(Object.FindObjectsOfType<Walker>(true).Select(w => w.gameObject));

        return expandedObjects
               .Concat(componentObjects)
               .Where(obj => obj.scene == scene)
               .Distinct()
               .ToList();
    }

    /// <summary>
    /// Expand an enemy object into every object that should be considered during entity discovery.
    /// </summary>
    /// <param name="effects">The enemy death-effects component used as the discovery root.</param>
    /// <returns>The live enemy object and, when available, its pre-instantiated corpse object.</returns>
    private static IEnumerable<GameObject> GetEnemyObjects(EnemyDeathEffects effects) {
        try {
            effects.PreInstantiate();
        } catch (Exception) {
            // PersonalObjectPool-based enemies cannot be pre-instantiated this early;
            // fall back to tracking only the base GameObject.
            return [effects.gameObject];
        }

        // TODO: CorpsePrefab is a prefab reference and may not be compatible with the original code path.
        return [effects.gameObject, effects.CorpsePrefab];
    }
}

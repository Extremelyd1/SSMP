using System;
using System.Collections.Generic;
using System.Linq;
using SSMP.Util;
using SSMP.Game.Client.Entity.Action;
using SSMP.Game.Client.Entity.Component;
using SSMP.Networking.Client;
using SSMP.Networking.Packet.Data;
using UnityEngine.SceneManagement;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS0618 // Type or member is obsolete

namespace SSMP.Game.Client.Entity;

/// <summary>
/// Manager class that handles entity creation, updating, networking and destruction.
/// </summary>
internal class EntityManager {
    /// <summary>
    /// The net client for networking.
    /// </summary>
    private readonly NetClient _netClient;

    /// <summary>
    /// Dictionary mapping entity IDs to their respective entity instances.
    /// </summary>
    private readonly Dictionary<ushort, Entity> _entities;

    // Updates buffered when the target entity or the scene host role aren't ready yet.
    private readonly Queue<BaseEntityUpdate> _deferredUpdates;

    // True once InitializeSceneHost or InitializeSceneClient has completed for the current scene.
    private bool _isSceneHostDetermined;

    /// <summary>
    /// Whether the client user is the scene host.
    /// </summary>
    public bool IsSceneHost { get; private set; }

    public EntityManager(NetClient netClient) {
        _netClient = netClient;
        _entities = new Dictionary<ushort, Entity>();
        _deferredUpdates = new Queue<BaseEntityUpdate>();
    }

    /// <summary>
    /// Initialize the entity manager by initializing the processor and action hooks.
    /// </summary>
    public void Initialize() {
        EntityProcessor.Initialize(_entities, _netClient);
    }

    /// <summary>
    /// Register the hooks for entity-related operations.
    /// </summary>
    public void RegisterHooks() {
        FsmActionHooks.RegisterHooks();
        MusicComponent.RegisterHooks();

        EntityFsmActions.EntitySpawnEvent += OnGameObjectSpawned;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnSceneChanged;
    }

    /// <summary>
    /// Deregister the hooks for entity-related operations.
    /// </summary>
    public void DeregisterHooks() {
        FsmActionHooks.DeregisterHooks();
        MusicComponent.DeregisterHooks();

        EntityFsmActions.EntitySpawnEvent -= OnGameObjectSpawned;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnSceneChanged;
        ClearEntities();
    }

    /// <summary>
    /// Initializes the entity manager if we are the scene host.
    /// </summary>
    public void InitializeSceneHost() => InitializeSceneRole(isHost: true);

    /// <summary>
    /// Initializes the entity manager if we are a scene client.
    /// </summary>
    public void InitializeSceneClient() => InitializeSceneRole(isHost: false);

    private void InitializeSceneRole(bool isHost) {
        Logger.Info(
            isHost
                ? "We are scene host, releasing control of all registered entities"
                : "We are scene client, taking control of all registered entities"
        );

        IsSceneHost = isHost;

        foreach (var entity in _entities.Values) {
            if (isHost) entity.InitializeHost();
            else entity.InitializeClient();
        }

        _isSceneHostDetermined = true;
        CheckDeferredUpdates();
    }

    /// <summary>
    /// Transitions all registered entities to host-controlled after the local player is promoted to scene host.
    /// Each entity is isolated in a try/catch: one failure must not abort the rest, since a partially-applied
    /// host transfer produces a silent desync that is extremely hard to diagnose.
    /// </summary>
    public void BecomeSceneHost() {
        Logger.Info("Becoming scene host");

        IsSceneHost = true;

        foreach (var entity in _entities.Values) {
            try {
                entity.MakeHost();
            } catch (Exception e) {
                Logger.Error($"Exception while making entity ({entity.Id}, {entity.Type}) a host entity: {e}");
            }
        }
    }

    /// <summary>
    /// Spawns an entity received from the network, using an existing entity of the same spawning type as a template.
    /// The specific instance doesn't matter — FSMs and components are identical across instances of the same type.
    /// </summary>
    /// <param name="id">The ID of the entity.</param>
    /// <param name="spawningType">The type of the entity that spawned the new entity.</param>
    /// <param name="spawnedType">The type of the spawned entity.</param>
    public void SpawnEntity(ushort id, EntityType spawningType, EntityType spawnedType) {
        Logger.Info($"Trying to spawn entity with ID {id} with types: {spawningType}, {spawnedType}");

        if (_entities.ContainsKey(id)) {
            Logger.Info($"  Entity with ID {id} already exists, assuming it has been spawned by action");
            return;
        }

        var spawningEntity = _entities.Values.FirstOrDefault(e => e.Type == spawningType);
        if (spawningEntity == null) {
            Logger.Warn("Could not find entity with same type for spawning");
            return;
        }

        var spawnedObject = EntitySpawner.SpawnEntityGameObject(
            spawningType,
            spawnedType,
            spawningEntity.Object.Client,
            spawningEntity.GetClientFsms()
        );

        var processor = new EntityProcessor {
            GameObject = spawnedObject,
            IsSceneHost = IsSceneHost,
            IsSceneHostDetermined = _isSceneHostDetermined,
            LateLoad = true,
            SpawnedId = id
        }.Process();

        if (!processor.Success) {
            Logger.Warn($"Could not process game object of spawned entity: {spawnedObject.name}");
        }
    }

    /// <summary>
    /// Method for handling received entity updates.
    /// </summary>
    /// <param name="entityUpdate">The entity update to handle.</param>
    /// <param name="alreadyInSceneUpdate">Whether this is the update from the already in scene packet.</param>
    public bool HandleEntityUpdate(EntityUpdate entityUpdate, bool alreadyInSceneUpdate = false) {
        if (IsSceneHost) {
            return true;
        }

        if (!_entities.TryGetValue(entityUpdate.Id, out var entity) || !_isSceneHostDetermined) {
            LogDeferred(entityUpdate.Id);
            _deferredUpdates.Enqueue(entityUpdate);
            return false;
        }

        if (entityUpdate.UpdateTypes.Contains(EntityUpdateType.Position)) {
            entity.UpdatePosition(entityUpdate.Position);
        }

        if (entityUpdate.UpdateTypes.Contains(EntityUpdateType.Scale)) {
            entity.UpdateScale(entityUpdate.Scale);
        }

        if (entityUpdate.UpdateTypes.Contains(EntityUpdateType.Animation)) {
            entity.UpdateAnimation(
                entityUpdate.AnimationId,
                (tk2dSpriteAnimationClip.WrapMode) entityUpdate.AnimationWrapMode,
                alreadyInSceneUpdate
            );
        }

        return true;
    }

    /// <summary>
    /// Method for handling received reliable entity updates.
    /// </summary>
    /// <param name="entityUpdate">The reliable entity update to handle.</param>
    /// <param name="alreadyInSceneUpdate">Whether this is the update from the already in scene packet.</param>
    public bool HandleReliableEntityUpdate(ReliableEntityUpdate entityUpdate, bool alreadyInSceneUpdate = false) {
        if (!_entities.TryGetValue(entityUpdate.Id, out var entity) || !_isSceneHostDetermined) {
            LogDeferred(entityUpdate.Id);
            _deferredUpdates.Enqueue(entityUpdate);
            return false;
        }

        // Active state and host FSM data are owned by the host — clients apply them, the host ignores them.
        // Generic data updates apply unconditionally.
        if (!IsSceneHost) {
            if (entityUpdate.UpdateTypes.Contains(EntityUpdateType.Active)) {
                entity.UpdateIsActive(entityUpdate.IsActive);
            }

            if (entityUpdate.UpdateTypes.Contains(EntityUpdateType.HostFsm)) {
                entity.UpdateHostFsmData(entityUpdate.HostFsmData);
            }
        }

        if (entityUpdate.UpdateTypes.Contains(EntityUpdateType.Data)) {
            entity.UpdateData(entityUpdate.GenericData, alreadyInSceneUpdate);
        }

        return true;
    }

    private void LogDeferred(ushort entityId) {
        Logger.Debug(
            _isSceneHostDetermined
                ? $"Could not find entity ({entityId}) to apply update for; storing update for now"
                : "Scene host is not determined yet to apply update; storing update for now"
        );
    }

    /// <summary>
    /// Callback method for when a game object is spawned from an existing entity.
    /// </summary>
    /// <param name="details">The entity spawn details containing how the entity was spawned.</param>
    /// <returns>Whether an entity was registered from this spawn.</returns>
    private bool OnGameObjectSpawned(EntitySpawnDetails details) {
        if (_entities.Values.Any(e => e.Object.Host == details.GameObject)) {
            Logger.Debug("Spawned object was already a registered entity");
            return true;
        }

        var processor = new EntityProcessor {
            GameObject = details.GameObject,
            IsSceneHost = IsSceneHost,
            IsSceneHostDetermined = _isSceneHostDetermined,
            LateLoad = true
        }.Process();

        if (!processor.Success) {
            return false;
        }

        if (!IsSceneHost) {
            Logger.Warn("Game object was spawned while not scene host, this shouldn't happen");
            return false;
        }

        if (details.Type != EntitySpawnType.FsmAction) {
            Logger.Error($"Invalid EntitySpawnDetails type: {details.Type}");
            return false;
        }

        if (!EntityRegistry.TryGetEntry(details.Action.Fsm.GameObject, out var entry)) {
            Logger.Warn("Could not find registry entry for spawning type of object");
            return false;
        }

        var topLevelEntity = processor.Entities[0];
        Logger.Info(
            $"Notifying server of entity ({details.Action.Fsm.GameObject.name}, {entry!.Type}) spawning entity " +
            $"({details.GameObject.name}, {topLevelEntity.Type}) with ID {topLevelEntity.Id}"
        );

        _netClient.UpdateManager.SetEntitySpawn(topLevelEntity.Id, entry.Type, topLevelEntity.Type);

        return true;
    }

    /// <summary>
    /// Attempts to apply all deferred updates. Snapshots the count before iterating so that updates
    /// re-queued during this pass are not visited again until the next call.
    /// <para>
    /// The handlers (HandleEntityUpdate, HandleReliableEntityUpdate) already re-enqueue on failure.
    /// The guard at the end of the loop is a safety net for any future handler that might not.
    /// </para>
    /// </summary>
    private void CheckDeferredUpdates() {
        var count = _deferredUpdates.Count;

        for (var i = 0; i < count; i++) {
            var update = _deferredUpdates.Dequeue();

            bool applied;
            switch (update) {
                case EntityUpdate entityUpdate:
                    applied = HandleEntityUpdate(entityUpdate);
                    break;
                case ReliableEntityUpdate reliableUpdate:
                    applied = HandleReliableEntityUpdate(reliableUpdate);
                    break;
                default:
                    Logger.Warn($"Unknown update type in deferred queue, discarding: {update.GetType()}");
                    continue; // Re-queuing an unrecognized type would loop forever.
            }

            if (!applied && _deferredUpdates.Count == count - i - 1) {
                _deferredUpdates.Enqueue(update);
            }
        }
    }

    /// <summary>
    /// Callback method for when the scene changes. Will clear existing entities and start checking for
    /// new entities.
    /// </summary>
    /// <param name="oldScene">The old scene.</param>
    /// <param name="newScene">The new scene.</param>
    private void OnSceneChanged(Scene oldScene, Scene newScene) {
        Logger.Info("Scene changed, clearing registered entities");

        ClearEntities();

        if (!_netClient.IsConnected) {
            return;
        }

        _isSceneHostDetermined = false;

        FindEntitiesInScene(newScene, lateLoad: false);
        CheckDeferredUpdates();
    }

    /// <summary>
    /// Clears all the registered entities, and resets static components.
    /// </summary>
    private void ClearEntities() {
        foreach (var entity in _entities.Values) {
            entity.Destroy();
        }

        _entities.Clear();
        _deferredUpdates.Clear();
        MusicComponent.ClearInstance();
    }

    /// <summary>
    /// Callback method for when a scene is loaded.
    /// </summary>
    /// <param name="scene">The scene that is loaded.</param>
    /// <param name="mode">The load scene mode.</param>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        // Only process scenes that are a named sub-scene of the current active scene — for example,
        // a boss room loaded additively on top of its parent scene. The active scene itself and
        // unrelated scenes are both ignored.
        var activeSceneName = SceneManager.GetActiveScene().name;
        var isSubScene = scene.name.StartsWith(activeSceneName) && !scene.name.Equals(activeSceneName);

        if (!isSubScene) {
            return;
        }

        Logger.Info($"Additional scene loaded ({scene.name}), looking for entities");

        FindEntitiesInScene(scene, lateLoad: true);
        CheckDeferredUpdates();
    }

    /// <summary>
    /// Find entities to register in the given scene.
    /// </summary>
    /// <param name="scene">The scene to find entities in.</param>
    /// <param name="lateLoad">Whether this scene was loaded late.</param>
    private void FindEntitiesInScene(Scene scene, bool lateLoad) {
        // --- Enemy objects (EnemyDeathEffects) ---
        // Include both the enemy's own GameObject and its pre-instantiated corpse prefab, so both
        // can be tracked before they become active.
        var enemyObjects = Object.FindObjectsOfType<EnemyDeathEffects>()
                                 .Where(e => e.gameObject.scene == scene)
                                 .SelectMany(effects => {
                                         try {
                                             effects.PreInstantiate();
                                         } catch (Exception) {
                                             // PersonalObjectPool-based enemies cannot be pre-instantiated this early;
                                             // fall back to tracking only the base GameObject.
                                             return new[] { effects.gameObject };
                                         }

                                         // TODO: CorpsePrefab is a prefab reference and may not be compatible with the
                                         // original code path.
                                         return [effects.gameObject, effects.CorpsePrefab];
                                     }
                                 );

        // --- FSM objects (PlayMakerFSM) ---
        var fsmObjects = Object.FindObjectsOfType<PlayMakerFSM>(true)
                               .Where(fsm => fsm.gameObject.scene == scene)
                               .Select(fsm => fsm.gameObject);

        // Expand each collected object to include all of its children so nested entities are found.
        var expandedObjects = enemyObjects
                              .Concat(fsmObjects)
                              .SelectMany(obj => obj == null ? [] : obj.GetChildren().Prepend(obj));

        // --- Component-based objects that bypass the FSM / EnemyDeathEffects paths ---
        var componentObjects =
            Object.FindObjectsOfType<Climber>(true).Select(c => c.gameObject)
                  .Concat(Object.FindObjectsOfType<Walker>(true).Select(w => w.gameObject));

        var candidateObjects = expandedObjects
                               .Concat(componentObjects)
                               .Where(obj => obj.scene == scene)
                               .Distinct()
                               .ToList();

        var entityCountBefore = _entities.Count;
        Logger.Info(
            $"Checking {candidateObjects.Count} candidate object(s) for entities in scene '{scene.name}' (lateLoad: {lateLoad})"
        );

        foreach (var obj in candidateObjects) {
            new EntityProcessor {
                GameObject = obj,
                IsSceneHost = IsSceneHost,
                IsSceneHostDetermined = _isSceneHostDetermined,
                LateLoad = lateLoad
            }.Process();
        }

        Logger.Info($"Registered {_entities.Count - entityCountBefore} entity/entities in scene '{scene.name}'");
    }
}

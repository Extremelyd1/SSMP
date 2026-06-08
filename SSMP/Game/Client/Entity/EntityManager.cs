using System;
using System.Collections.Generic;
using System.Linq;
using SSMP.Game.Client.Entity.Action;
using SSMP.Game.Client.Entity.Component;
using SSMP.Game.Client.Entity.Encounters;
using SSMP.Networking.Client;
using SSMP.Networking.Packet.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = SSMP.Logging.Logger;

#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace SSMP.Game.Client.Entity;

/// <summary>
/// Manager class that handles entity creation, updating, networking and destruction.
/// </summary>
internal class EntityManager {
    /// <summary>
    /// Static reference to the active entity manager.
    /// </summary>
    public static EntityManager? Instance { get; private set; }

    /// <summary>
    /// The net client for networking.
    /// </summary>
    private readonly NetClient _netClient;

    /// <summary>
    /// Dictionary mapping entity IDs to their respective entity instances.
    /// </summary>
    private readonly Dictionary<ushort, Entity> _entities;

    /// <summary>
    /// Updates buffered when the target entity or the scene host role aren't ready yet.
    /// </summary>
    private readonly DeferredEntityUpdateQueue _deferredUpdates;

    /// <summary>
    /// Allocates IDs for newly registered entities.
    /// </summary>
    private readonly EntityIdAllocator _entityIds;

    /// <summary>
    /// Finds scene objects that may be registered as entities.
    /// </summary>
    private readonly EntitySceneDiscovery _sceneDiscovery;


    // True once InitializeSceneHost or InitializeSceneClient has completed for the current scene.
    private bool _isSceneHostDetermined;

    /// <summary>
    /// Whether the client user is the scene host.
    /// </summary>
    public bool IsSceneHost { get; private set; }

    /// <summary>
    /// Gets the currently registered entities.
    /// </summary>
    public IEnumerable<Entity> Entities => _entities.Values;

    /// <summary>
    /// Create a new entity manager for the given network client.
    /// </summary>
    /// <param name="netClient">The client used for entity networking.</param>
    public EntityManager(NetClient netClient) {
        Instance = this;
        _netClient = netClient;
        _entities = new Dictionary<ushort, Entity>();
        _entityIds = new EntityIdAllocator(_entities);
        _sceneDiscovery = new EntitySceneDiscovery();
        _deferredUpdates = new DeferredEntityUpdateQueue();
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

    /// <summary>
    /// Apply the resolved scene role to every registered entity and then retry any deferred updates.
    /// </summary>
    /// <param name="isHost">Whether the local player is the authoritative host for the current scene.</param>
    private void InitializeSceneRole(bool isHost) {
        Logger.Info(
            isHost
                ? "We are scene host, releasing control of all registered entities"
                : "We are scene client, taking control of all registered entities"
        );

        IsSceneHost = isHost;
        EncounterManager.OnSceneLoaded(SceneManager.GetActiveScene(), isHost);

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
    /// The specific instance doesn't matter; FSMs and components are identical across instances of the same type.
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

        if (TryBindExistingSpawnedEntity(id, spawnedType)) {
            return;
        }

        Logger.Warn(
            $"Cannot spawn entity {spawnedType} from {spawningType}: no matching scene object exists and prefab spawning is not implemented."
        );
    }

    /// <summary>
    /// Bind a network-spawned entity to an already-present scene object of the requested type.
    /// This keeps SSMP as identity/state sync for native mobs and only leaves prefab instantiation as fallback.
    /// </summary>
    /// <param name="id">The network entity ID to assign.</param>
    /// <param name="spawnedType">The entity type requested by the host.</param>
    /// <returns><c>true</c> when an existing scene object was claimed.</returns>
    private bool TryBindExistingSpawnedEntity(ushort id, EntityType spawnedType) {
        var scene = SceneManager.GetActiveScene();
        var candidates = _sceneDiscovery.FindCandidates(scene);

        foreach (var candidate in candidates) {
            if (IsEntityAlreadyBound(candidate)) {
                continue;
            }

            if (!TryGetEntityEntry(candidate, out var entry)) {
                continue;
            }

            if (entry.Type != spawnedType) {
                continue;
            }

            Logger.Info(
                $"Binding spawned entity ({spawnedType}) with ID {id} to existing scene object '{candidate.name}'"
            );

            var processor = CreateProcessor(candidate, lateLoad: true, spawnedId: id).Process();

            if (processor.Success) {
                return true;
            }

            Logger.Warn(
                $"Existing scene object '{candidate.name}' matched {spawnedType} but could not be processed"
            );
        }

        return false;
    }

    private bool IsEntityAlreadyBound(GameObject candidate) {
        return _entities.Values.Any(entity =>
            entity.Object.Host == candidate ||
            entity.Object.Client == candidate
        );
    }

    private static bool TryGetEntityEntry(
        GameObject candidate,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out EntityRegistryEntry? entry
    ) {
        return EncounterManager.TryGetEntityEntry(candidate, out entry) ||
               EntityRegistry.TryGetEntry(candidate, out entry);
    }

    /// <summary>
    /// Create a processor configured for the current scene-role state.
    /// </summary>
    /// <param name="gameObject">The root object to process.</param>
    /// <param name="lateLoad">Whether the object appeared after the initial scene scan.</param>
    /// <param name="spawnedId">An optional network-assigned ID for spawned entities.</param>
    /// <returns>A configured entity processor instance.</returns>
    private EntityProcessor CreateProcessor(
        GameObject gameObject,
        bool lateLoad,
        ushort? spawnedId = null
    ) {
        return new EntityProcessor(_entities, _netClient, _entityIds) {
            GameObject = gameObject,
            IsSceneHost = IsSceneHost,
            IsSceneHostDetermined = _isSceneHostDetermined,
            LateLoad = lateLoad,
            SpawnedId = spawnedId
        };
    }

    /// <summary>
    /// Method for handling received entity updates.
    /// </summary>
    /// <param name="entityUpdate">The entity update to handle.</param>
    /// <param name="alreadyInSceneUpdate">Whether this is the update from the already in scene packet.</param>
    /// <returns><c>true</c> when the update was applied immediately; otherwise it was deferred.</returns>
    public void HandleEntityUpdate(EntityUpdate entityUpdate, bool alreadyInSceneUpdate = false) {
        if (IsSceneHost) {
            return;
        }

        if (TryApplyEntityUpdate(entityUpdate, alreadyInSceneUpdate)) {
            return;
        }

        LogDeferred(entityUpdate.Id);
        _deferredUpdates.Enqueue(entityUpdate);
    }

    /// <summary>
    /// Attempt to apply an unreliable entity update immediately.
    /// </summary>
    /// <param name="entityUpdate">The update to apply.</param>
    /// <param name="alreadyInSceneUpdate">Whether the update came from the already-in-scene sync payload.</param>
    /// <returns><c>true</c> when the target entity was ready and the update was applied.</returns>
    private bool TryApplyEntityUpdate(EntityUpdate entityUpdate, bool alreadyInSceneUpdate = false) {
        if (!_entities.TryGetValue(entityUpdate.Id, out var entity) || !_isSceneHostDetermined) {
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
    public void HandleReliableEntityUpdate(ReliableEntityUpdate entityUpdate, bool alreadyInSceneUpdate = false) {
        if (!TryApplyReliableEntityUpdate(entityUpdate, alreadyInSceneUpdate)) {
            LogDeferred(entityUpdate.Id);
            _deferredUpdates.Enqueue(entityUpdate);
        }
    }

    /// <summary>
    /// Attempt to apply a reliable entity update immediately.
    /// </summary>
    /// <param name="entityUpdate">The update to apply.</param>
    /// <param name="alreadyInSceneUpdate">Whether the update came from the already-in-scene sync payload.</param>
    /// <returns><c>true</c> when the target entity was ready and the update was applied.</returns>
    private bool TryApplyReliableEntityUpdate(
        ReliableEntityUpdate entityUpdate,
        bool alreadyInSceneUpdate = false
    ) {
        if (!_entities.TryGetValue(entityUpdate.Id, out var entity) || !_isSceneHostDetermined) {
            return false;
        }

        // Active state and host FSM data are owned by the host - clients apply them, the host ignores them.
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

    /// <summary>
    /// Log why an incoming update is being deferred instead of applied immediately.
    /// </summary>
    /// <param name="entityId">The target entity ID from the incoming update.</param>
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

        var processor = CreateProcessor(details.GameObject, lateLoad: true).Process();

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
            $"Notifying server of entity ({details.Action.Fsm.GameObject.name}, {entry.Type}) spawning entity " +
            $"({details.GameObject.name}, {topLevelEntity.Type}) with ID {topLevelEntity.Id}"
        );

        _netClient.UpdateManager.SetEntitySpawn(topLevelEntity.Id, entry.Type, topLevelEntity.Type);

        return true;
    }

    /// <summary>
    /// Attempt to apply all pending entity updates that could not be applied when received.
    /// </summary>
    private void CheckDeferredUpdates() {
        _deferredUpdates.Retry(TryApplyDeferredUpdate);
    }

    /// <summary>
    /// Retry a buffered update using the appropriate entity-update handler.
    /// </summary>
    /// <param name="update">The buffered update to retry.</param>
    /// <returns><c>true</c> when the update should be removed from the queue.</returns>
    private bool TryApplyDeferredUpdate(BaseEntityUpdate update) {
        switch (update) {
            case EntityUpdate entityUpdate:
                return TryApplyEntityUpdate(entityUpdate);
            case ReliableEntityUpdate reliableUpdate:
                return TryApplyReliableEntityUpdate(reliableUpdate);
            default:
                DeferredEntityUpdateQueue.DiscardUnknown(update);
                return true;
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
        _entityIds.Reset();
        MusicComponent.ClearInstance();
    }

    /// <summary>
    /// Callback method for when a scene is loaded.
    /// </summary>
    /// <param name="scene">The scene that is loaded.</param>
    /// <param name="mode">The load scene mode.</param>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        // Only process scenes that are a named sub-scene of the current active scene, for example,
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
        var candidateObjects = _sceneDiscovery.FindCandidates(scene);
        var entityCountBefore = _entities.Count;

        Logger.Info(
            $"Checking {candidateObjects.Count} candidate object(s) for entities in scene '{scene.name}' (lateLoad: {lateLoad})"
        );

        foreach (var obj in candidateObjects) {
            CreateProcessor(obj, lateLoad).Process();
        }

        Logger.Info($"Registered {_entities.Count - entityCountBefore} entity/entities in scene '{scene.name}'");
    }
}

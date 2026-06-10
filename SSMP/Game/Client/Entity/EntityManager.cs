using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SSMP.Util;
using HutongGames.PlayMaker.Actions;
using SSMP.Game.Client.Entity.Action;
using SSMP.Game.Client.Entity.Component;
using SSMP.Networking.Client;
using SSMP.Networking.Packet.Data;
using MonoMod.RuntimeDetour;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS0618 // Type or member is obsolete

namespace SSMP.Game.Client.Entity;

internal class EntityManager {
    private readonly NetClient _netClient;
    private readonly Dictionary<ushort, Entity> _entities;

    // Buffered updates waiting on an entity that hasn't registered yet, or on scene-host determination.
    private readonly Queue<BaseEntityUpdate> _pendingUpdates;

    private Hook? _findGameObjectHook;

    // Both flags are set together in InitializeSceneHost / InitializeSceneClient.
    public bool IsSceneHost { get; private set; }
    private bool _sceneRoleDetermined;

    public EntityManager(NetClient netClient) {
        _netClient = netClient;
        _entities = new Dictionary<ushort, Entity>();
        _pendingUpdates = new Queue<BaseEntityUpdate>();
    }

    public void Initialize() {
        EntityProcessor.Initialize(_entities, _netClient);
    }

    public void RegisterHooks() {
        FsmActionHooks.RegisterHooks();
        MusicComponent.RegisterHooks();

        EntityFsmActions.EntitySpawnEvent += OnGameObjectSpawned;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnSceneChanged;

        _findGameObjectHook = new Hook(
            typeof(FindGameObject).GetMethod(
                nameof(FindGameObject.OnEnter),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            ),
            OnFindGameObject
        );
    }

    public void DeregisterHooks() {
        FsmActionHooks.DeregisterHooks();
        MusicComponent.DeregisterHooks();

        EntityFsmActions.EntitySpawnEvent -= OnGameObjectSpawned;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnSceneChanged;

        _findGameObjectHook?.Dispose();
        _findGameObjectHook = null;

        ClearEntities();
    }

    public void InitializeSceneHost() {
        Logger.Info("We are scene host, releasing control of all registered entities");
        IsSceneHost = true;
        foreach (var entity in _entities.Values) entity.InitializeHost();
        _sceneRoleDetermined = true;
        DrainPendingUpdates();
    }

    public void InitializeSceneClient() {
        Logger.Info("We are scene client, taking control of all registered entities");
        IsSceneHost = false;
        foreach (var entity in _entities.Values) entity.InitializeClient();
        _sceneRoleDetermined = true;
        DrainPendingUpdates();
    }

    public void BecomeSceneHost() {
        Logger.Info("Becoming scene host");
        IsSceneHost = true;
        foreach (var entity in _entities.Values) entity.MakeHost();
    }

    /// <summary>
    /// Attempts to spawn a networked entity. No-ops if the ID is already registered (assumed spawned by action).
    /// </summary>
    public void SpawnEntity(ushort id, EntityType spawningType, EntityType spawnedType) {
        Logger.Info($"Trying to spawn entity with ID {id} with types: {spawningType}, {spawnedType}");

        if (_entities.ContainsKey(id)) {
            Logger.Info($"  Entity with ID {id} already exists, assuming it has been spawned by action");
            return;
        }

        // Any entity of the same type works as a template — FSMs and components are identical across instances.
        var templateEntity = _entities.Values.FirstOrDefault(e => e.Type == spawningType);
        if (templateEntity == null) {
            Logger.Warn("Could not find entity with same type for spawning");
            return;
        }

        var spawnedObject = EntitySpawner.SpawnEntityGameObject(
            spawningType,
            spawnedType,
            templateEntity.Object.Client,
            templateEntity.GetClientFsms()
        );

        var processor = new EntityProcessor {
            GameObject = spawnedObject,
            IsSceneHost = IsSceneHost,
            IsSceneHostDetermined = _sceneRoleDetermined,
            LateLoad = true,
            SpawnedId = id
        }.Process();

        if (!processor.Success) {
            Logger.Warn($"Could not process game object of spawned entity: {spawnedObject.name}");
        }
    }

    /// <summary>
    /// Applies an unreliable entity update (position, scale, animation).
    /// Returns false and buffers the update if the entity isn't ready yet.
    /// </summary>
    public bool HandleEntityUpdate(EntityUpdate update, bool alreadyInSceneUpdate = false) {
        // Scene host owns entity state; updates from peers are ignored.
        if (IsSceneHost) return true;

        if (!_entities.TryGetValue(update.Id, out var entity) || !_sceneRoleDetermined) {
            LogBufferingReason(update.Id);
            _pendingUpdates.Enqueue(update);
            return false;
        }

        if (update.UpdateTypes.Contains(EntityUpdateType.Position))
            entity.UpdatePosition(update.Position);

        if (update.UpdateTypes.Contains(EntityUpdateType.Scale))
            entity.UpdateScale(update.Scale);

        if (update.UpdateTypes.Contains(EntityUpdateType.Animation))
            entity.UpdateAnimation(
                update.AnimationId,
                (tk2dSpriteAnimationClip.WrapMode) update.AnimationWrapMode,
                alreadyInSceneUpdate
            );

        return true;
    }

    /// <summary>
    /// Applies a reliable entity update (active state, host FSM data, generic data).
    /// Returns false and buffers the update if the entity isn't ready yet.
    /// </summary>
    public bool HandleReliableEntityUpdate(ReliableEntityUpdate update, bool alreadyInSceneUpdate = false) {
        if (!_entities.TryGetValue(update.Id, out var entity) || !_sceneRoleDetermined) {
            LogBufferingReason(update.Id);
            _pendingUpdates.Enqueue(update);
            return false;
        }

        // Active state and host FSM are driven by the scene host; clients are consumers only.
        if (!IsSceneHost) {
            if (update.UpdateTypes.Contains(EntityUpdateType.Active))
                entity.UpdateIsActive(update.IsActive);

            if (update.UpdateTypes.Contains(EntityUpdateType.HostFsm))
                entity.UpdateHostFsmData(update.HostFsmData);
        }

        if (update.UpdateTypes.Contains(EntityUpdateType.Data))
            entity.UpdateData(update.GenericData, alreadyInSceneUpdate);

        return true;
    }

    private void OnSceneChanged(Scene oldScene, Scene newScene) {
        Logger.Info("Scene changed, clearing registered entities");
        ClearEntities();

        if (!_netClient.IsConnected) return;

        _sceneRoleDetermined = false;
        FindEntitiesInScene(newScene, lateLoad: false);
        DrainPendingUpdates();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        var activeScene = SceneManager.GetActiveScene().name;

        // Boss/boss-defeated scenes share a name prefix with the base scene; skip unrelated additively-loaded scenes.
        if (!scene.name.StartsWith(activeScene) || scene.name.Equals(activeScene)) return;

        Logger.Info($"Additional scene loaded ({scene.name}), looking for entities");
        FindEntitiesInScene(scene, lateLoad: true);
        DrainPendingUpdates();
    }

    private void FindEntitiesInScene(Scene scene, bool lateLoad) {
        var objects = CollectEntityCandidates(scene);

        foreach (var obj in objects) {
            new EntityProcessor {
                GameObject = obj,
                IsSceneHost = IsSceneHost,
                IsSceneHostDetermined = _sceneRoleDetermined,
                LateLoad = lateLoad
            }.Process();
        }
    }

    /// <summary>
    /// Gathers all GameObjects in the scene that are candidates for entity registration.
    /// Handles EnemyDeathEffects corpse pre-instantiation and several component-driven
    /// object types (Climber, Walker, BigCentipede, CameraLockArea, DreamPlatform).
    /// </summary>
    private static IEnumerable<GameObject> CollectEntityCandidates(Scene scene) {
        var fromDeathEffects = Object.FindObjectsOfType<EnemyDeathEffects>()
                                     .Where(e => e.gameObject.scene == scene)
                                     .SelectMany(ExpandDeathEffects);

        var fromFsms = Object.FindObjectsOfType<PlayMakerFSM>(true)
                             .Where(fsm => fsm.gameObject.scene == scene)
                             .Select(fsm => fsm.gameObject);

        var fromComponents = new[] {
            Object.FindObjectsOfType<Climber>(true).Select(c => c.gameObject),
            Object.FindObjectsOfType<Walker>(true).Select(c => c.gameObject),
            Object.FindObjectsOfType<BigCentipede>(true).Select(c => c.gameObject),
            Object.FindObjectsOfType<CameraLockArea>(true).Select(c => c.gameObject),
            Object.FindObjectsOfType<DreamPlatform>(true).Select(c => c.gameObject),
        }.SelectMany(x => x);

        return fromDeathEffects
               // Expand each object to itself and all children
               .Concat(fromFsms)
               .SelectMany(obj => obj == null ? Array.Empty<GameObject>() : obj.GetChildren().Prepend(obj))
               .Concat(fromComponents)
               .Where(obj => obj.scene == scene)
               .Distinct();
    }


    private static IEnumerable<GameObject> ExpandDeathEffects(EnemyDeathEffects deathEffects) {
        try {
            deathEffects.PreInstantiate();
        } catch (Exception) {
            // PersonalObjectPool objects can't be pre-instantiated this early; fall back to the root only.
            return [deathEffects.gameObject];
        }

        // TODO: CorpsePrefab is a prefab reference, may not be compatible with original code.
        var corpse = deathEffects.CorpsePrefab;
        return [deathEffects.gameObject, corpse];
    }

    private bool OnGameObjectSpawned(EntitySpawnDetails details) {
        if (_entities.Values.Any(e => e.Object.Host == details.GameObject)) {
            Logger.Debug("Spawned object was already a registered entity");
            return true;
        }

        var processor = new EntityProcessor {
            GameObject = details.GameObject,
            IsSceneHost = IsSceneHost,
            IsSceneHostDetermined = _sceneRoleDetermined,
            LateLoad = true
        }.Process();

        if (!processor.Success) return false;

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

        var topLevel = processor.Entities[0];
        Logger.Info(
            $"Notifying server of entity ({details.Action.Fsm.GameObject.name}, {entry.Type}) spawning entity ({details.GameObject.name}, {topLevel.Type}) with ID {topLevel.Id}"
        );
        _netClient.UpdateManager.SetEntitySpawn(topLevel.Id, entry.Type, topLevel.Type);

        return true;
    }

    /// <summary>
    /// Replays buffered updates for entities that are now registered and whose scene role is known.
    /// Updates that still can't be applied are left in the queue.
    /// </summary>
    private void DrainPendingUpdates() {
        // Iterate a snapshot count; newly buffered updates (HandleEntityUpdate returning false) stay in the queue.
        var count = _pendingUpdates.Count;
        for (var i = 0; i < count; i++) {
            var update = _pendingUpdates.Dequeue();

            var applied = update switch {
                EntityUpdate eu => HandleEntityUpdate(eu),
                ReliableEntityUpdate r => HandleReliableEntityUpdate(r),
                _ => true
            };

            if (!applied) {
                // Still not applicable; it was re-enqueued by Handle*..nothing to do.
            }
        }
    }

    private void ClearEntities() {
        foreach (var entity in _entities.Values) entity.Destroy();
        _entities.Clear();
        _pendingUpdates.Clear();
        MusicComponent.ClearInstance();
    }

    private void LogBufferingReason(ushort id) {
        var reason = _sceneRoleDetermined
            ? $"Could not find entity ({id}) to apply update for; storing update for now"
            : "Scene host is not determined yet to apply update; storing update for now";
        Logger.Debug(reason);
    }

    // Patch: intercept FindGameObject.OnEnter so that entities the system has made inactive are still findable.
    private void OnFindGameObject(Action<FindGameObject> orig, FindGameObject self) {
        orig(self);

        if (self.store.Value != null) return;

        // This particular FSM state finds a Roller via tag; letting our code handle it breaks Blocker Control logic.
        if (self.State.Name == "Can Roller?" && self.Fsm.Name == "Blocker Control") return;

        Logger.Debug($"OnFindGameObject, find failed: looking for '{self.objectName.Value}'");

        // Tag-based finds won't match our entities by name.
        if (self.withTag.Value != "Untagged") return;

        foreach (var entity in _entities.Values) {
            var host = entity.Object.Host;
            if (host != null && host.name == self.objectName.Value) {
                self.store.Value = host;
                Logger.Debug($"  Name matches host object of entity: ({entity.Id}, {entity.Type})");
                return;
            }
        }

        Logger.Debug("  Name did not match any entity");
    }
}

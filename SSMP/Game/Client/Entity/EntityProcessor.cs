using System.Collections.Generic;
using System.Linq;
using SSMP.Networking.Client;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider
// adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace SSMP.Game.Client.Entity;

/// <summary>
/// Processes a single GameObject and its children into registered entities.
/// Constructed with <c>init</c> properties describing the processing context; call <see cref="Process()"/> to run.
/// Inspect <see cref="Success"/> and <see cref="Entities"/> for results.
/// </summary>
internal class EntityProcessor {
    /// <summary>
    /// Reference to the dictionary of entities from the entity manager.
    /// </summary>
    private static Dictionary<ushort, Entity> _entities;

    /// <summary>
    /// The net client used to pass onto constructed entities.
    /// </summary>
    private static NetClient _netClient;

    /// <summary>
    /// The last used entity ID.
    /// </summary>
    private static ushort _lastId;

    private static bool _isSceneHookRegistered;

    /// <summary>
    /// The game object to process.
    /// </summary>
    public GameObject GameObject { get; init; }

    /// <summary>
    /// Whether the local client is the scene host.
    /// </summary>
    public bool IsSceneHost { get; init; }

    /// <summary>
    /// Whether the scene host is determined for this scene locally.
    /// </summary>
    public bool IsSceneHostDetermined { get; init; }

    /// <summary>
    /// Whether the processing of this entity should happen under the assumption that was a late load of the
    /// game object.
    /// </summary>
    public bool LateLoad { get; init; }

    /// <summary>
    /// Whether the game object was spawned and should have the designated ID.
    /// </summary>
    public ushort? SpawnedId { get; init; }

    /// <summary>
    /// The list of entities that were created during the processing.
    /// </summary>
    public List<Entity> Entities { get; } = [];

    /// <summary>
    /// Whether the processing of the entity was a success.
    /// </summary>
    public bool Success => Entities.Count > 0;

    /// <summary>
    /// Initialize the entity processor with a reference to the entity dict and the net client.
    /// </summary>
    /// <param name="entities">Shared entity registry from EntityManager.</param>
    /// <param name="netClient">Net client passed into each constructed entity.</param>
    public static void Initialize(Dictionary<ushort, Entity> entities, NetClient netClient) {
        _entities = entities;
        _netClient = netClient;

        if (_isSceneHookRegistered) {
            return;
        }

        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += (_, _) => _lastId = 0;
        _isSceneHookRegistered = true;
    }

    /// <summary>
    /// Process the game object set in this instance with the parameter set in this instance.
    /// </summary>
    /// <returns>The instance of this class for convenience.</returns>
    public EntityProcessor Process() {
        Process(GameObject);
        return this;
    }

    /// <summary>
    /// Process the given game object to (potentially) become an entity. Check for child objects as well and will
    /// recursively process those.
    /// </summary>
    /// <param name="gameObject">The game object to process.</param>
    /// <param name="entries">Entity registry entries to check against for the entity or null to use all
    /// top-level entries.</param>
    /// <param name="parentClientObject">The client object of the parent entity for this entity or null if no such
    /// parent exists.</param>
    private void Process(
        GameObject gameObject,
        IEnumerable<EntityRegistryEntry> entries = null,
        GameObject parentClientObject = null
    ) {
        EntityRegistryEntry? foundEntry;
        if (entries is null) {
            if (!EntityRegistry.TryGetEntry(gameObject, out foundEntry) || foundEntry == null) return;
        } else {
            if (!EntityRegistry.TryGetEntry(entries, gameObject, out foundEntry) || foundEntry == null) return;
        }

        var id = ResolveId();
        if (!id.HasValue) return;

        var componentTypes = foundEntry.ComponentTypes ?? [];
        Entity entity;

        if (parentClientObject is null) {
            Logger.Info($"Registering entity ({foundEntry.Type}) '{gameObject.name}' with ID '{id.Value}'");

            entity = new Entity(
                _netClient,
                id.Value,
                foundEntry.Type,
                gameObject,
                types: componentTypes
            );
        } else {
            Logger.Info(
                $"Registering entity ({foundEntry.Type}) '{gameObject.name}' with ID '{id.Value}'" +
                $" with parent: {parentClientObject.name}"
            );

            // Find a matching child of the parent's client object that hasn't already been claimed
            // by an earlier entity in this processing pass.
            var clientObject = parentClientObject.GetChildren()
                                                 .FirstOrDefault(c =>
                                                     c.name.Contains(foundEntry.BaseObjectName) &&
                                                     Entities.All(e => e.Object.Client != c)
                                                 );

            if (clientObject is null) {
                Logger.Warn("Could not find child of client object of parent entity");
                return;
            }

            Logger.Debug(
                $"Found child of client object of parent entity: {clientObject.name}, {clientObject.GetInstanceID()}"
            );

            entity = new Entity(
                _netClient,
                id.Value,
                foundEntry.Type,
                gameObject,
                clientObject,
                componentTypes
            );
        }

        _entities[id.Value] = entity;
        Entities.Add(entity);

        if (foundEntry.Children != null) {
            foreach (var child in gameObject.GetChildren()) {
                Process(child, foundEntry.Children, entity.Object.Client);
            }
        }

        // Late-loaded entities miss the initial role assignment pass; apply it now.
        // Note: client-side late loads sync only active state - InitializeClient() is NOT called.
        // This appears intentional: the full client init is only done for entities present at scene load.
        if (LateLoad && IsSceneHostDetermined) {
            if (IsSceneHost) {
                entity.InitializeHost();
            } else {
                entity.UpdateIsActive(true);
            }
        }
    }

    /// <summary>
    /// Resolves the entity ID for the current processing context.
    /// Returns null and logs if the ID cannot be assigned.
    /// </summary>
    private ushort? ResolveId() {
        if (SpawnedId.HasValue) {
            if (_entities.ContainsKey(SpawnedId.Value)) {
                Logger.Warn(
                    $"Tried registering entity with forced ID ({SpawnedId.Value}), but an entity with that ID already exists"
                );
                return null;
            }

            return SpawnedId.Value;
        }

        // ushort spans 0–65535 (65536 distinct values); the space is only full when count exceeds MaxValue.
        if (_entities.Count > ushort.MaxValue) {
            Logger.Error("Could not register entity because ID space is full!");
            return null;
        }

        while (_entities.ContainsKey(_lastId)) {
            _lastId++;
        }

        return _lastId++;
    }
}

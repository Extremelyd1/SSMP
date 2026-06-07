using System.Collections.Generic;
using System.Linq;
using SSMP.Networking.Client;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

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
    private readonly Dictionary<ushort, Entity> _entities;

    /// <summary>
    /// The net client used to pass onto constructed entities.
    /// </summary>
    private readonly NetClient _netClient;

    /// <summary>
    /// Allocates entity IDs for registered entities.
    /// </summary>
    private readonly EntityIdAllocator _idAllocator;

    /// <summary>
    /// Create a processor that registers entities into the shared scene entity store.
    /// </summary>
    /// <param name="entities">The entity store to populate.</param>
    /// <param name="netClient">The network client to pass into created entities.</param>
    /// <param name="idAllocator">The allocator used to assign entity IDs.</param>
    public EntityProcessor(
        Dictionary<ushort, Entity> entities,
        NetClient netClient,
        EntityIdAllocator idAllocator
    ) {
        _entities = entities;
        _netClient = netClient;
        _idAllocator = idAllocator;
    }

    /// <summary>
    /// The game object to process.
    /// </summary>
    public GameObject GameObject { get; init; } = null!;

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
        IEnumerable<EntityRegistryEntry>? entries = null,
        GameObject? parentClientObject = null
    ) {
        EntityRegistryEntry? foundEntry;
        if (entries is null) {
            if (!EntityRegistry.TryGetEntry(gameObject, out foundEntry)) return;
        } else {
            if (!EntityRegistry.TryGetEntry(entries, gameObject, out foundEntry)) return;
        }

        var id = _idAllocator.Resolve(SpawnedId);
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

            // Find a matching child of the parent's client object that has not already been claimed
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
}

using System.Collections.Generic;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Game.Client.Entity;

/// <summary>
/// Allocates entity IDs against the currently registered entity dictionary.
/// </summary>
internal class EntityIdAllocator {
    /// <summary>
    /// The currently registered entities keyed by ID.
    /// </summary>
    private readonly Dictionary<ushort, Entity> _entities;

    /// <summary>
    /// The next automatically assigned ID candidate.
    /// </summary>
    private ushort _lastId;

    /// <summary>
    /// Create an allocator backed by the shared entity dictionary.
    /// </summary>
    /// <param name="entities">The entity dictionary used to detect ID collisions.</param>
    public EntityIdAllocator(Dictionary<ushort, Entity> entities) {
        _entities = entities;
    }

    /// <summary>
    /// Resets automatic ID allocation for a new active scene.
    /// </summary>
    public void Reset() {
        _lastId = 0;
    }

    /// <summary>
    /// Resolves an entity ID, using <paramref name="forcedId"/> when present.
    /// </summary>
    /// <param name="forcedId">A network-assigned ID for spawned entities, or null for automatic allocation.</param>
    /// <returns>The resolved entity ID, or null when no ID can be assigned.</returns>
    public ushort? Resolve(ushort? forcedId) {
        if (forcedId.HasValue) {
            if (_entities.ContainsKey(forcedId.Value)) {
                Logger.Warn(
                    $"Tried registering entity with forced ID ({forcedId.Value}), but an entity with that ID already exists"
                );
                return null;
            }

            return forcedId.Value;
        }

        // ushort spans 0-65535 (65536 distinct values); the space is only full when count exceeds MaxValue.
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

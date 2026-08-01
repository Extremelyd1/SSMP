using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using SSMP.Game.Client.Entity.Component;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Game.Client.Entity;

/// <summary>
/// Loads and provides read-only access to entity registry data at startup.
/// Matches game objects to their registered <see cref="EntityRegistryEntry"/> by name, FSM, and parent.
/// Thread-safe for concurrent reads after initialization.
/// </summary>
internal static class EntityRegistry {
    /// <summary>
    /// The file path of the embedded resource file for the entity registry.
    /// </summary>
    private const string EntityRegistryFilePath = "SSMP.Resource.entity-registry.json";

    /// <summary>
    /// List of all entity registry entries that are loaded from the embedded file.
    /// Read-only after static constructor completes. Concurrent reads are safe.
    /// </summary>
    private static readonly List<EntityRegistryEntry> Entries;

    static EntityRegistry() {
        var loadedEntries = FileUtil.LoadObjectFromEmbeddedJson<List<EntityRegistryEntry>>(EntityRegistryFilePath)
                            ?? throw new InvalidDataException("Could not deserialize entries from embedded JSON.");

        Entries = GetValidEntries(loadedEntries);
    }

    /// <summary>
    /// Filters out entries whose <see cref="EntityRegistryEntry.TypeName"/> has no mapping in
    /// <see cref="EntityType"/>, and recursively validates children.
    /// </summary>
    /// <param name="entries">The raw loaded entries.</param>
    /// <returns>A validated list of entries.</returns>
    private static List<EntityRegistryEntry> GetValidEntries(IEnumerable<EntityRegistryEntry> entries) {
        var validEntries = new List<EntityRegistryEntry>();

        foreach (var entry in entries) {
            if (!entry.HasValidType) {
                Logger.Warn(
                    $"Ignoring entity registry entry '{entry.BaseObjectName}' with unmapped type '{entry.TypeName}'"
                );
                continue;
            }

            if (entry.Children != null) {
                entry.Children = GetValidEntries(entry.Children);
            }

            validEntries.Add(entry);
        }

        return validEntries;
    }

    /// <summary>
    /// Finds the best-matching entry for <paramref name="gameObject"/> within the top-level registry.
    /// Matching prefers the entry with the longest <see cref="EntityRegistryEntry.BaseObjectName"/>
    /// that is a substring of the object's name, optionally filtered by FSM and parent name.
    /// </summary>
    /// <param name="gameObject">The game object to find an entry for.</param>
    /// <param name="foundEntry">The matched entry, or <c>null</c> if none matched.</param>
    /// <returns><c>true</c> if a matching entry was found.</returns>
    public static bool TryGetEntry(GameObject gameObject, [NotNullWhen(true)] out EntityRegistryEntry? foundEntry) {
        return TryGetEntry(Entries, gameObject, out foundEntry);
    }

    /// <summary>
    /// Finds the best-matching entry for <paramref name="gameObject"/> within
    /// <paramref name="entries"/>. Useful for searching within a parent entry's children.
    /// </summary>
    /// <remarks>
    /// Known limitation: entries that share the same <see cref="EntityRegistryEntry.BaseObjectName"/>
    /// and FSM names are ambiguous. The type field is not part of the selection criteria.
    /// Example: GreatConchfly vs RagingConchfly under Coral Conch Driller Giant Solo.
    /// </remarks>
    /// <param name="entries">The set of entries to search.</param>
    /// <param name="gameObject">The game object to find an entry for.</param>
    /// <param name="foundEntry">The matched entry, or <c>null</c> if none matched.</param>
    /// <returns><c>true</c> if a matching entry was found.</returns>
    public static bool TryGetEntry(
        IEnumerable<EntityRegistryEntry> entries,
        GameObject gameObject,
        [NotNullWhen(true)] out EntityRegistryEntry? foundEntry
    ) {
        foundEntry = null;
        var longestBaseName = 0;

        // GetComponents<T>() allocates a new array each call. We defer it until we encounter
        // an entry that actually requires FSM filtering, and then reuse the result for the rest
        // of the loop. This avoids the allocation entirely when no entry uses FSM names.
        PlayMakerFSM[]? fsms = null;

        foreach (var entry in entries) {
            if (!gameObject.name.Contains(entry.BaseObjectName)) {
                continue;
            }

            if (entry.HasFsmFilter) {
                fsms ??= gameObject.GetComponents<PlayMakerFSM>();
                if (!HasMatchingFsm(fsms, entry)) continue;
            }

            if (entry.ParentName != null) {
                var parent = gameObject.transform.parent;
                // No parent means it trivially cannot match the required parent name.
                if (!parent || !parent.gameObject.name.Contains(entry.ParentName)) {
                    continue;
                }
            }

            var nameLength = entry.BaseObjectName.Length;
            if (nameLength <= longestBaseName) {
                continue;
            }

            longestBaseName = nameLength;
            foundEntry = entry;
        }

        // Do not use (longestBaseName == 0) as the "not found" check: an entry with an empty
        // BaseObjectName would always match Contains("") but would never win the length comparison,
        // causing a false negative. foundEntry != null is strictly correct.
        return foundEntry != null;
    }

    /// <summary>
    /// Returns true if any FSM in <paramref name="fsms"/> matches the entry's FSM name filter.
    /// Avoids LINQ and closure allocations; uses the entry's pre-built hash set for O(1) lookup.
    /// </summary>
    /// <param name="fsms">The FSMs on the game object.</param>
    /// <param name="entry">The registry entry with filter filters.</param>
    /// <returns>True if a match exists, false otherwise.</returns>
    private static bool HasMatchingFsm(PlayMakerFSM[] fsms, EntityRegistryEntry entry) {
        foreach (var fsm in fsms) {
            if (entry.ContainsFsmName(fsm.Fsm.Name)) return true;
        }

        return false;
    }
}

/// <summary>
/// A single entry in the entity registry, describing how to identify and construct an entity.
/// Instances are created by JSON deserialization and treated as read-only after initialization.
/// </summary>
internal class EntityRegistryEntry {
    /// <summary>
    /// The base of the game object name for this entity type.
    /// For example: "Zombie Leaper", which in-game may appear as "Zombie Leaper (Clone) (1)".
    /// Matching uses <see cref="string.Contains(string)"/> against the live object's name.
    /// </summary>
    [JsonProperty("base_object_name")]
    public string BaseObjectName { get; set; } = null!;

    /// <summary>The resolved entity type. Valid only when <see cref="HasValidType"/> is true.</summary>
    [JsonIgnore]
    public EntityType Type { get; private set; }

    /// <summary>The raw type string from the registry file, used for diagnostics and logging.</summary>
    [JsonProperty("type")]
    public string TypeName { get; private set; } = null!;

    /// <summary>
    /// Whether <see cref="TypeName"/> successfully mapped to a known <see cref="EntityType"/>.
    /// Entries where this is false are discarded during loading.
    /// </summary>
    [JsonIgnore]
    public bool HasValidType { get; private set; }

    /// <summary>
    /// FSM names used to disambiguate this entry from others sharing the same
    /// <see cref="BaseObjectName"/>. Null or empty means FSM matching is not required.
    /// </summary>
    [JsonProperty("fsm_names")]
    public List<string>? FsmNames { get; set; }

    /// <summary>
    /// Required parent name substring, or null if the parent is not part of the match criteria.
    /// </summary>
    [JsonProperty("parent_name")]
    public string? ParentName { get; set; }

    /// <summary>
    /// Additional component types to initialize when this entity is created.
    /// </summary>
    [JsonProperty("components")]
    public EntityComponentType[]? ComponentTypes { get; set; }

    /// <summary>
    /// Alert range name fragments that identify ranges which may validate an existing approved target,
    /// but must not acquire or switch to a new target.
    /// </summary>
    [JsonProperty("non_acquiring_ranges")]
    public List<string>? NonAcquiringRanges { get; set; }

    /// <summary>
    /// Child entries nested under this entry. Populated from the registry file and validated
    /// during startup. Null if no children are defined.
    /// </summary>
    [JsonProperty("children")]
    public List<EntityRegistryEntry>? Children { get; set; }

    /// <summary>
    /// True if this entry requires FSM-based filtering during lookup.
    /// Set during deserialization; safe to read concurrently.
    /// </summary>
    [JsonIgnore]
    internal bool HasFsmFilter { get; private set; }

    /// <summary>
    /// Fast-lookup set compiled from FsmNames for O(1) comparison checks.
    /// </summary>
    private HashSet<string>? _fsmNameSet;

    [OnDeserialized]
    private void OnDeserialized(StreamingContext _) {
        HasValidType = Enum.TryParse(TypeName, ignoreCase: false, out EntityType type);
        Type = type;

        if (FsmNames is not { Count: > 0 }) return;
        _fsmNameSet = new HashSet<string>(FsmNames, StringComparer.Ordinal);
        HasFsmFilter = true;
    }

    /// <summary>
    /// Returns true if <paramref name="fsmName"/> is in this entry's FSM name filter.
    /// Always false when <see cref="HasFsmFilter"/> is false.
    /// </summary>
    internal bool ContainsFsmName(string fsmName) => _fsmNameSet?.Contains(fsmName) ?? false;
}

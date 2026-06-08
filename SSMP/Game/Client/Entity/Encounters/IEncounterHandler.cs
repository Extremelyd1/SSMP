using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SSMP.Game.Client.Entity.Encounters;

/// <summary>
/// Interface for scene-specific encounter hooks.
/// </summary>
internal interface IEncounterHandler {
    /// <summary>
    /// Gets the list of scene names this handler manages.
    /// </summary>
    IEnumerable<string> SupportedScenes { get; }

    /// <summary>
    /// Called when a supported scene has finished loading and the local scene role is known.
    /// </summary>
    void OnSceneLoaded(Scene scene, bool isHost);

    /// <summary>
    /// Gives the encounter a chance to provide a scene-specific entity match before the global registry is used.
    /// </summary>
    /// <returns><c>true</c> when the encounter provided an entry for this object.</returns>
    bool TryGetEntityEntry(GameObject gameObject, [NotNullWhen(true)] out EntityRegistryEntry? entry);

    /// <summary>
    /// Called when an entity is registered while the encounter is active.
    /// </summary>
    void OnEntityRegistered(Entity entity);

    /// <summary>
    /// Called when a registered entity's FSM changes state while the encounter is active.
    /// </summary>
    void OnEntityFsmStateChanged(Entity entity, PlayMakerFSM fsm, string stateName);

    /// <summary>
    /// Called when a replicated FSM action is applied while the encounter is active.
    /// </summary>
    /// <returns><c>true</c> to suppress the default generic action application.</returns>
    bool OnEntityFsmAction(Entity entity, string stateName, FsmStateAction action);
}

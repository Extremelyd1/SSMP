using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Game.Client.Entity.Encounters;

/// <summary>
/// Static manager that coordinates the discovery, lifecycle, and event routing of scene encounters.
/// </summary>
internal static class EncounterManager {
    private static readonly List<IEncounterHandler> AllHandlers = [];
    private static readonly List<IEncounterHandler> ActiveHandlers = [];

    static EncounterManager() {
        InitializeHandlers();

        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    /// <summary>
    /// Forces static initialization of the encounter manager.
    /// </summary>
    public static void Initialize() {
        Logger.Info("EncounterManager: Initializing encounter system.");
        RefreshActiveHandlers(SceneManager.GetActiveScene());
    }

    /// <summary>
    /// Registers all encounter handlers.
    /// </summary>
    private static void InitializeHandlers() {
        RegisterHandler(new MossMotherEncounterHandler());
    }

    private static void RegisterHandler(IEncounterHandler handler) {
        AllHandlers.Add(handler);
        Logger.Info($"Registered encounter handler: '{handler.GetType().Name}'");
    }

    /// <summary>
    /// Tracks scene changes and refreshes the active handler set.
    /// </summary>
    private static void OnActiveSceneChanged(Scene oldScene, Scene newScene) {
        RefreshActiveHandlers(newScene);
    }

    /// <summary>
    /// Recomputes the handlers that are active for the given scene.
    /// </summary>
    private static void RefreshActiveHandlers(Scene scene) {
        ActiveHandlers.Clear();

        foreach (var handler in AllHandlers) {
            try {
                if (!handler.SupportedScenes.Contains(scene.name)) {
                    continue;
                }

                ActiveHandlers.Add(handler);
                Logger.Info($"Activated encounter handler '{handler.GetType().Name}' for scene '{scene.name}'");
            } catch (Exception e) {
                Logger.Error(
                    $"Error while activating handler '{handler.GetType().Name}' for scene '{scene.name}': {e}"
                );
            }
        }
    }

    /// <summary>
    /// Gives active handlers a chance to provide a scene-specific entity entry before the global registry is used.
    /// </summary>
    public static bool TryGetEntityEntry(GameObject gameObject, [NotNullWhen(true)] out EntityRegistryEntry? entry) {
        foreach (var handler in ActiveHandlers) {
            try {
                if (handler.TryGetEntityEntry(gameObject, out entry)) {
                    return true;
                }
            } catch (Exception e) {
                Logger.Error($"Error in TryGetEntityEntry for handler '{handler.GetType().Name}': {e}");
            }
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// Notifies active handlers that the supported scene is ready and the local scene role is known.
    /// </summary>
    public static void OnSceneLoaded(Scene scene, bool isHost) {
        foreach (var handler in ActiveHandlers) {
            try {
                handler.OnSceneLoaded(scene, isHost);
            } catch (Exception e) {
                Logger.Error($"Error in OnSceneLoaded for handler '{handler.GetType().Name}': {e}");
            }
        }
    }

    /// <summary>
    /// Notifies active handlers that an entity has been registered.
    /// </summary>
    public static void OnEntityRegistered(Entity entity) {
        foreach (var handler in ActiveHandlers) {
            try {
                handler.OnEntityRegistered(entity);
            } catch (Exception e) {
                Logger.Error($"Error in OnEntityRegistered for handler '{handler.GetType().Name}': {e}");
            }
        }
    }

    /// <summary>
    /// Notifies active handlers that an entity FSM changed state.
    /// </summary>
    public static void OnEntityFsmStateChanged(Entity entity, PlayMakerFSM fsm, string stateName) {
        foreach (var handler in ActiveHandlers) {
            try {
                handler.OnEntityFsmStateChanged(entity, fsm, stateName);
            } catch (Exception e) {
                Logger.Error($"Error in OnEntityFsmStateChanged for handler '{handler.GetType().Name}': {e}");
            }
        }
    }

    /// <summary>
    /// Notifies active handlers that an entity FSM action was applied from replicated network data.
    /// </summary>
    public static bool OnEntityFsmAction(Entity entity, PlayMakerFSM fsm, string stateName, FsmStateAction action) {
        foreach (var handler in ActiveHandlers) {
            try {
                if (handler.OnEntityFsmAction(entity, stateName, action)) {
                    return true;
                }
            } catch (Exception e) {
                Logger.Error($"Error in OnEntityFsmAction for handler '{handler.GetType().Name}': {e}");
            }
        }

        return false;
    }
}

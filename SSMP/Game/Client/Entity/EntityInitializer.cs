using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using SSMP.Game.Client.Entity.Action;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace SSMP.Game.Client.Entity;

/// <summary>
/// Class that manages initializing client-side entities to ensure they have correct references within FSM actions
/// to game objects (such as child objects).
/// </summary>
internal static class EntityInitializer {
    /// <summary>
    /// Array of state names that indicates that it is a initializing state.
    /// </summary>
    private static readonly string[] InitStateNames = [
        "init",
        "initiate",
        "initialise",
        "initialize",
        "dormant",
        "pause",
        "init pause",
        "deparents",
        "opened" // For battle gates
    ];

    /// <summary>
    /// Array of types that should be removed from client-side enemies so it doesn't interfere with remote behaviour.
    /// </summary>
    private static readonly Type[] ToRemoveTypes = [
        typeof(Walker),
        typeof(BigCentipede),
        typeof(Crawler)
    ];

    /// <summary>
    /// Array of types of actions that should be skipped during initialization. 
    /// </summary>
    private static readonly Type[] ToSkipTypes = [
        typeof(Tk2dPlayAnimation),
        typeof(HutongGames.PlayMaker.Actions.ActivateAllChildren),
        typeof(SetCollider) // TODO: test whether this has effects on other entities during host transfer (this was
        // added for battle gates)
    ];

    /// <summary>
    /// Initialize the FSM of a client entity by finding initialize states and executing the actions in those states.
    /// </summary>
    /// <param name="fsm">The FSM to initialize.</param>
    public static void InitializeFsm(PlayMakerFSM fsm) {
        // Create a list of states to initialize later
        var statesToInit = new List<FsmState>();
        // Keep track of the indices where the individual initialization states begin in our final list
        var indices = new int[InitStateNames.Length];

        CheckPreProcessFsm(fsm);

        // Go over each state in the FSM
        foreach (var state in fsm.FsmStates) {
            var stateName = state.Name.ToLower();
            var index = Array.IndexOf(InitStateNames, stateName);
            // Check if it is a "init" state
            if (index == -1) {
                continue;
            }

            // Then insert it at the correct index according to our tracked indices
            statesToInit.Insert(indices[index], state);
            // Increase all indices that come after, since we inserted something before
            for (var i = index; i < indices.Length; i++) {
                indices[i]++;
            }
        }

        // Now we can loop over the states in the same order as our "InitStateNames" array
        foreach (var state in statesToInit) {
            Logger.Debug($"Found initialization state: {state.Name}, executing actions");

            // Go over each action and try to execute it by applying empty data to it
            foreach (var action in state.Actions) {
                if (!action.Enabled) {
                    continue;
                }

                if (ToSkipTypes.Contains(action.GetType())) {
                    continue;
                }

                if (!EntityFsmActions.SupportedActionTypes.Contains(action.GetType())) {
                    continue;
                }

                if (action.Fsm == null) {
                    Logger.Error($"FSM in action for state '{state.Name}', action '{action.GetType()}' is null");
                    continue;
                }

                Logger.Debug($"  Executing action {action.GetType()} for initialization");

                EntityFsmActions.ApplyNetworkDataFromAction(null, action);
            }
        }
    }

    /// <summary>
    /// Remove all types that should be removed from a client-side entity object.
    /// </summary>
    /// <param name="gameObject">The game object on which to remove the types.</param>
    /// <param name="entityType">The registered entity type.</param>
    public static void RemoveClientTypes(GameObject gameObject, EntityType entityType) {
        // Keep Rigidbody2D components alive. Native death and corpse components cache their bodies during Awake;
        // destroying one here leaves callbacks such as ActiveCorpse.Update and SetParticleScale.OnUpdate with a
        // Unity-null reference. Remote controller bodies are still made kinematic so they cannot simulate locally.
        if (entityType != EntityType.GrassBall) {
            foreach (var rigidbody in gameObject.GetComponentsInChildren<Rigidbody2D>(true)) {
                if (rigidbody != null) {
                    ConfigureClientRigidbody(rigidbody);
                }
            }
        }

        foreach (var type in ToRemoveTypes) {
            foreach (var component in gameObject.GetComponentsInChildren(type, true)) {
                if (component == null) {
                    continue;
                }

                if (component is Behaviour behaviour) {
                    behaviour.enabled = false;
                }

                UnityEngine.Object.Destroy(component);
            }
        }
    }

    /// <summary>
    /// Makes one client-side rigidbody non-authoritative unless it is owned by corpse logic.
    /// </summary>
    /// <param name="rigidbody">The client rigidbody to configure.</param>
    internal static void ConfigureClientRigidbody(Rigidbody2D rigidbody) {
        if (IsCorpseRigidbody(rigidbody)) {
            return;
        }

        rigidbody.bodyType = RigidbodyType2D.Kinematic;
    }

    /// <summary>
    /// Checks whether a rigidbody belongs to a corpse hierarchy whose native lifecycle requires the original body.
    /// </summary>
    /// <param name="rigidbody">The rigidbody to classify.</param>
    /// <returns>Whether the rigidbody is owned by corpse logic.</returns>
    private static bool IsCorpseRigidbody(Rigidbody2D rigidbody) {
        if (rigidbody.GetComponent<Corpse>() != null ||
            rigidbody.GetComponent<CorpseItems>() != null ||
            rigidbody.GetComponentInParent<Corpse>() != null ||
            rigidbody.GetComponentInParent<CorpseItems>() != null) {
            return true;
        }

        // ActiveCorpse caches GetComponent<Rigidbody2D>() on the same object.
        return rigidbody.GetComponent<ActiveCorpse>() != null;
    }

    /// <summary>
    /// Check whether the given FSM needs to be pre-processed by doing a loop over all states and actions to see if
    /// any references to the FSM are missing, which causes issues if we want to hook or initialize the FSM.
    /// </summary>
    /// <param name="fsm">The PlayMaker FSM to check and potentially pre-process.</param>
    public static void CheckPreProcessFsm(PlayMakerFSM fsm) {
        foreach (var state in fsm.FsmStates) {
            if (state.Fsm == null) {
                Logger.Debug($"Reference to FSM in state '{state.Name}' was null, pre-processing FSM...");
                fsm.Preprocess();
                break;
            }

            var wasPreProcessed = false;
            foreach (var action in state.Actions) {
                if (action.Fsm == null) {
                    Logger.Debug(
                        $"Reference to FSM in action '{action.GetType()}' in state '{state.Name}' was null, pre-processing FSM..."
                    );
                    fsm.Preprocess();
                    wasPreProcessed = true;
                    break;
                }
            }

            if (wasPreProcessed) {
                break;
            }
        }
    }
}

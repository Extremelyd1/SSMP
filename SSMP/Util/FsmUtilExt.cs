using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;

namespace SSMP.Util;

/// <summary>
/// Extension methods for PlayMakerFSM manipulation.
/// </summary>
public static class FsmUtilExt {
    extension(PlayMakerFSM fsm) {
        /// <summary>
        /// Get an FSM action by state name and index.
        /// Returns null if the state does not exist or the index is out of range.
        /// </summary>
        private FsmStateAction? GetAction(string stateName, int index) {
            foreach (var state in fsm.FsmStates) {
                if (state.Name != stateName) continue;
                var actions = state.Actions;
                // Explicit bounds check: no allocation, no silent index extension.
                return index >= 0 && index < actions.Length ? actions[index] : null;
            }

            return null;
        }

        /// <summary>
        /// Get an FSM action by state name, index, and type.
        /// Returns null if the state, index, or cast does not match.
        /// </summary>
        public T? GetAction<T>(string stateName, int index) where T : FsmStateAction {
            return fsm.GetAction(stateName, index) as T;
        }

        /// <summary>
        /// Get the first FSM action of the given type in the given state.
        /// Throws ArgumentException if the state does not exist or the action type is not found.
        /// </summary>
        public T GetFirstAction<T>(string stateName) where T : FsmStateAction {
            var state = fsm.GetState(stateName)
                        ?? throw new ArgumentException($"FSM does not have state \"{stateName}\"", nameof(stateName));

            return state.Actions.OfType<T>().FirstOrDefault()
                   ?? throw new ArgumentException(
                       $"FSM state \"{stateName}\" does not have action of type \"{typeof(T)}\"",
                       nameof(stateName)
                   );
        }

        /// <summary>
        /// Get an FSM state by its name.
        /// Returns null if no such state exists — never throws.
        /// </summary>
        public FsmState? GetState(string stateName) {
            // Simple loop: no intermediate allocations, consistent null-on-miss contract
            // so every caller can handle absence explicitly without catching exceptions.
            foreach (var state in fsm.FsmStates) {
                if (state.Name == stateName)
                    return state;
            }

            return null;
        }

        /// <summary>
        /// Insert an FSM action into a state at the given index.
        /// Silently does nothing if the state does not exist.
        /// </summary>
        public void InsertAction(string stateName, FsmStateAction action, int index) {
            foreach (var state in fsm.FsmStates) {
                if (state.Name != stateName) continue;

                var actions = state.Actions.ToList();
                actions.Insert(index, action);
                state.Actions = actions.ToArray();
                action.Init(state);
                break; // State names are unique within a PlayMaker FSM; no need to continue scanning.
            }
        }

        /// <summary>
        /// Insert a method as an FSM action into a state at the given index.
        /// </summary>
        public void InsertMethod(string stateName, int index, Action method) {
            fsm.InsertAction(stateName, new InvokeMethod(method), index);
        }

        /// <summary>
        /// Remove the action at the given index from the given state.
        /// Throws ArgumentException if the state does not exist.
        /// Throws ArgumentOutOfRangeException if the index is out of bounds.
        /// </summary>
        public void RemoveAction(string stateName, int index) {
            var state = fsm.GetState(stateName)
                        ?? throw new ArgumentException(
                            "FSM does not have a state with the given name", nameof(stateName)
                        );

            var orig = state.Actions;
            if (index < 0 || index >= orig.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Action index is out of range");

            var result = new FsmStateAction[orig.Length - 1];
            Array.Copy(orig, 0, result, 0, index);
            Array.Copy(orig, index + 1, result, index, orig.Length - index - 1);
            state.Actions = result;
        }

        /// <summary>
        /// Remove the first action of the given type from the given state.
        /// Throws ArgumentException if the state does not exist.
        /// Silently does nothing if the action type is not present.
        /// </summary>
        public void RemoveFirstAction<T>(string stateName) {
            var state = fsm.GetState(stateName)
                        ?? throw new ArgumentException(
                            "FSM does not have a state with the given name", nameof(stateName)
                        );

            var skipped = false;
            state.Actions = state.Actions.Where(a => {
                    if (skipped || a.GetType() != typeof(T)) return true;
                    skipped = true;
                    return false;
                }
            ).ToArray();
        }
    }
}

/// <summary>
/// FSM action that invokes a delegate and immediately finishes.
/// </summary>
internal class InvokeMethod : FsmStateAction {
    private readonly Action _action;

    public InvokeMethod(Action a) {
        _action = a;
    }

    public override void OnEnter() {
        _action.Invoke();
        Finish();
    }
}

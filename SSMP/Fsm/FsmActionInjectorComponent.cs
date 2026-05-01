using System;
using System.Collections.Generic;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Fsm;

/// <summary>
/// A component to copy FSM injections between objects. An instantiated object can't copy Actions,
/// so this bridges the gap.
/// </summary>
internal class FsmActionInjectorComponent : MonoBehaviour {
    /// <summary>
    /// Dictionary of all injections by their injection index.
    /// </summary>
    private static readonly Dictionary<int, List<Injection>> AllInjections = [];

    /// <summary>
    /// List of injections for this object.
    /// </summary>
    private List<Injection> _injections = [];

    /// <summary>
    /// Whether injection has taken place.
    /// </summary>
    private bool _injected;

    /// <summary>
    /// Injection index for this object.
    /// </summary>
    [SerializeField]
    private int injectionIndex;

    public void Awake() {
        _injected = false;

        if (AllInjections.TryGetValue(injectionIndex, out var injections)) {
            _injections = injections;
            TryDoInjection();
        }
    }

    /// <summary>
    /// Sets the injections for this component and any future copies of it.
    /// </summary>
    /// <param name="injections">The injections for this Object and any future copies.</param>
    public void SetInjections(List<Injection> injections) {
        // Set injections and index
        _injections = injections;
        injectionIndex = AllInjections.Count + 1;

        // Add to static collection of injections and inject
        AllInjections.Add(injectionIndex, injections);
        TryDoInjection();
    }

    /// <summary>
    /// Tries to do all the defined injections in this instance.
    /// </summary>
    private void TryDoInjection() {
        // Check if injections need to happen
        if (_injected) return;
        if (_injections.Count == 0) return;

        foreach (var injection in _injections) {
            // Locate and patch the FSM
            var fsm = gameObject.LocateMyFSM(injection.fsmName);
            var state = fsm.GetState(injection.fsmStateName);

            FsmStateActionInjector.Inject(state, injection.Hook, injection.actionIndex, injection.hookName);
        }

        _injected = true;
    }

    [Serializable]
    public class Injection {
        /// <summary>
        /// The name of the FSM to inject.
        /// </summary>
        public required string fsmName;

        /// <summary>
        /// An optional name for the hook.
        /// </summary>
        public string hookName = "Fsm Injection";

        /// <summary>
        /// The state on the FSM to inject.
        /// </summary>
        public required string fsmStateName;

        /// <summary>
        /// The index to place the hook action.
        /// </summary>
        public int actionIndex;

        /// <summary>
        /// The hook to run when the action index is reached.
        /// </summary>
        [NonSerialized]
        public required Action<PlayMakerFSM> Hook;
    }
}

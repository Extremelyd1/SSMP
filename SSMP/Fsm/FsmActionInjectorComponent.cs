using System;
using System.Collections.Generic;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Fsm;

/// <summary>
/// A component to copy FSM injections between objects. An instantiated object can't copy Actions, so this bridges the gap.
/// </summary>
internal class FsmActionInjectorComponent : MonoBehaviour {
    private static Dictionary<int, List<Injection>> _allInjections = [];

    private List<Injection> _injections = [];

    private bool _injected = false;

    [SerializeField]
    private int _injectionIndex;

    public void Awake() {
        _injected = false;

        if (_allInjections.TryGetValue(_injectionIndex, out var injections)) {
            _injections = injections;
            TryDoInjection();
        }
    }

    /// <summary>
    /// Sets the injections for this component and any future copies of it
    /// </summary>
    /// <param name="injections">The injections for this Object and any future copies</param>
    public void SetInjections(List<Injection> injections) {
        // Set injections and index
        _injections = injections;
        _injectionIndex = _allInjections.Count + 1;

        // Add to static collection of injections and inject
        _allInjections.Add(_injectionIndex, injections);
        TryDoInjection();
    }

    private void TryDoInjection() {
        // Check if injections need to happen
        if (_injected) return;
        if (_injections.Count == 0) return;

        foreach (var injection in _injections) {
            // Ensure injection is set up correctly
            if (injection.FsmName == null) return;
            if (injection.Hook == null) return;
            if (injection.FsmStateName == null) return;

            // Locate and patch the FSM
            var fsm = gameObject.LocateMyFSM(injection.FsmName);
            var state = fsm.GetState(injection.FsmStateName);

            FsmStateActionInjector.Inject(state, injection.Hook, injection.ActionIndex, injection.HookName);
        }

        _injected = true;
    }

    [Serializable]
    public class Injection {
        /// <summary>
        /// The name of the FSM to inject
        /// </summary>
        public required string FsmName;

        /// <summary>
        /// An optional name for the hook
        /// </summary>
        public string HookName = "Fsm Injection";

        /// <summary>
        /// The state on the FSM to inject
        /// </summary>
        public required string FsmStateName;

        /// <summary>
        /// The index to place the hook action
        /// </summary>
        public int ActionIndex;

        /// <summary>
        /// The hook to run when the action index is reached
        /// </summary>
        [NonSerialized]
        public required Action<PlayMakerFSM> Hook;
    }
}

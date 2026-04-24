using System;
using System.Linq;
using HutongGames.PlayMaker;
using SSMP.Logging;

namespace SSMP.Fsm;

/// <summary>
/// FSM state action that allows to be injected into an FSM's state.
/// </summary>
internal sealed class FsmStateActionInjector : FsmStateAction {
    private static Action? _onUninject;

    /// <summary>
    /// The action to run when the state for this action is entered.
    /// </summary>
    private Action<PlayMakerFSM>? _onStateEnter;

    /// <summary>
    /// Injects a delegate action into an FSM state
    /// </summary>
    private void DoInjection(int index) {
        var stateActions = State.Actions.ToList();

        if (index < stateActions.Count) {
            // Replace existing hooks with this one
            var atIndex = stateActions[index];
            if (atIndex is FsmStateActionInjector injector && injector.Name == Name) {
                injector._onStateEnter = _onStateEnter;
                return;
            }
        }

        stateActions.Insert(index, this);
        State.Actions = stateActions.ToArray();
        State.SaveActions();
    }

    /// <summary>
    /// Removes the delegate action from the FSM state
    /// </summary>
    public void Uninject() {
        var actions = State.Actions.ToList();
        actions.Remove(this);
        State.Actions = actions.ToArray();
        State.SaveActions();

        _onStateEnter = null;
        _onUninject -= Uninject;
    }

    /// <inheritdoc/>
    public override void OnEnter() {
        if (_onStateEnter != null) {
            try {
                _onStateEnter?.Invoke(Fsm.FsmComponent);
            } catch (Exception e) {
                Logger.Error(e.ToString());
            }
        }
        Finish();
    }

    /// <summary>
    /// Injects a custom action into the specified FSM state to execute when the state is entered.
    /// </summary>
    /// <param name="state">The FSM state into which the action will be injected.</param>
    /// <param name="onEnter">An action to execute when the state is entered.</param>
    /// <param name="actionIndex">The index at which to inject the action within the state's action list. Defaults to 0.</param>
    /// <param name="name">A unique name for the injected FSM Action</param>
    /// <returns>The injected action.</returns>
    public static FsmStateActionInjector Inject(FsmState state, Action<PlayMakerFSM> onEnter, int actionIndex = 0, string name = "Fsm Injection") {
        if (state == null) {
            throw new NullReferenceException("Received null state when injecting FSM");
        }

        var action = new FsmStateActionInjector {
            State = state,
            _onStateEnter = onEnter,
            Name = name
        };
        action.DoInjection(actionIndex);

        return action;
    }

    /// <summary>
    /// Removes all injected FSM actions
    /// </summary>
    public static void UninjectAll() {
        _onUninject?.Invoke();
    }
}

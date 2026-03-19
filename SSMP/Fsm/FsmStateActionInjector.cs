using System;
using System.Linq;
using HutongGames.PlayMaker;
using SSMP.Logging;

namespace SSMP.Fsm;

internal sealed class FsmStateActionInjector : FsmStateAction {
    private static Action? _onUninject;
    private Action<PlayMakerFSM>? _onStateEnter;
    private FsmStateActionInjector(FsmState state, Action<PlayMakerFSM> onEnter) {
        Init(state);
        _onStateEnter = onEnter;
        _onUninject += Uninject;
    }

    /// <summary>
    /// Injects a delegate action into an FSM state
    /// </summary>
    private void DoInjection(int index) {
        var stateActions = State.Actions.ToList();
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
                _onStateEnter.Invoke(Fsm.FsmComponent);
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
    /// <returns>The injected action.</returns>
    public static FsmStateActionInjector Inject(FsmState state, Action<PlayMakerFSM> onEnter, int actionIndex = 0) {
        if (state == null) {
            throw new NullReferenceException("Received null state when injecting FSM");
        }

        var action = new FsmStateActionInjector(state, onEnter);
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

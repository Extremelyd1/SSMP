using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HutongGames.PlayMaker;
using Steamworks;

namespace SSMP.Fsm;

internal class FsmStateActionInjector : FsmStateAction {
    private static Action? _onUninject;
    private Action<PlayMakerFSM>? _onStateEnter;
    private FsmStateActionInjector(FsmState state, Action<PlayMakerFSM> onEnter) {
        Fsm = state.Fsm;
        State = state;
        _onStateEnter = onEnter;
        _onUninject += Uninject;
    }

    private void DoInjection(int index) {
        var stateActions = State.Actions.ToList();
        stateActions.Insert(index, this);
        State.Actions = stateActions.ToArray();
        State.SaveActions();
    }

    public void Uninject() {
        var actions = State.Actions.ToList();
        actions.Remove(this);
        State.Actions = actions.ToArray();
        State.SaveActions();
    }

    public override void OnEnter() {
        Finish();
        _onStateEnter?.Invoke(Fsm.FsmComponent);
    }


    public static FsmStateActionInjector Inject(FsmState state, Action<PlayMakerFSM> onEnter, int actionIndex = 0) {
        var action = new FsmStateActionInjector(state, onEnter);
        action.DoInjection(actionIndex);

        return action;
    }

    public static void UninjectAll() {
        _onUninject?.Invoke();
    }
}

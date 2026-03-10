using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HutongGames.PlayMaker;

namespace SSMP.Fsm;

internal class FsmStateActionInjector : FsmStateAction {

    private Action<PlayMakerFSM>? _onStateEnter;
    public override void OnEnter() {
        _onStateEnter?.Invoke(Fsm.FsmComponent);
        Finish();
    }

    public static void Inject(FsmState state, Action<PlayMakerFSM> onEnter) {
        Inject(state, 0, onEnter);
    }
    public static void Inject(FsmState state, int actionIndex, Action<PlayMakerFSM> onEnter) {
        var action = new FsmStateActionInjector();
        action.Fsm = state.Fsm;
        action._onStateEnter = onEnter;

        var stateActions = state.Actions.ToList();
        stateActions.Insert(actionIndex, action);
        state.Actions = stateActions.ToArray();
        state.SaveActions();
    }
}

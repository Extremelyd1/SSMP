using System.Collections;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using SSMP.Networking.Packet.Data;
using SSMP.Util;
using UnityEngine;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
#pragma warning disable CS0618
#pragma warning disable CS8600
#pragma warning disable CS8618

namespace SSMP.Game.Client.Entity.Action;

internal static partial class EntityFsmActions {
    #region SetFsmBool

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetFsmBool action) {
        if (action.setValue == null) {
            return false;
        }

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == action.Fsm.GameObject) {
            return false;
        }

        var setValue = action.setValue.Value;
        data.Packet.Write(setValue);

        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetFsmBool action) {
        var setValue = data.Packet.ReadBool();

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var fsm = ActionHelpers.GetGameObjectFsm(gameObject, action.fsmName.Value);
        if (fsm == null) {
            return;
        }

        var fsmBool = fsm.FsmVariables.FindFsmBool(action.variableName.Value);

        fsmBool?.Value = setValue;
    }

    #endregion

    #region SetFsmInt

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetFsmInt action) {
        if (action.setValue == null) {
            return false;
        }

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == action.Fsm.GameObject) {
            return false;
        }

        var setValue = action.setValue.Value;
        data.Packet.Write(setValue);

        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetFsmInt action) {
        var setValue = data.Packet.ReadInt();

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var fsm = ActionHelpers.GetGameObjectFsm(gameObject, action.fsmName.Value);
        if (fsm == null) {
            return;
        }

        var fsmInt = fsm.FsmVariables.GetFsmInt(action.variableName.Value);

        fsmInt?.Value = setValue;
    }

    #endregion

    #region SetFsmFloat

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetFsmFloat action) {
        // TODO: if action.setValue can be a reference, make sure to network it
        if (action.setValue == null) {
            return false;
        }

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        return gameObject != action.Fsm.GameObject;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetFsmFloat action) {
        if (action.setValue == null) {
            return;
        }

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var fsm = ActionHelpers.GetGameObjectFsm(gameObject, action.fsmName.Value);
        if (fsm == null) {
            return;
        }

        var fsmFloat = fsm.FsmVariables.GetFsmFloat(action.variableName.Value);

        fsmFloat?.Value = action.setValue.Value;
    }

    #endregion

    #region RandomFloat

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, RandomFloat action) {
        data.Packet.Write(action.storeResult.Value);
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, RandomFloat action) {
        action.storeResult.Value = data.Packet.ReadFloat();
    }

    #endregion

    #region SetFsmString

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetFsmString action) {
        // TODO: if action.setValue can be a reference, make sure to network it
        if (action.setValue == null) {
            return false;
        }

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        return gameObject != action.Fsm.GameObject;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetFsmString action) {
        if (action.setValue == null) {
            return;
        }

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var fsm = ActionHelpers.GetGameObjectFsm(gameObject, action.fsmName.Value);
        if (fsm == null) {
            return;
        }

        var fsmString = fsm.FsmVariables.GetFsmString(action.variableName.Value);
        if (fsmString == null) {
            return;
        }

        fsmString.Value = action.setValue.Value;
    }

    #endregion

    #region SendEventByName

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SendEventByName action) {
        return action.eventTarget.gameObject.GameObject.Value != action.Fsm.GameObject.gameObject;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SendEventByName action) {
        if (action.delay.Value < 1.0 / 1000.0) {
            action.Fsm.Event(action.eventTarget, action.sendEvent.Value);
        } else {
            // We need to delay the event sending ourselves, because the FSM that we are executing in is not enabled
            // The usual implementation of SendEventByName will thus not work
            MonoBehaviourUtil.Instance.StartCoroutine(DelayEvent());

            IEnumerator DelayEvent() {
                yield return new WaitForSeconds(action.delay.Value);

                action.Fsm.Event(action.eventTarget, action.sendEvent.Value);
            }
        }
    }

    #endregion

    #region SendEventByNameV2

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SendEventByNameV2 action) {
        return action.eventTarget.gameObject.GameObject.Value != action.Fsm.GameObject.gameObject;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SendEventByNameV2 action) {
        if (action.delay.Value < 1.0 / 1000.0) {
            action.Fsm.Event(action.eventTarget, action.sendEvent.Value);
        } else {
            // We need to delay the event sending ourselves, because the FSM that we are executing in is not enabled
            // The usual implementation of SendEventByNameV2 will thus not work
            MonoBehaviourUtil.Instance.StartCoroutine(DelayEvent());

            IEnumerator DelayEvent() {
                yield return new WaitForSeconds(action.delay.Value);

                action.Fsm.Event(action.eventTarget, action.sendEvent.Value);
            }
        }
    }

    #endregion

    #region SendHealthManagerDeathEvent

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SendHealthManagerDeathEvent action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SendHealthManagerDeathEvent action) {
        var gameObject = action.target.OwnerOption == OwnerDefaultOption.UseOwner
            ? action.Owner
            : action.target.GameObject.Value;

        if (gameObject == null) {
            return;
        }

        var healthManager = gameObject.GetComponent<HealthManager>();
        if (healthManager == null) {
            return;
        }

        healthManager.SendDeathEvent();
    }

    #endregion
}

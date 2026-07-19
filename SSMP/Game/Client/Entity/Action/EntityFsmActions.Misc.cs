using System;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using SSMP.Networking.Packet.Data;
using UnityEngine;
using Object = UnityEngine.Object;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
#pragma warning disable CS0618
#pragma warning disable CS8600
#pragma warning disable CS8618

namespace SSMP.Game.Client.Entity.Action;

internal static partial class EntityFsmActions {
    #region ActivateAllChildren

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(
        EntityNetworkData _,
        HutongGames.PlayMaker.Actions.ActivateAllChildren __
    ) => true;

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(
        EntityNetworkData _,
        HutongGames.PlayMaker.Actions.ActivateAllChildren action
    ) => action.OnEnter();

    #endregion

    #region SetTag

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetTag action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetTag action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        gameObject.tag = action.tag.Value;
    }

    #endregion

    #region DestroyObject

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, DestroyObject action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, DestroyObject action) {
        var gameObject = action.gameObject.Value;
        if (gameObject == null) {
            return;
        }

        var delay = action.delay.Value;
        if (delay <= 0) {
            Object.Destroy(gameObject);
        } else {
            Object.Destroy(gameObject, delay);
        }

        if (action.detachChildren.Value) {
            gameObject.transform.DetachChildren();
        }
    }

    #endregion

    #region SetStringValue

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetStringValue action) {
        return action is { stringVariable: not null, stringValue: not null };
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetStringValue action) {
        action.stringVariable.Value = action.stringValue.Value;
    }

    #endregion

    #region GetRandomChild

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, GetRandomChild action) {
        if (!RandomActionValues.TryGetValue(action, out var queue)) {
            return false;
        }

        if (queue.Count == 0) {
            //Logger.Debug("Getting data for GetRandomChild has not enough items in queue");
            return false;
        }

        var randomIndex = (int) queue.Dequeue();
        data.Packet.Write((byte) randomIndex);

        queue.Clear();

        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, GetRandomChild action) {
        var randomIndex = data.Packet.ReadByte();

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var childCount = gameObject.transform.childCount;
        if (childCount == 0) {
            return;
        }

        action.storeResult.Value = gameObject.transform.GetChild(randomIndex).gameObject;
    }

    #endregion

    #region DestroyComponent

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, DestroyComponent action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, DestroyComponent action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var component = gameObject.GetComponent(ReflectionUtils.GetGlobalType(action.component.Value));
        if (component == null) {
            return;
        }

        Object.Destroy(component);
    }

    #endregion

    #region AddComponent

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, AddComponent action) {
        return !action.removeOnExit.Value;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, AddComponent action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var component = gameObject.AddComponent(ReflectionUtils.GetGlobalType(action.component.Value));
        action.storeComponent.Value = component;
    }

    #endregion

    #region PreBuildTK2DSprites

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, PreBuildTK2DSprites action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, PreBuildTK2DSprites action) {
        var gameObject = action.gameObject.Value;
        if (gameObject == null) {
            return;
        }

        var sprites = action.useChildren
            ? gameObject.GetComponentsInChildren<tk2dSprite>(true)
            : gameObject.GetComponents<tk2dSprite>();

        foreach (var sprite in sprites) {
            sprite.ForceBuild();
        }
    }

    #endregion

    #region CallMethodProper

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, CallMethodProper action) {
        //Logger.Debug($"Getting network data for CallMethodProper: {action.Fsm.GameObject.name}, {action.Fsm.Name}");

        return action.Fsm.GameObject.name.StartsWith("Colosseum Manager") &&
               action.Fsm.Name.Equals("Battle Control") ||
               action.Fsm.GameObject.name.StartsWith("Mantis Lord Throne") &&
               action.Fsm.Name.Equals("Mantis Throne Main") ||
               action.Fsm.GameObject.name.Equals("Radiance") &&
               action.Fsm.Name.Equals("Control");
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, CallMethodProper action) {
        if (action.behaviour.Value == null) {
            return;
        }

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var component = gameObject.GetComponent(action.behaviour.Value) as MonoBehaviour;
        if (component == null) {
            return;
        }

        var type = component.GetType();
        var methodInfo = type.GetMethod(action.methodName.Value);
        if (methodInfo == null) {
            return;
        }

        var parameterInfo = methodInfo.GetParameters();

        object obj;
        if (parameterInfo.Length == 0) {
            obj = methodInfo.Invoke(component, null);
        } else {
            var paramArray = new object[action.parameters.Length];

            for (var i = 0; i < action.parameters.Length; i++) {
                var fsmVar = action.parameters[i];
                fsmVar.UpdateValue();
                paramArray[i] = fsmVar.GetValue();
            }

            try {
                obj = methodInfo.Invoke(component, paramArray);
            } catch (Exception e) {
                //Logger.Error($"Error applying CallMethodProper:\n{e}");
                return;
            }
        }

        if (action.storeResult.Type == VariableType.Unknown) {
            return;
        }

        action.storeResult.SetValue(obj);
    }

    #endregion

    #region SendMessage

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SendMessage action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SendMessage action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        object parameter = action.functionCall.ParameterType switch {
            "Array" => action.functionCall.ArrayParameter.Values,
            "Color" => action.functionCall.ColorParameter.Value,
            "Enum" => action.functionCall.EnumParameter.Value,
            "GameObject" => action.functionCall.GameObjectParameter.Value,
            "Material" => action.functionCall.MaterialParameter.Value,
            "Object" => action.functionCall.ObjectParameter.Value,
            "Quaternion" => action.functionCall.QuaternionParameter.Value,
            "Rect" => action.functionCall.RectParamater.Value,
            "Texture" => action.functionCall.TextureParameter.Value,
            "Vector2" => action.functionCall.Vector2Parameter.Value,
            "Vector3" => action.functionCall.Vector3Parameter.Value,
            "bool" => action.functionCall.BoolParameter.Value,
            "float" => action.functionCall.FloatParameter.Value,
            "int" => action.functionCall.IntParameter.Value,
            "string" => action.functionCall.StringParameter.Value,
            _ => null
        };

        switch (action.delivery) {
            case SendMessage.MessageType.SendMessage:
                gameObject.SendMessage(action.functionCall.FunctionName, parameter, action.options);
                break;
            case SendMessage.MessageType.SendMessageUpwards:
                gameObject.SendMessageUpwards(action.functionCall.FunctionName, parameter, action.options);
                break;
            case SendMessage.MessageType.BroadcastMessage:
                gameObject.BroadcastMessage(action.functionCall.FunctionName, parameter, action.options);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    #endregion

    #region EndGGBossScene

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, EndGGBossScene action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, EndGGBossScene action) {
        if (BossSceneController.Instance) {
            BossSceneController.Instance.EndBossScene();
        }
    }

    #endregion
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using SSMP.Networking.Packet.Data;
using SSMP.Util;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
#pragma warning disable CS0618
#pragma warning disable CS8600
#pragma warning disable CS8618

namespace SSMP.Game.Client.Entity.Action;

internal static partial class EntityFsmActions {
    #region SetScale

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetScale action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == action.Fsm.GameObject) {
            return false;
        }

        if (IsObjectInRegistry(gameObject)) {
            return false;
        }

        var scale = action.vector.IsNone ? gameObject.transform.localScale : action.vector.Value;
        if (!action.x.IsNone) {
            scale.x = action.x.Value;
        }

        if (!action.y.IsNone) {
            scale.y = action.y.Value;
        }

        if (!action.z.IsNone) {
            scale.z = action.z.Value;
        }

        data.Packet.Write(scale.x);
        data.Packet.Write(scale.y);
        data.Packet.Write(scale.z);

        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData? data, SetScale action) {
        Vector3 scale;

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);

        if (data == null) {
            scale = action.vector.IsNone ? gameObject.transform.localScale : action.vector.Value;

            if (!action.x.IsNone) {
                scale.x = action.x.Value;
            }

            if (!action.y.IsNone) {
                scale.y = action.y.Value;
            }

            if (!action.z.IsNone) {
                scale.z = action.z.Value;
            }
        } else {
            scale = new Vector3(
                data.Packet.ReadFloat(),
                data.Packet.ReadFloat(),
                data.Packet.ReadFloat()
            );
        }

        gameObject.transform.localScale = scale;
    }

    #endregion

    #region SetVelocity2d

#pragma warning disable CS0618 // Type or member is obsolete

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetVelocity2d action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return false;
        }

        if (IsObjectInRegistry(gameObject)) {
            //Logger.Debug("Tried getting SetVelocity2d network data, but entity is in registry");
            return false;
        }

        var rigidbody = gameObject.GetComponent<Rigidbody2D>();
        if (rigidbody == null) {
            return false;
        }

        var vector = action.vector.IsNone ? rigidbody.velocity : action.vector.Value;
        if (!action.x.IsNone) {
            vector.x = action.x.Value;
        }

        if (!action.y.IsNone) {
            vector.y = action.y.Value;
        }

        data.Packet.Write(vector.x);
        data.Packet.Write(vector.y);

        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetVelocity2d action) {
        var vector = new Vector2(
            data.Packet.ReadFloat(),
            data.Packet.ReadFloat()
        );

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var rigidbody = gameObject.GetComponent<Rigidbody2D>();
        if (rigidbody == null) {
            return;
        }

        rigidbody.velocity = vector;
    }

    #endregion

    #region SetPosition

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetPosition action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return false;
        }

        if (IsObjectInRegistry(gameObject)) {
            //Logger.Debug("Tried getting SetPosition network data, but entity is in registry");
            return false;
        }

        Vector3 vector3;
        if (!action.vector.IsNone) {
            vector3 = action.vector.Value;
        } else {
            vector3 = action.space == Space.World ? gameObject.transform.position : gameObject.transform.localPosition;
        }

        if (!action.x.IsNone) {
            vector3.x = action.x.Value;
        }

        if (!action.y.IsNone) {
            vector3.y = action.y.Value;
        }

        if (!action.z.IsNone) {
            vector3.z = action.z.Value;
        }

        data.Packet.Write(vector3.x);
        data.Packet.Write(vector3.y);
        data.Packet.Write(vector3.z);

        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData? data, SetPosition action) {
        Vector3 vector3;

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);

        if (data == null) {
            if (gameObject == null) {
                return;
            }

            if (!action.vector.IsNone) {
                vector3 = action.vector.Value;
            } else {
                vector3 = action.space == Space.World
                    ? gameObject.transform.position
                    : gameObject.transform.localPosition;
            }

            if (!action.x.IsNone) {
                vector3.x = action.x.Value;
            }

            if (!action.y.IsNone) {
                vector3.y = action.y.Value;
            }

            if (!action.z.IsNone) {
                vector3.z = action.z.Value;
            }
        } else {
            vector3 = new Vector3(
                data.Packet.ReadFloat(),
                data.Packet.ReadFloat(),
                data.Packet.ReadFloat()
            );

            if (gameObject == null) {
                return;
            }
        }

        if (action.space == Space.World) {
            gameObject.transform.position = vector3;
        } else {
            gameObject.transform.localPosition = vector3;
        }
    }

    #endregion

    #region SetRotation

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetRotation action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return false;
        }

        if (IsObjectInRegistry(gameObject)) {
            //Logger.Debug("Tried getting SetPosition network data, but entity is in registry");
            return false;
        }

        Vector3 vector3;
        if (action.quaternion.IsNone) {
            if (action.vector.IsNone) {
                vector3 = action.space == Space.Self
                    ? gameObject.transform.localEulerAngles
                    : gameObject.transform.eulerAngles;
            } else {
                vector3 = action.vector.Value;
            }
        } else {
            vector3 = action.quaternion.Value.eulerAngles;
        }

        if (!action.xAngle.IsNone) {
            vector3.x = action.xAngle.Value;
        }

        if (!action.yAngle.IsNone) {
            vector3.y = action.yAngle.Value;
        }

        if (!action.zAngle.IsNone) {
            vector3.z = action.zAngle.Value;
        }

        data.Packet.Write(vector3.x);
        data.Packet.Write(vector3.y);
        data.Packet.Write(vector3.z);

        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData? data, SetRotation action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);

        Vector3 euler;
        if (data == null) {
            // Host path: read current state from the FSM action.
            if (gameObject == null) return;

            if (!action.quaternion.IsNone) {
                euler = action.quaternion.Value.eulerAngles;
            } else if (!action.vector.IsNone) {
                euler = action.vector.Value;
            } else {
                euler = action.space == Space.Self
                    ? gameObject.transform.localEulerAngles
                    : gameObject.transform.eulerAngles;
            }

            if (!action.xAngle.IsNone) euler.x = action.xAngle.Value;
            if (!action.yAngle.IsNone) euler.y = action.yAngle.Value;
            if (!action.zAngle.IsNone) euler.z = action.zAngle.Value;
        } else {
            // Client path: always consume packet bytes to keep the stream in sync,
            // even if the target object is gone.
            euler = new Vector3(
                data.Packet.ReadFloat(),
                data.Packet.ReadFloat(),
                data.Packet.ReadFloat()
            );

            if (gameObject == null) return;
        }

        if (action.space == Space.Self) {
            gameObject.transform.localEulerAngles = euler;
        } else {
            gameObject.transform.eulerAngles = euler;
        }
    }

    #endregion

    #region SetBoxCollider2DSizeVector

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetBoxCollider2DSizeVector action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetBoxCollider2DSizeVector action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject1);
        if (gameObject == null) {
            return;
        }

        var collider = gameObject.GetComponent<BoxCollider2D>();
        if (collider == null) {
            return;
        }

        if (!action.size.IsNone) {
            collider.size = action.size.Value;
        }

        if (!action.offset.IsNone) {
            collider.offset = action.offset.Value;
        }
    }

    #endregion

    #region SetVelocityAsAngle

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetVelocityAsAngle action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return false;
        }

        if (IsObjectInRegistry(gameObject)) {
            //Logger.Debug("Tried getting SetVelocityAsAngle network data, but entity is in registry");
            return false;
        }

        data.Packet.Write(action.speed.Value);
        data.Packet.Write(action.angle.Value);

        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetVelocityAsAngle action) {
        var speed = data.Packet.ReadFloat();
        var angle = data.Packet.ReadFloat();

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var rigidbody = gameObject.GetComponent<Rigidbody2D>();
        if (rigidbody == null) {
            return;
        }

        var x = speed * Mathf.Cos(angle * ((float) System.Math.PI / 180f));
        var y = speed * Mathf.Sin(angle * ((float) System.Math.PI / 180f));

        rigidbody.velocity = new Vector2(x, y);
    }

    #endregion

    #region iTweenMoveBy

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, iTweenMoveBy action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return false;
        }

        return !IsObjectInRegistry(gameObject);
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, iTweenMoveBy action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var id = action.itweenID;

        var args = new Hashtable {
            { "amount", action.vector.IsNone ? Vector3.zero : action.vector.Value }, {
                action.speed.IsNone ? "time" : "speed",
                (float) (action.speed.IsNone
                    ? (action.time.IsNone ? 1.0 : action.time.Value)
                    : (double) action.speed.Value)
            },
            { "delay", (float) (action.delay.IsNone ? 0.0 : (double) action.delay.Value) },
            { "easetype", action.easeType },
            { "looptype", action.loopType },
            { "oncomplete", "iTweenOnComplete" },
            { "oncompleteparams", id },
            { "onstart", "iTweenOnStart" },
            { "onstartparams", id },
            { "ignoretimescale", !action.realTime.IsNone && action.realTime.Value },
            { "space", action.space },
            { "name", action.id.IsNone ? "" : (object) action.id.Value }, {
                "axis",
                action.axis == iTweenFsmAction.AxisRestriction.none
                    ? ""
                    : (object) Enum.GetName(typeof(iTweenFsmAction.AxisRestriction), action.axis)
            }
        };

        if (!action.orientToPath.IsNone) {
            args.Add("orienttopath", action.orientToPath.Value);
        }

        if (!action.lookAtObject.IsNone) {
            args.Add(
                "looktarget",
                action.lookAtVector.IsNone
                    ? action.lookAtObject.Value.transform.position
                    : action.lookAtObject.Value.transform.position + action.lookAtVector.Value
            );
        } else if (!action.lookAtVector.IsNone) {
            args.Add("looktarget", action.lookAtVector.Value);
        }

        if (!action.lookAtObject.IsNone || !action.lookAtVector.IsNone) {
            args.Add("looktime", (float) (action.lookTime.IsNone ? 0.0 : (double) action.lookTime.Value));
        }

        action.itweenType = "move";

        iTween.MoveBy(gameObject, args);
    }

    #endregion

    #region iTweenScaleTo

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, iTweenScaleTo action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return false;
        }

        if (IsObjectInRegistry(gameObject)) {
            return false;
        }

        return action.loopType == iTween.LoopType.none;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, iTweenScaleTo action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var vector = action.vectorScale.IsNone ? Vector3.zero : action.vectorScale.Value;

        if (!action.transformScale.IsNone && action.transformScale.Value) {
            vector = action.transformScale.Value.transform.localScale + vector;
        }

        var id = action.itweenID;

        iTween.ScaleTo(
            gameObject, iTween.Hash(
                "scale",
                vector,
                "name",
                action.id.IsNone ? "" : action.id.Value,
                action.speed.IsNone ? "time" : "speed",
                (float) (action.speed.IsNone
                    ? action.time.IsNone ? 1.0 : action.time.Value
                    : (double) action.speed.Value),
                "delay",
                (float) (action.delay.IsNone ? 0.0 : (double) action.delay.Value),
                "easetype",
                action.easeType,
                "looptype",
                action.loopType,
                "oncomplete",
                "iTweenOnComplete",
                "oncompleteparams",
                id,
                "onstart",
                "iTweenOnStart",
                "onstartparams",
                id,
                "ignoretimescale",
                (action.realTime.IsNone ? 0 : action.realTime.Value ? 1 : 0) > 0
            )
        );
    }

    #endregion

    #region SetGravity2dScale

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetGravity2dScale action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return false;
        }

        return !IsObjectInRegistry(gameObject);
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetGravity2dScale action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var rigidBody = gameObject.GetComponent<Rigidbody2D>();
        if (rigidBody == null) {
            return;
        }

        rigidBody.gravityScale = action.gravityScale.Value;
    }

    #endregion

    #region SetCollider

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetCollider action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return false;
        }

        if (IsObjectInRegistry(gameObject)) {
            //Logger.Debug("Tried getting SetCollider network data, but entity is in registry");
            return false;
        }

        data.Packet.Write(action.active.Value);
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData? data, SetCollider action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var collider = gameObject.GetComponent<Collider2D>();
        if (collider == null) {
            return;
        }

        collider.enabled = data == null ? action.active.Value : data.Packet.ReadBool();
    }

    #endregion

    #region SetCircleCollider

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetCircleCollider action) {
        if (action.gameObject == null) {
            return false;
        }

        data.Packet.Write(action.active.Value);
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData? data, SetCircleCollider action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var collider = gameObject.GetComponent<CircleCollider2D>();
        if (collider != null) {
            collider.enabled = data == null ? action.active.Value : data.Packet.ReadBool();
        }
    }

    #endregion

    #region SetPolygonCollider

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetPolygonCollider action) {
        if (action.gameObject == null) {
            return false;
        }

        data.Packet.Write(action.active.Value);
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData? data, SetPolygonCollider action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var collider = gameObject.GetComponent<PolygonCollider2D>();
        if (collider != null) {
            collider.enabled = data == null ? action.active.Value : data.Packet.ReadBool();
        }
    }

    #endregion

    #region MoveLiftChain

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, MoveLiftChain action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, MoveLiftChain action) {
        var go = action.target.GetSafe(action);
        if (go == null) {
            return;
        }

        var liftChain = go.GetComponent<LiftChain>();
        if (liftChain == null) {
            return;
        }

        action.Apply(liftChain);
    }

    #endregion

    #region StopLiftChain

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, StopLiftChain action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, StopLiftChain action) {
        var go = action.target.GetSafe(action);
        if (go == null) {
            return;
        }

        var liftChain = go.GetComponent<LiftChain>();
        if (liftChain == null) {
            return;
        }

        action.Apply(liftChain);
    }

    #endregion

    #region SetIsKinematic2d

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetIsKinematic2d action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetIsKinematic2d action) {
        var go = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (go == null) {
            return;
        }

        var rigidbody = go.GetComponent<Rigidbody2D>();
        if (rigidbody == null) {
            return;
        }

        rigidbody.isKinematic = action.isKinematic.Value;
    }

    #endregion
}

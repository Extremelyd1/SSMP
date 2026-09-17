using HutongGames.PlayMaker.Actions;
using SSMP.Networking.Packet.Data;
using Logger = SSMP.Logging.Logger;
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
    #region CreateObject

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, CreateObject action) {
        // We first check whether this action results in the spawning of an entity that is managed by the
        // system. Because if so, it would already be handled by an EntitySpawn packet instead, and this will only
        // duplicate the spawning and leave it uncontrolled. So we don't send the data at all
        if (EntitySpawnEvent.Invoke(
                new EntitySpawnDetails {
                    Type = EntitySpawnType.FsmAction,
                    Action = action,
                    GameObject = action.storeObject.Value
                }
            )) {
            //Logger.Debug($"Tried getting CreateObject network data, but spawned object is entity");
            return false;
        }

        var original = action.gameObject.Value;
        if (original == null) {
            return false;
        }

        var position = Vector3.zero;
        var euler = Vector3.zero;

        if (action.spawnPoint.Value != null) {
            position = action.spawnPoint.Value.transform.position;
            if (!action.position.IsNone) {
                position += action.position.Value;
            }

            euler = !action.rotation.IsNone ? action.rotation.Value : action.spawnPoint.Value.transform.eulerAngles;
        } else {
            if (!action.position.IsNone) {
                position = action.position.Value;
            }

            if (!action.rotation.IsNone) {
                euler = action.rotation.Value;
            }
        }

        data.Packet.Write(position.x);
        data.Packet.Write(position.y);
        data.Packet.Write(position.z);

        data.Packet.Write(euler.x);
        data.Packet.Write(euler.y);
        data.Packet.Write(euler.z);

        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData? data, CreateObject action) {
        //Logger.Debug("ApplyNetworkDataFromAction CreateObject");

        Vector3 position;
        Vector3 euler;

        if (data == null) {
            position = Vector3.zero;
            euler = Vector3.zero;
            if (action.spawnPoint.Value != null) {
                position = action.spawnPoint.Value.transform.position;

                if (!action.position.IsNone) {
                    position += action.position.Value;
                }

                euler = !action.rotation.IsNone ? action.rotation.Value : action.spawnPoint.Value.transform.eulerAngles;
            } else {
                if (!action.position.IsNone) {
                    position = action.position.Value;
                }

                if (!action.rotation.IsNone) {
                    euler = action.rotation.Value;
                }
            }
        } else {
            position = new Vector3(
                data.Packet.ReadFloat(),
                data.Packet.ReadFloat(),
                data.Packet.ReadFloat()
            );
            euler = new Vector3(
                data.Packet.ReadFloat(),
                data.Packet.ReadFloat(),
                data.Packet.ReadFloat()
            );
        }

        var original = action.gameObject.Value;
        if (original == null) {
            return;
        }

        var spawnedObject = Object.Instantiate(original, position, Quaternion.Euler(euler));
        action.storeObject.Value = spawnedObject;
    }

    #endregion

    #region FireAtTarget

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, FireAtTarget action) {
        var target = action.target;

        var position = target.Value.transform.position;
        data.Packet.Write(position.x);
        data.Packet.Write(position.y);

        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, FireAtTarget action) {
        var posX = data.Packet.ReadFloat();
        var posY = data.Packet.ReadFloat();

        var selfGameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (selfGameObject == null) {
            return;
        }

        var selfPosition = selfGameObject.transform.position;

        var rigidBody = selfGameObject.GetComponent<Rigidbody2D>();
        if (rigidBody == null) {
            return;
        }

        var num = Mathf.Atan2(
            posY + action.position.Value.y - selfPosition.y,
            posX + action.position.Value.x - selfPosition.x
        ) * 57.295776f;

        if (!action.spread.IsNone) {
            num += Random.Range(-action.spread.Value, action.spread.Value);
        }

        rigidBody.velocity = new Vector2(
            action.speed.Value * Mathf.Cos(num * ((float) System.Math.PI / 180f)),
            action.speed.Value * Mathf.Sin(num * ((float) System.Math.PI / 180f))
        );
    }

    #endregion

    #region SetGameObject

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetGameObject action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetGameObject action) {
        action.variable.Value = action.gameObject.Value;
    }

    #endregion

    #region GetOwner

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, GetOwner action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, GetOwner action) {
        action.storeGameObject.Value = action.Owner;
    }

    #endregion

    #region GetHero

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, GetHero action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, GetHero action) {
        var heroController = HeroController.instance;
        action.storeResult.Value = heroController == null ? null : heroController.gameObject;
    }

    #endregion

    #region GetChild

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, GetChild action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, GetChild action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);

        var result = GetChild.DoGetChildByName(gameObject, action.childName.value, action.withTag.value);
        action.storeResult.Value = result;
    }

    #endregion

    #region FindChild

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, FindChild action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, FindChild action) {
        if (action.Fsm == null) {
            return;
        }

        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var transform = gameObject.transform.Find(action.childName.Value);
        action.storeResult.Value = transform == null ? null : transform.gameObject;
    }

    #endregion

    #region FindGameObject

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, FindGameObject action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, FindGameObject action) {
        if (action.withTag.Value == "Untagged") {
            action.store.Value = GameObject.Find(action.objectName.Value);
            return;
        }

        if (string.IsNullOrEmpty(action.objectName.Value)) {
            action.store.Value = GameObject.FindGameObjectWithTag(action.withTag.Value);
            return;
        }

        foreach (var gameObject in GameObject.FindGameObjectsWithTag(action.withTag.Value)) {
            if (gameObject.name == action.objectName.Value) {
                action.store.Value = gameObject;
                return;
            }
        }

        action.store.Value = null;
    }

    #endregion

    #region SetProperty

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetProperty action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetProperty action) {
        action.targetProperty.SetValue();
    }

    #endregion

    #region SetParent

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetParent action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetParent action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var parent = action.parent.Value;
        gameObject.transform.parent = parent != null ? parent.transform : null;

        if (action.resetLocalPosition.Value) {
            gameObject.transform.localPosition = Vector3.zero;
        }

        if (action.resetLocalRotation.Value) {
            gameObject.transform.localRotation = Quaternion.identity;
        }

        if (parent == null) {
            var fsms = gameObject.GetComponents<PlayMakerFSM>();
            foreach (var fsm in fsms) {
                if (fsm.Fsm.Name.Equals("destroy_if_gameobject_null")) {
                    Object.Destroy(fsm);

                    //Logger.Debug($"De-parented object contained \"{fsm.Fsm.Name}\" FSM, removing it");
                }
            }
        }
    }

    #endregion

    #region FindAlertRange

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, FindAlertRange action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, FindAlertRange action) {
        var fsmOwner = action.Fsm.GameObject;
        var target = action.target.GetSafe(action);

        // FindAlertRange stores a live component reference in an FSM object variable. Do not allow
        // a reference from another enemy (or a detached pooled object) to survive initialization or
        // network replay. The vanilla action is expected to search within the current FSM hierarchy.
        if (fsmOwner == null || target == null ||
            (target != fsmOwner && !target.transform.IsChildOf(fsmOwner.transform))) {
            action.storeResult.Value = null;
            return;
        }

        var resolvedRange = AlertRange.Find(target, action.childName);
        if (resolvedRange != null &&
            resolvedRange.gameObject != fsmOwner &&
            !resolvedRange.transform.IsChildOf(fsmOwner.transform)) {
            Logger.Warn(
                $"Rejected cross-instance FindAlertRange result '{resolvedRange.name}' " +
                $"for FSM '{action.Fsm.Name}' on '{fsmOwner.name}'."
            );
            resolvedRange = null;
        }

        action.storeResult.Value = resolvedRange;
    }

    #endregion

    #region GetParent

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, GetParent action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, GetParent action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            action.storeResult.Value = null;
            return;
        }

        var parent = gameObject.transform.parent;
        if (parent == null) {
            action.storeResult.Value = null;
            return;
        }

        action.storeResult.Value = parent.gameObject;
    }

    #endregion

    #region GetPosition

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, GetPosition action) {
        //Logger.Debug($"Getting network data for GetPosition: {action.Fsm.GameObject.name}, {action.Fsm.Name}");

        return action.Fsm.GameObject.name.StartsWith("Colosseum Manager") && action.Fsm.Name.Equals("Battle Control");
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, GetPosition action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var vector3 = action.space == Space.World ? gameObject.transform.position : gameObject.transform.localPosition;
        action.vector.Value = vector3;
        action.x.Value = vector3.x;
        action.y.Value = vector3.y;
        action.z.Value = vector3.z;
    }

    #endregion

    #region PreSpawnGameObjects

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, PreSpawnGameObjects action) {
        {
            var spawnedEntity = false;

            var arr = action.storeArray.Values;
            foreach (var t in arr) {
                var spawnedGo = (GameObject) t;

                if (EntitySpawnEvent.Invoke(
                        new EntitySpawnDetails {
                            Type = EntitySpawnType.FsmAction,
                            Action = action,
                            GameObject = spawnedGo
                        }
                    )) {
                    //Logger.Debug("Tried getting PreSpawnGameObjects network data, but spawned objects contains
                    // entity");
                    spawnedEntity = true;
                }
            }

            if (spawnedEntity) {
                return false;
            }
        }

        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, PreSpawnGameObjects action) {
        if (action.prefab.Value == null) {
            return;
        }

        if (action.storeArray.IsNone) {
            return;
        }

        if (action.spawnAmount.Value <= 0 || action.spawnAmountMultiplier.Value <= 0) {
            return;
        }

        var length = action.spawnAmount.Value * action.spawnAmountMultiplier.Value;
        action.storeArray.Resize(length);

        for (var i = 0; i < length; i++) {
            var go = Object.Instantiate(action.prefab.Value);
            go.SetActive(false);
            action.storeArray.Values[i] = go;
        }
    }

    #endregion
}

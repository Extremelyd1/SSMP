using System.Collections;
using HutongGames.PlayMaker.Actions;
using SSMP.Networking.Packet.Data;
using SSMP.Util;
using UnityEngine;
using Random = UnityEngine.Random;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
#pragma warning disable CS0618
#pragma warning disable CS8600
#pragma warning disable CS8618

namespace SSMP.Game.Client.Entity.Action;

internal static partial class EntityFsmActions {
    #region SpawnObjectFromGlobalPool

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SpawnObjectFromGlobalPool action) {
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
            //Logger.Debug($"Tried getting SpawnObjectFromGlobalPool network data, but spawned object is entity");
            return false;
        }

        var position = Vector3.zero;
        var euler = Vector3.up;

        var spawnPoint = action.spawnPoint.Value;
        if (spawnPoint != null) {
            position = spawnPoint.transform.position;
            if (!action.position.IsNone) {
                position += action.position.Value;
            }

            euler = !action.rotation.IsNone ? action.rotation.Value : spawnPoint.transform.eulerAngles;
        } else {
            if (!action.position.IsNone)
                position = action.position.Value;
            if (!action.rotation.IsNone)
                euler = action.rotation.Value;
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
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SpawnObjectFromGlobalPool action) {
        var position = new Vector3(
            data.Packet.ReadFloat(),
            data.Packet.ReadFloat(),
            data.Packet.ReadFloat()
        );
        var euler = new Vector3(
            data.Packet.ReadFloat(),
            data.Packet.ReadFloat(),
            data.Packet.ReadFloat()
        );

        if (action.gameObject == null) {
            return;
        }

        var spawnedObject = action.gameObject.Value.Spawn(position, Quaternion.Euler(euler));
        action.storeObject.Value = spawnedObject;
    }

    #endregion

    #region FlingObjectsFromGlobalPool

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, FlingObjectsFromGlobalPool action) {
        // We first check whether the game object belonging to the Rigidbody2D in the action is an object that is
        // managed by the system. Because if so, it means that we have already caught its spawning in the IL hook
        // for the action and sent an EntitySpawn packet instead. So we need not also network this action separately.
        var rigidbody = action.rb2d;
        if (rigidbody != null && rigidbody.gameObject != null && IsObjectInRegistry(rigidbody.gameObject)) {
            //Logger.Debug(
            //    "Skipping getting network data for FlingObjectsFromGlobalPool, because spawned objects are managed by
            // system"
            //);
            return false;
        }

        var position = Vector3.zero;

        var spawnPoint = action.spawnPoint.Value;
        if (spawnPoint != null) {
            position = spawnPoint.transform.position;
            if (!action.position.IsNone) {
                position += action.position.Value;
            }
        } else if (!action.position.IsNone) {
            position = action.position.Value;
        }

        if (!RandomActionValues.TryGetValue(action, out var queue)) {
            return false;
        }

        if (queue.Count == 0) {
            //Logger.Debug("Getting data for FlingObjectsFromGlobalPool has not enough items in queue 1");
            return false;
        }

        data.Packet.Write(position.x);
        data.Packet.Write(position.y);
        data.Packet.Write(position.z);

        var numSpawns = (int) queue.Dequeue();
        data.Packet.Write((byte) numSpawns);

        for (var i = 0; i < numSpawns; i++) {
            if (action.originVariationX != null) {
                if (queue.Count == 0) {
                    //Logger.Debug("Getting data for FlingObjectsFromGlobalPool has not enough items in queue 2");
                    return false;
                }

                var originVariationX = (float) queue.Dequeue();
                data.Packet.Write(originVariationX);
            } else {
                data.Packet.Write(0f);
            }

            if (action.originVariationY != null) {
                if (queue.Count == 0) {
                    //Logger.Debug("Getting data for FlingObjectsFromGlobalPool has not enough items in queue 3");
                    return false;
                }

                var originVariationY = (float) queue.Dequeue();
                data.Packet.Write(originVariationY);
            } else {
                data.Packet.Write(0f);
            }

            if (queue.Count < 2) {
                //Logger.Debug("Getting data for FlingObjectsFromGlobalPool has not enough items in queue 4");
                queue.Clear();
                return false;
            }

            var speed = (float) queue.Dequeue();
            var angle = (float) queue.Dequeue();

            data.Packet.Write(speed);
            data.Packet.Write(angle);
        }

        queue.Clear();
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, FlingObjectsFromGlobalPool action) {
        var position = new Vector3(
            data.Packet.ReadFloat(),
            data.Packet.ReadFloat(),
            data.Packet.ReadFloat()
        );

        var numSpawns = data.Packet.ReadByte();
        for (var i = 0; i < numSpawns; i++) {
            var go = action.gameObject.Value.Spawn(position, Quaternion.Euler(Vector3.zero));

            var originVariationX = data.Packet.ReadFloat();
            position.x += originVariationX;

            var originVariationY = data.Packet.ReadFloat();
            position.y += originVariationY;

            go.transform.position = position;

            var speed = data.Packet.ReadFloat();
            var angle = data.Packet.ReadFloat();

            var x = speed * Mathf.Cos(angle * ((float) System.Math.PI / 180f));
            var y = speed * Mathf.Sin(angle * ((float) System.Math.PI / 180f));

            var rigidBody = go.GetComponent<Rigidbody2D>();
            if (rigidBody == null) {
                return;
            }

            rigidBody.velocity = new Vector2(x, y);

            if (!action.FSM.IsNone) {
                FSMUtility.LocateFSM(go, action.FSM.Value).SendEvent(action.FSMEvent.Value);
            }
        }
    }

    #endregion

    #region FlingObjectsFromGlobalPoolVel

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, FlingObjectsFromGlobalPoolVel action) {
        // We first check whether the game object belonging to the Rigidbody2D in the action is an object that is
        // managed by the system. Because if so, it means that we have already caught its spawning in the IL hook
        // for the action and sent an EntitySpawn packet instead. So we need not also network this action separately.
        var rigidbody = action.rb2d;
        if (rigidbody != null && rigidbody.gameObject != null && IsObjectInRegistry(rigidbody.gameObject)) {
            //Logger.Debug(
            //    "Skipping getting network data for FlingObjectsFromGlobalPool, because spawned objects are managed by
            // system"
            //);
            return false;
        }

        var position = Vector3.zero;

        var spawnPoint = action.spawnPoint.Value;
        if (spawnPoint != null) {
            position = spawnPoint.transform.position;
            if (!action.position.IsNone) {
                position += action.position.Value;
            }
        } else if (!action.position.IsNone) {
            position = action.position.Value;
        }

        if (!RandomActionValues.TryGetValue(action, out var queue)) {
            return false;
        }

        if (queue.Count == 0) {
            //Logger.Debug("Getting data for FlingObjectsFromGlobalPoolVel has not enough items in queue 1");
            return false;
        }

        data.Packet.Write(position.x);
        data.Packet.Write(position.y);
        data.Packet.Write(position.z);

        var numSpawns = (int) queue.Dequeue();
        data.Packet.Write((byte) numSpawns);

        for (var i = 0; i < numSpawns; i++) {
            if (action.originVariationX != null) {
                if (queue.Count == 0) {
                    //Logger.Debug("Getting data for FlingObjectsFromGlobalPoolVel has not enough items in queue 2");
                    return false;
                }

                var originVariationX = (float) queue.Dequeue();
                data.Packet.Write(originVariationX);
            } else {
                data.Packet.Write(0f);
            }

            if (action.originVariationY != null) {
                if (queue.Count == 0) {
                    //Logger.Debug("Getting data for FlingObjectsFromGlobalPoolVel has not enough items in queue 3");
                    return false;
                }

                var originVariationY = (float) queue.Dequeue();
                data.Packet.Write(originVariationY);
            } else {
                data.Packet.Write(0f);
            }

            if (queue.Count < 2) {
                //Logger.Debug("Getting data for FlingObjectsFromGlobalPoolVel has not enough items in queue 4");
                queue.Clear();
                return false;
            }

            var speedX = (float) queue.Dequeue();
            var speedY = (float) queue.Dequeue();

            data.Packet.Write(speedX);
            data.Packet.Write(speedY);
        }

        queue.Clear();
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, FlingObjectsFromGlobalPoolVel action) {
        var position = new Vector3(
            data.Packet.ReadFloat(),
            data.Packet.ReadFloat(),
            data.Packet.ReadFloat()
        );

        var numSpawns = data.Packet.ReadByte();
        for (var i = 0; i < numSpawns; i++) {
            var go = action.gameObject.Value.Spawn(position, Quaternion.Euler(Vector3.zero));

            var originVariationX = data.Packet.ReadFloat();
            position.x += originVariationX;

            var originVariationY = data.Packet.ReadFloat();
            position.y += originVariationY;

            go.transform.position = position;

            var speedX = data.Packet.ReadFloat();
            var speedY = data.Packet.ReadFloat();

            var rigidBody = go.GetComponent<Rigidbody2D>();
            if (rigidBody == null) {
                return;
            }

            rigidBody.velocity = new Vector2(speedX, speedY);
        }
    }

    #endregion

    #region FlingObjectsFromGlobalPoolTime

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, FlingObjectsFromGlobalPoolTime action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, FlingObjectsFromGlobalPoolTime action) {
        var coroutine = MonoBehaviourUtil.Instance.StartCoroutine(Behaviour());

        new ActionInState {
            Fsm = action.Fsm,
            StateName = action.State.Name,
            Coroutine = coroutine
        }.Register();
        return;

        IEnumerator Behaviour() {
            while (true) {
                yield return new WaitForSeconds(action.frequency.Value);

                if (action.gameObject.Value == null) {
                    break;
                }

                var position = Vector3.zero;

                var spawnPoint = action.spawnPoint.Value;
                if (spawnPoint != null) {
                    position = spawnPoint.transform.position;
                    if (!action.position.IsNone) {
                        position += action.position.Value;
                    }
                } else if (!action.position.IsNone) {
                    position = action.position.Value;
                }

                var numSpawns = Random.Range(action.spawnMin.Value, action.spawnMax.Value + 1);
                for (var i = 0; i < numSpawns; i++) {
                    var gameObject = action.gameObject.Value.Spawn(position, Quaternion.Euler(Vector3.zero));

                    if (action.originVariationX != null) {
                        position.x += Random.Range(-action.originVariationX.Value, action.originVariationX.Value);
                    }

                    if (action.originVariationY != null) {
                        position.y += Random.Range(-action.originVariationY.Value, action.originVariationY.Value);
                    }

                    gameObject.transform.position = position;

                    var rigidBody = gameObject.GetComponent<Rigidbody2D>();
                    if (rigidBody == null) {
                        continue;
                    }

                    var speed = Random.Range(action.speedMin.Value, action.speedMax.Value);
                    var angle = Random.Range(action.angleMin.Value, action.angleMax.Value);

                    var x = speed * Mathf.Cos(angle * ((float) System.Math.PI / 180f));
                    var y = speed * Mathf.Sin(angle * ((float) System.Math.PI / 180f));

                    rigidBody.velocity = new Vector2(x, y);
                }
            }
        }
    }

    #endregion
}

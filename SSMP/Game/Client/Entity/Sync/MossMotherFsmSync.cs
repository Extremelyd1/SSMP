using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using SSMP.Networking.Packet.Data;
using UnityEngine;

namespace SSMP.Game.Client.Entity.Sync;

/// <summary>
/// Synchronizes Moss Mother's pre-placed stalactites when her FSM starts a rock fall.
/// </summary>
internal sealed class MossMotherFsmSync : IEntityFsmSync {
    private static readonly FieldInfo? RandomWaitTimeField = typeof(RandomWait).GetField(
        "time",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    /// <inheritdoc />
    public void AddNetworkData(EntityNetworkData data, FsmStateAction action) {
        if (action is not SendEventByName sendEvent || !IsStalactiteFall(sendEvent, out var target)) {
            return;
        }

        var stalactites = GetStalactites(target);
        var positions = GetPositions(stalactites);
        data.Packet.Write(positions.Length > 0);
        if (positions.Length == 0) {
            return;
        }

        data.Packet.Write((byte) positions.Length);
        for (var i = 0; i < positions.Length; i++) {
            var position = positions[i];
            data.Packet.Write(position.x);
            data.Packet.Write(position.y);
            data.Packet.Write(GetRandomWaitTime(stalactites[i]));
        }
    }

    /// <inheritdoc />
    public void ApplyNetworkData(EntityNetworkData data, FsmStateAction action) {
        if (action is not SendEventByName sendEvent || !IsStalactiteFall(sendEvent, out var target) ||
            !data.Packet.ReadBool()) {
            return;
        }

        var positions = new Vector2[data.Packet.ReadByte()];
        var waitTimes = new float[positions.Length];
        for (var i = 0; i < positions.Length; i++) {
            positions[i] = new Vector2(data.Packet.ReadFloat(), data.Packet.ReadFloat());
            waitTimes[i] = data.Packet.ReadFloat();
        }

        var stalactites = GetStalactites(target);
        for (var i = 0; i < positions.Length && i < stalactites.Length; i++) {
            SetRandomWaitTime(stalactites[i], waitTimes[i]);

            stalactites[i].simulated = true;
            stalactites[i].bodyType = RigidbodyType2D.Dynamic;

            var position = stalactites[i].transform.position;
            position.x = positions[i].x;
            position.y = positions[i].y;
            stalactites[i].position = new Vector2(position.x, position.y);
            stalactites[i].transform.position = position;
        }
    }

    /// <summary>Checks whether an FSM action starts Moss Mother's stalactite fall.</summary>
    private static bool IsStalactiteFall(SendEventByName action, out GameObject target) {
        target = action.eventTarget.gameObject.GameObject.Value;
        return target != null && target.name.StartsWith("Stals", StringComparison.Ordinal) &&
               action.sendEvent.Value == "FALL";
    }

    /// <summary>Captures the current host positions of Moss Mother's stalactites.</summary>
    private static Vector2[] GetPositions(Rigidbody2D[] stalactites) {
        var positions = new Vector2[stalactites.Length];
        for (var i = 0; i < stalactites.Length; i++) {
            var position = stalactites[i].transform.position;
            positions[i] = new Vector2(position.x, position.y);
        }

        return positions;
    }

    /// <summary>Reads the host-selected delay for a stalactite that is in its anticipation state.</summary>
    private static float GetRandomWaitTime(Rigidbody2D stalactite) {
        var randomWait = GetRandomWait(stalactite);
        if (randomWait == null || RandomWaitTimeField == null) {
            return -1f;
        }

        return (float) RandomWaitTimeField.GetValue(randomWait)!;
    }

    /// <summary>Applies the host-selected anticipation delay to a remote stalactite.</summary>
    private static void SetRandomWaitTime(Rigidbody2D stalactite, float waitTime) {
        if (waitTime < 0f || RandomWaitTimeField == null) {
            return;
        }

        var randomWait = GetRandomWait(stalactite);
        if (randomWait == null) {
            return;
        }

        RandomWaitTimeField.SetValue(randomWait, waitTime);
    }

    private static RandomWait? GetRandomWait(Rigidbody2D stalactite) {
        var fsm = stalactite.GetComponent<PlayMakerFSM>();
        if (fsm == null || fsm.ActiveStateName != "Antic") {
            return null;
        }

        foreach (var state in fsm.FsmStates) {
            if (state.Name != "Antic") {
                continue;
            }

            foreach (var action in state.Actions) {
                if (action is RandomWait randomWait) {
                    return randomWait;
                }
            }
        }

        return null;
    }

    /// <summary>Gets the rigidbodies belonging to Moss Mother's stalactites.</summary>
    private static Rigidbody2D[] GetStalactites(GameObject target) {
        var rigidbodies = target.GetComponentsInChildren<Rigidbody2D>(true);
        var stalactites = new List<Rigidbody2D>(rigidbodies.Length);
        foreach (var rigidbody in rigidbodies) {
            if (rigidbody.gameObject.name.StartsWith("Mossbone Stalactite", StringComparison.Ordinal)) {
                stalactites.Add(rigidbody);
            }
        }

        return stalactites.ToArray();
    }
}

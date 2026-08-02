using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using SSMP.Game.Client.Entity.Sync;
using SSMP.Networking.Packet.Data;
using SSMP.Util;
using UnityEngine;
using Random = UnityEngine.Random;
using Logger = SSMP.Logging.Logger;

// ReSharper disable NotAccessedField.Local
// ReSharper disable CollectionNeverUpdated.Local
// ReSharper disable AssignNullToNotNullAttribute
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider
// adding the 'required' modifier or declaring as nullable.

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local

namespace SSMP.Game.Client.Entity.Action;

/// <summary>
/// Static class containing method that transform FSM actions into network-able data and applying networked data
/// into the FSM actions implementations. 
/// </summary>
internal static partial class EntityFsmActions {
    /// <summary>
    /// The prefix of a method name that transforms an FSM action into network-able data.
    /// </summary>
    private const string GetMethodNamePrefix = "Get";

    /// <summary>
    /// The prefix of a method name that applies network data into an FSM action.
    /// </summary>
    private const string ApplyMethodNamePrefix = "Apply";

    /// <summary>
    /// Binding flags for accessing the private static methods in this class.
    /// </summary>
    private const BindingFlags StaticNonPublicFlags = BindingFlags.Static | BindingFlags.NonPublic;

    /// <summary>
    /// Set containing types of actions that are supported for transformation by a method in this class.
    /// </summary>
    public static readonly HashSet<Type> SupportedActionTypes = [];

    /// <summary>
    /// Event that is called when an entity is spawned from an object.
    /// </summary>
    public static event Func<EntitySpawnDetails, bool> EntitySpawnEvent;

    /// <summary>
    /// Dictionary mapping a type of an FSM action to the corresponding method info of the "get" method in this class.
    /// </summary>
    private static readonly Dictionary<Type, MethodInfo> TypeGetMethodInfos = new();

    /// <summary>
    /// Dictionary mapping a type of an FSM action to the corresponding method info of the "apply" method in this class.
    /// </summary>
    private static readonly Dictionary<Type, MethodInfo> TypeApplyMethodInfos = new();

    /// <summary>
    /// Dictionary containing queues of objects for a FSM action that has been executed on a host entity.
    /// Used to log the results of random calls to network to clients.
    /// </summary>
    private static readonly ConditionalWeakTable<FsmStateAction, Queue<object>> RandomActionValues = new();

    /// <summary>
    /// List of actions that are executing while in a state and need to be stopped again when the state is exited.
    /// </summary>
    private static readonly List<ActionInState> ActionsInState = [];

    /// <summary>
    /// ILHook for FlingObjectsFromGlobalPool.OnEnter.
    /// </summary>
    private static ILHook? _flingPoolHook;

    /// <summary>
    /// ILHook for FlingObjectsFromGlobalPoolVel.OnEnter.
    /// </summary>
    private static ILHook? _flingPoolVelHook;

    /// <summary>
    /// ILHook for FlingObjectsFromGlobalPoolTime.OnUpdate.
    /// </summary>
    private static ILHook? _flingPoolTimeUpdateHook;

    /// <summary>
    /// ILHook for FlingObjectsFromGlobalPoolTime.OnEnter (NOP emit).
    /// </summary>
    private static ILHook? _flingPoolTimeEnterNopHook;

    /// <summary>
    /// ILHook for GetRandomChild.DoGetRandomChild.
    /// </summary>
    private static ILHook? _getRandomChildHook;

    /// <summary>
    /// ILHook for SpawnBloodTime.OnEnter (NOP emit).
    /// </summary>
    private static ILHook? _spawnBloodTimeNopHook;

    /// <summary>
    /// Static constructor that initializes the set and dictionaries by checking all methods in the class.
    /// </summary>
    /// <exception cref="Exception"></exception>
    static EntityFsmActions() {
        var methodInfos = typeof(EntityFsmActions).GetMethods(StaticNonPublicFlags);

        foreach (var methodInfo in methodInfos) {
            var parameterInfos = methodInfo.GetParameters();
            if (parameterInfos.Length != 2) {
                // Can't be a method that gets or applies entity network data
                continue;
            }

            // Filter out the base methods
            var parameterType = parameterInfos[1].ParameterType;
            if (parameterType.IsAbstract || !parameterType.IsSubclassOf(typeof(FsmStateAction))) {
                continue;
            }

            SupportedActionTypes.Add(parameterType);

            if (methodInfo.Name.StartsWith(GetMethodNamePrefix)) {
                TypeGetMethodInfos.Add(parameterType, methodInfo);
            } else if (methodInfo.Name.StartsWith(ApplyMethodNamePrefix)) {
                TypeApplyMethodInfos.Add(parameterType, methodInfo);
            } else {
                throw new Exception("Method was defined that does not adhere to the method naming");
            }
        }

        _flingPoolHook = new ILHook(
            typeof(FlingObjectsFromGlobalPool).GetMethod(
                nameof(FlingObjectsFromGlobalPool.OnEnter),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            )!,
            FlingObjectsFromGlobalPoolOnEnter
        );
        _flingPoolVelHook = new ILHook(
            typeof(FlingObjectsFromGlobalPoolVel).GetMethod(
                nameof(FlingObjectsFromGlobalPoolVel.OnEnter),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            )!,
            FlingObjectsFromGlobalPoolVelOnEnter
        );
        _flingPoolTimeUpdateHook = new ILHook(
            typeof(FlingObjectsFromGlobalPoolTime).GetMethod(
                "OnUpdate",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            )!,
            FlingObjectsFromGlobalPoolTimeOnUpdate
        );
        _getRandomChildHook = new ILHook(
            typeof(GetRandomChild).GetMethod(
                "DoGetRandomChild",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            )!,
            GetRandomChildOnDoGetRandomChild
        );

        // Register IL hooks for the OnEnter method of certain classes. These OnEnter methods do not
        // have a method body and thus no IL instructions (apart from ret). Hooking this in the FsmActionHooks class
        // will not work, so we emit a NOP instruction to the body to make it hookable
        _flingPoolTimeEnterNopHook = new ILHook(
            typeof(FlingObjectsFromGlobalPoolTime).GetMethod(
                nameof(FlingObjectsFromGlobalPoolTime.OnEnter),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            )!,
            EmitNop
        );
        _spawnBloodTimeNopHook = new ILHook(
            typeof(SpawnBloodTime).GetMethod(
                nameof(SpawnBloodTime.OnEnter),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            )!,
            EmitNop
        );
        return;

        // Register the IL hooks for modifying FSM action methods
        void EmitNop(ILContext il) => new ILCursor(il).Emit(OpCodes.Nop);
    }

    /// <summary>
    /// Gets network-able data from the given action and puts it in the given <see cref="EntityNetworkData"/> instance.
    /// </summary>
    /// <param name="data">The instance to put the data into.</param>
    /// <param name="action">The action to transform.</param>
    /// <param name="sync">Optional entity-specific synchronization adapter.</param>
    /// <returns>Whether from this action network-able data was made.</returns>
    /// <exception cref="InvalidOperationException">Thrown if there is no suitable method for the action and thus
    /// no network data is written.</exception>
    public static bool GetNetworkDataFromAction(
        EntityNetworkData data,
        FsmStateAction action,
        IEntityFsmSync? sync = null
    ) {
        var actionType = action.GetType();
        if (!TypeGetMethodInfos.TryGetValue(actionType, out var methodInfo)) {
            throw new InvalidOperationException(
                $"Given action type: {action.GetType()} does not have an associated method to get"
            );
        }

        var returnObject = methodInfo.Invoke(
            null,
            StaticNonPublicFlags,
            null,
            [data, action],
            null!
        );

        // Return whether the return object is a bool and has the value 'true'
        var hasNetworkData = returnObject is true;
        if (hasNetworkData) {
            sync?.AddNetworkData(data, action);
        }

        return hasNetworkData;
    }

    /// <summary>
    /// Reads networked data from the given instance and mimics the execution of the given FSM action.
    /// </summary>
    /// <param name="data">The instance from which to get the data.</param>
    /// <param name="action">The FSM action to mimic execution for.</param>
    /// <param name="sync">Optional entity-specific synchronization adapter.</param>
    /// <exception cref="InvalidOperationException">Thrown if there is no suitable method for the action and thus
    /// no FSM action will be mimicked.</exception>
    public static void ApplyNetworkDataFromAction(
        EntityNetworkData? data,
        FsmStateAction action,
        IEntityFsmSync? sync = null
    ) {
        var actionType = action.GetType();
        if (!TypeApplyMethodInfos.TryGetValue(actionType, out var methodInfo)) {
            throw new InvalidOperationException(
                $"Given action type: {action.GetType()} does not have an associated method to apply"
            );
        }

        try {
            methodInfo.Invoke(
                null,
                StaticNonPublicFlags,
                null,
                [data, action],
                null!
            );
        } catch (Exception e) {
            //Logger.Warn($"Apply method threw exception: {e.GetType()}, {e.Message}, {e.StackTrace}");

            e = e.InnerException;
            while (e != null) {
                //Logger.Warn($"  Inner exception: {e.GetType()}, {e.Message}, {e.StackTrace}");

                e = e.InnerException;
            }

            return;
        }

        if (data != null) {
            sync?.ApplyNetworkData(data, action);
        }
    }

    /// <summary>
    /// Checks whether the given game object is in the entity registry and can thus be registered as an entity in
    /// the system.
    /// </summary>
    /// <param name="gameObject">The game object to check for.</param>
    /// <returns>true if the given game object is in the entity registry; otherwise false.</returns>
    private static bool IsObjectInRegistry(GameObject gameObject) {
        return EntityRegistry.TryGetEntry(gameObject, out _);
    }

    /// <summary>
    /// Method to call the spawn event externally. TODO: refactor this into something more appropriate
    /// </summary>
    /// <param name="details">The spawn details for the event.</param>
    /// <returns>Whether an entity was registered from this spawn.</returns>
    public static void CallEntitySpawnEvent(EntitySpawnDetails details) {
        EntitySpawnEvent?.Invoke(details);
    }

    /// <summary>
    /// Emit intercept instruction on the next Unity Random Range() call for the given IL cursor.
    /// </summary>
    /// <param name="c">The cursor for the IL context of the method.</param>
    /// <typeparam name="TValue">The return type of the random call.</typeparam>
    /// <typeparam name="TObject">The type of the FSM state action in which the random call occurs.</typeparam>
    private static void EmitRandomInterceptInstructions<TValue, TObject>(ILCursor c)
        where TValue : notnull
        where TObject : FsmStateAction {
        // Goto the next call instruction for Random.Range()
        c.GotoNext(i => i.MatchCall(typeof(Random), "Range"));

        // Move the cursor after the call instruction
        c.Index++;

        // Push the current instance of the class onto the stack
        c.Emit(OpCodes.Ldarg_0);

        // Emit a delegate that pops the current random value off the stack and puts it back after some processing 
        c.EmitDelegate<Func<TValue, TObject, TValue>>((value, instance) => {
                // We need to check whether the game object that is being spawned with this action is not an object
                // managed by the system. Because if so, we do not store the random values because the action for it
                // is not being networked. Only the game object spawn is networked with an EntitySpawn packet directly.
                if (typeof(TObject).GetField(
                        "gameObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    )?.GetValue(instance) is FsmGameObject fsmGameObject && fsmGameObject.Value != null &&
                    IsObjectInRegistry(fsmGameObject.Value)) {
                    return value;
                }

                if (!RandomActionValues.TryGetValue(instance, out var queue)) {
                    queue = new Queue<object>();
                    RandomActionValues.Add(instance, queue);
                }

                queue.Enqueue(value);

                return value;
            }
        );
    }

    /// <summary>
    /// IL edit method for modifying the <see cref="FlingObjectsFromGlobalPool"/>
    /// <see cref="FlingObjectsFromGlobalPool.OnEnter"/> method to store the results of the random calls.
    /// </summary>
    private static void FlingObjectsFromGlobalPoolOnEnter(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            // Emit instructions for Random.Range calls for 1 int and 4 floats 
            EmitRandomInterceptInstructions<int, FlingObjectsFromGlobalPool>(c);
            EmitRandomInterceptInstructions<float, FlingObjectsFromGlobalPool>(c);
            EmitRandomInterceptInstructions<float, FlingObjectsFromGlobalPool>(c);
            EmitRandomInterceptInstructions<float, FlingObjectsFromGlobalPool>(c);
            EmitRandomInterceptInstructions<float, FlingObjectsFromGlobalPool>(c);

            // Reset cursor
            c = new ILCursor(il);

            // Goto the next call instruction for ObjectPoolExtensions.Spawn
            c.GotoNext(i => i.MatchCall(typeof(ObjectPoolExtensions), "Spawn"));

            // Move the cursor after the call instruction
            c.Index++;

            // Push the current instance of the class onto the stack
            c.Emit(OpCodes.Ldarg_0);

            // Emit a delegate that pops the spawned game object off the stack and uses it, then puts it back again
            c.EmitDelegate<Func<GameObject, FlingObjectsFromGlobalPool, GameObject>>((go, action) => {
                    //Logger.Debug($"Delegate of FlingObjectsFromGlobalPool: {go.name}");
                    if (EntitySpawnEvent != null && EntitySpawnEvent.Invoke(
                            new EntitySpawnDetails {
                                Type = EntitySpawnType.FsmAction,
                                Action = action,
                                GameObject = go
                            }
                        )) {
                        //Logger.Debug("FlingObjectsFromGlobalPool IL spawned object is entity");
                    }

                    return go;
                }
            );
        } catch (Exception e) {
            Logger.Error($"Could not change FlingObjectsFromGlobalPool#OnEnter IL:\n{e}");
        }
    }

    /// <summary>
    /// IL edit method for modifying the <see cref="FlingObjectsFromGlobalPoolVel"/>
    /// <see cref="FlingObjectsFromGlobalPoolVel.OnEnter"/> method to store the results of the random calls.
    /// </summary>
    private static void FlingObjectsFromGlobalPoolVelOnEnter(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            // Emit instructions for Random.Range calls for 1 int and 4 floats 
            EmitRandomInterceptInstructions<int, FlingObjectsFromGlobalPoolVel>(c);
            EmitRandomInterceptInstructions<float, FlingObjectsFromGlobalPoolVel>(c);
            EmitRandomInterceptInstructions<float, FlingObjectsFromGlobalPoolVel>(c);
            EmitRandomInterceptInstructions<float, FlingObjectsFromGlobalPoolVel>(c);
            EmitRandomInterceptInstructions<float, FlingObjectsFromGlobalPoolVel>(c);

            // Reset cursor
            c = new ILCursor(il);

            // Goto the next call instruction for ObjectPoolExtensions.Spawn
            c.GotoNext(i => i.MatchCall(typeof(ObjectPoolExtensions), "Spawn"));

            // Move the cursor after the call instruction
            c.Index++;

            // Push the current instance of the class onto the stack
            c.Emit(OpCodes.Ldarg_0);

            // Emit a delegate that pops the spawned game object off the stack and uses it, then puts it back again
            c.EmitDelegate<Func<GameObject, FlingObjectsFromGlobalPoolVel, GameObject>>((go, action) => {
                    Logger.Debug($"Delegate of FlingObjectsFromGlobalPoolVel: {go.name}");
                    if (EntitySpawnEvent != null && EntitySpawnEvent.Invoke(
                            new EntitySpawnDetails {
                                Type = EntitySpawnType.FsmAction,
                                Action = action,
                                GameObject = go
                            }
                        )) {
                        //Logger.Debug("FlingObjectsFromGlobalPoolVel IL spawned object is entity");
                    }

                    return go;
                }
            );
        } catch (Exception e) {
            Logger.Error($"Could not change FlingObjectsFromGlobalPoolVel#OnEnter IL:\n{e}");
        }
    }

    /// <summary>
    /// IL edit method for modifying the <see cref="FlingObjectsFromGlobalPoolTime"/>
    /// <see cref="FlingObjectsFromGlobalPoolTime.OnUpdate"/> method to network the repeated spawning of objects.
    /// </summary>
    private static void FlingObjectsFromGlobalPoolTimeOnUpdate(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            // Goto the next call instruction for Random.Range()
            c.GotoNext(i => i.MatchCall(typeof(ObjectPoolExtensions), "Spawn"));

            // Move the cursor after the call instruction
            c.Index++;

            // Push the current instance of the class onto the stack
            c.Emit(OpCodes.Ldarg_0);

            // Emit a delegate that pops the spawned object off the stack and pushes it onto it again
            c.EmitDelegate<Func<GameObject, FlingObjectsFromGlobalPoolTime, GameObject>>((gameObject, action) => {
                    EntitySpawnEvent?.Invoke(
                        new EntitySpawnDetails {
                            Type = EntitySpawnType.FsmAction,
                            Action = action,
                            GameObject = gameObject
                        }
                    );

                    return gameObject;
                }
            );
        } catch (Exception e) {
            Logger.Error($"Could not change FlingObjectsFromGlobalPoolTime#OnUpdate IL:\n{e}");
        }
    }

    /// <summary>
    /// IL edit method for modifying the <see cref="GetRandomChild"/> DoGetRandomChild
    /// method to store the results of the random calls.
    /// </summary>
    private static void GetRandomChildOnDoGetRandomChild(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            // Emit instructions for Random.Range calls for 1 int and 4 floats 
            EmitRandomInterceptInstructions<int, GetRandomChild>(c);
        } catch (Exception e) {
            Logger.Error($"Could not change GetRandomChild#DoGetRandomChild IL:\n{e}");
        }
    }

    /// <summary>
    /// Register a state change for the given FSM. Will propagate this change to all actions that are running in
    /// that state.
    /// </summary>
    /// <param name="fsm">The FSM that changed states.</param>
    /// <param name="stateName">The name of the state that was changed to.</param>
    public static void RegisterStateChange(HutongGames.PlayMaker.Fsm fsm, string stateName) {
        //Logger.Debug($"RegisterStateChange: {fsm.Name}, {stateName}");

        for (var i = ActionsInState.Count - 1; i >= 0; i--) {
            var actionInState = ActionsInState[i];

            //Logger.Debug($"  Action in state: {actionInState.Fsm.Name}, {actionInState.StateName}");

            if (actionInState.Fsm == fsm && actionInState.StateName != stateName) {
                //Logger.Debug("EntityFsmActions: state changed, cancelling action in state");
                actionInState.ExitState();
                ActionsInState.RemoveAt(i);
            }
        }
    }


    /// <summary>
    /// Class that keeps track of an action that executes while in a certain state of the FSM.
    /// </summary>
    private class ActionInState {
        /// <summary>
        /// The FSM of the action that is executing.
        /// </summary>
        public HutongGames.PlayMaker.Fsm Fsm { get; init; }

        /// <summary>
        /// The name of the state in which this action executes.
        /// </summary>
        public string StateName { get; init; }

        /// <summary>
        /// The coroutine that should be stopped when the state is exited.
        /// </summary>
        public Coroutine Coroutine { private get; init; }

        /// <summary>
        /// The action that should be executed when the state is exited.
        /// </summary>
        public System.Action ExitAction { private get; init; }

        /// <summary>
        /// Register this action by adding it to the list.
        /// </summary>
        public void Register() {
            ActionsInState.Add(this);
        }

        /// <summary>
        /// Call when the state is exited, will stop the coroutine and execute the exit action.
        /// </summary>
        public void ExitState() {
            if (Coroutine != null) {
                MonoBehaviourUtil.Instance.StopCoroutine(Coroutine);
            }

            ExitAction?.Invoke();
        }
    }
}

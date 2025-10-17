using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using SSMP.Logging;
using UnityEngine.Audio;

namespace SSMP.Hooks;

// TODO: create method for de-registering the hooks
/// <summary>
/// Static class that manages and exposes custom hooks that are not possible with On hooks or ModHooks. Uses IL modification
/// to embed event calls in certain methods.
/// </summary>
public static class CustomHooks {
    /// <summary>
    /// The binding flags for obtaining certain types for hooking.
    /// </summary>
    private const BindingFlags BindingFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    
    /// <summary>
    /// The instruction match set for matching the instructions below. This is the call to
    /// HeroController.SendHeroInPosition.
    /// </summary>
    // IL_00c5: ldarg.0      // this
    // IL_00c6: ldc.i4.0
    // IL_00c7: call         instance void HeroController::SendHeroInPosition(bool)
    private static readonly Func<Instruction, bool>[] HeroInPositionInstructions = [
        i => i.MatchLdarg(0) || i.MatchLdloc(1),
        i => i.MatchLdcI4(out _),
        i => i.MatchCall(typeof(HeroController), "SendHeroInPosition")
    ];

    /// <summary>
    /// Event for when the player object is done being transformed (changed position, scale) after entering a scene.
    /// </summary>
    public static event Action? AfterEnterSceneHeroTransformed;

    /// <summary>
    /// Event for when the AudioManager.ApplyMusicCue method is called from the ApplyMusicCue FSM action.
    /// </summary>
    public static event Action<ApplyMusicCue>? ApplyMusicCueFromFsmAction;

    /// <summary>
    /// Event for when the AudioMixerSnapshot.TransitionTo method is called from the TransitionToAudioSnapshot FSM
    /// action.
    /// </summary>
    public static event Action<TransitionToAudioSnapshot>? TransitionToAudioSnapshotFromFsmAction;

    /// <summary>
    /// Internal event for <see cref="HeroControllerStartAction"/>.
    /// </summary>
    private static event Action? HeroControllerStartActionInternal;
    
    /// <summary>
    /// Event that executes when the HeroController starts or executes its subscriber immediately if the HeroController
    /// is already active.
    /// </summary>
    public static event Action HeroControllerStartAction {
        add {
            if (HeroController.UnsafeInstance) {
                value.Invoke();
            }
            
            HeroControllerStartActionInternal += value;
        }

        remove => HeroControllerStartActionInternal -= value;
    }

    /// <summary>
    /// Initialize the class by registering the IL/On hooks.
    /// </summary>
    [SuppressMessage("ReSharper", "ObjectCreationAsStatement")]
    public static void Initialize() {
        new ILHook(
            typeof(HeroController).GetMethod(nameof(HeroController.EnterSceneDreamGate), BindingFlags),
            HeroControllerOnEnterSceneDreamGate
        );

        new ILHook(
            typeof(HeroController).GetMethod(nameof(HeroController.EnterScene)).GetStateMachineTarget(), 
            HeroControllerOnEnterScene
        );

        new ILHook(
            typeof(HeroController).GetMethod(nameof(HeroController.EnterHeroSubHorizontal), BindingFlags).GetStateMachineTarget(),
            HeroControllerOnEnterHeroSubHorizontal
        );

        new ILHook(
            typeof(HeroController).GetMethod(nameof(HeroController.Respawn)).GetStateMachineTarget(), 
            HeroControllerOnRespawn
        );

        // IL.HutongGames.PlayMaker.Actions.ApplyMusicCue.OnEnter += ApplyMusicCueOnEnter;
        // IL.HutongGames.PlayMaker.Actions.TransitionToAudioSnapshot.OnEnter += TransitionToAudioSnapshotOnEnter;

        new Hook(
            typeof(HeroController).GetMethod(nameof(HeroController.Start), BindingFlags), 
            HeroControllerOnStart
        );
    }

    /// <summary>
    /// IL Hook for the HeroController EnterSceneDreamGate method. Calls an event within the method.
    /// </summary>
    private static void HeroControllerOnEnterSceneDreamGate(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            EmitAfterEnterSceneEventHeroInPosition(c);
        } catch (Exception e) {
            Logger.Error($"Could not change HeroControllerOnEnterSceneDreamGate IL: \n{e}");
        }
    }

    /// <summary>
    /// IL Hook for the HeroController EnterScene method. Calls an event multiple times within the method.
    /// </summary>
    private static void HeroControllerOnEnterScene(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            for (var i = 0; i < 3; i++) {
                EmitAfterEnterSceneEventHeroInPosition(c);
            }
        } catch (Exception e) {
            Logger.Error($"Could not change HeroController#EnterScene IL: \n{e}");
        }
    }
    
    /// <summary>
    /// IL Hook for the HeroController EnterHeroSubHorizontal method. Calls an event multiple times within the method.
    /// </summary>
    private static void HeroControllerOnEnterHeroSubHorizontal(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            c.GotoNext(
                MoveType.After,
                HeroInPositionInstructions
            );
            
            // IL_0634: ldloc.1      // V_1
            // IL_0635: callvirt     instance void HeroController::FaceRight()
            Func<Instruction, bool>[] faceDirectionInstructions = [
                i => i.MatchLdloc(1),
                i => 
                    i.MatchCall(typeof(HeroController), "FaceRight") || 
                    i.MatchCall(typeof(HeroController), "FaceLeft")
            ];

            for (var i = 0; i < 2; i++) {
                c.GotoNext(
                    MoveType.After,
                    faceDirectionInstructions
                );
                
                c.EmitDelegate(() => { AfterEnterSceneHeroTransformed?.Invoke(); });
            }
        } catch (Exception e) {
            Logger.Error($"Could not change HeroController#EnterHeroSubHorizontal IL: \n{e}");
        }
    }

    /// <summary>
    /// IL Hook for the HeroController Respawn method. Calls an event multiple times within the method.
    /// </summary>
    private static void HeroControllerOnRespawn(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            for (var i = 0; i < 2; i++) {
                EmitAfterEnterSceneEventHeroInPosition(c);
            }
        } catch (Exception e) {
            Logger.Error($"Could not change HeroController#Respawn IL: \n{e}");
        }
    }

    /// <summary>
    /// Emit the delegate for calling the <see cref="AfterEnterSceneHeroTransformed"/> event after the
    /// 'HeroInPosition' instructions.
    /// </summary>
    /// <param name="c">The IL cursor on which to match the instructions and emit the delegate.</param>
    private static void EmitAfterEnterSceneEventHeroInPosition(ILCursor c) {
        c.GotoNext(
            MoveType.After,
            HeroInPositionInstructions
        );

        c.EmitDelegate(() => { AfterEnterSceneHeroTransformed?.Invoke(); });
    }
    
    /// <summary>
    /// IL Hook for the ApplyMusicCue OnEnter method. Calls an event in the method after the ApplyMusicCue call is
    /// made.
    /// </summary>
    // ReSharper disable once UnusedMember.Local
    private static void ApplyMusicCueOnEnter(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            // IL_005d: ldc.i4.0
            // IL_005e: callvirt     instance void AudioManager::ApplyMusicCue(class MusicCue, float32, float32, bool)
            c.GotoNext(
                MoveType.After,
                i => i.MatchLdcI4(0),
                i => i.MatchCallvirt(typeof(AudioManager), "ApplyMusicCue")
            );

            // Put the instance of the ApplyMusicCue class onto the stack
            c.Emit(OpCodes.Ldarg_0);

            // Emit a delegate for firing the event with the ApplyMusicCue instance
            c.EmitDelegate<Action<ApplyMusicCue>>(action => { ApplyMusicCueFromFsmAction?.Invoke(action); });
        } catch (Exception e) {
            Logger.Error($"Could not change ApplyMusicCueOnEnter IL: \n{e}");
        }
    }
    
    /// <summary>
    /// IL Hook for the TransitionToAudioSnapshot OnEnter method. Calls an event in the method after the TransitionTo
    /// call is made.
    /// </summary>
    // ReSharper disable once UnusedMember.Local
    private static void TransitionToAudioSnapshotOnEnter(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            // IL_0021: callvirt     instance float32 [PlayMaker]HutongGames.PlayMaker.FsmFloat::get_Value()
            // IL_0026: callvirt     instance void [UnityEngine.AudioModule]UnityEngine.Audio.AudioMixerSnapshot::TransitionTo(float32)
            c.GotoNext(
                MoveType.After,
                i => i.MatchCallvirt(typeof(FsmFloat), "get_Value"),
                i => i.MatchCallvirt(typeof(AudioMixerSnapshot), "TransitionTo")
            );

            // Put the instance of the TransitionToAudioSnapshot class onto the stack
            c.Emit(OpCodes.Ldarg_0);

            // Emit a delegate for firing the event with the TransitionToAudioSnapshot instance
            c.EmitDelegate<Action<TransitionToAudioSnapshot>>(action => { TransitionToAudioSnapshotFromFsmAction?.Invoke(action); });
        } catch (Exception e) {
            Logger.Error($"Could not change TransitionToAudioSnapshotOnEnter IL: \n{e}");
        }
    }

    /// <summary>
    /// On hook for when the HeroController starts, so we can invoke our custom event.
    /// </summary>
    private static void HeroControllerOnStart(Action<HeroController> orig, HeroController self) {
        orig(self);

        HeroControllerStartActionInternal?.Invoke();
    }
}

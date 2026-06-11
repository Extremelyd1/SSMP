using System;
using System.Reflection;
using HutongGames.PlayMaker.Actions;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using SSMP.Hooks;
using SSMP.Networking.Client;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Game.Client;

/// <summary>
/// Registers and owns standalone multiplayer patches for player-only interactions, enemy target actions,
/// camera locks, damage feedback, and transition filtering.
/// </summary>
internal partial class GamePatcher {
    /// <summary>
    /// Binding flags used to access private instance members through reflection.
    /// </summary>
    private const BindingFlags InstanceNonPublicFlags = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// Binding flags used to access public instance members through reflection.
    /// </summary>
    private const BindingFlags InstancePublicFlags = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>
    /// Binding flags used to access private, public, and static members through reflection.
    /// </summary>
    private const BindingFlags StaticNonPublicPublicFlags =
        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    /// <summary>
    /// Hook for retargeting <see cref="ChaseObject"/> movement before the original buzz chase logic runs.
    /// </summary>
    private Hook? _chaseObjectDoBuzzHook;

    /// <summary>
    /// Hook for retargeting <see cref="ChaseObjectV2"/> movement before the original chase logic runs.
    /// </summary>
    private Hook? _chaseObjectV2DoChaseHook;

    /// <summary>
    /// IL hook for suppressing local-only tink feedback and correcting remote-hit tink effects.
    /// </summary>
    private ILHook? _tinkEffectOnTriggerEnter2DHook;

    /// <summary>
    /// IL hook for suppressing local-only invincibility feedback caused by remote player hits.
    /// </summary>
    private ILHook? _healthManagerInvincibleHook;

    /// <summary>
    /// IL hook for preventing remote player hits from granting local-only rewards or side effects.
    /// </summary>
    private ILHook? _healthManagerTakeDamageHook;

    /// <summary>
    /// Hook for filtering local-only PlayMaker method calls such as hero recoil and bounce feedback.
    /// </summary>
    private Hook? _callMethodProperDoMethodCallHook;

    /// <summary>
    /// Hook for allowing camera lock checks to remain valid while connected and paused.
    /// </summary>
    private Hook? _cameraLockAreaIsInApplicableGameStateHook;

    /// <summary>
    /// Hook for guarding <see cref="Crawler.StopCrawling"/> against destruction-time null reference errors.
    /// </summary>
    private Hook? _crawlerStopCrawlingHook;

    /// <summary>
    /// The NetClient instance to check if we are connected to a server.
    /// </summary>
    private readonly NetClient _netClient;

    public GamePatcher(NetClient netClient) {
        _netClient = netClient;
    }

    /// <summary>
    /// Registers all gameplay hooks owned by this patcher.
    /// </summary>
    public void RegisterHooks() {
        _tinkEffectOnTriggerEnter2DHook = new ILHook(
            typeof(TinkEffect).GetMethod(
                "TryDoTinkReactionNoDamager",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                [
                    typeof(GameObject), typeof(bool), typeof(bool), typeof(bool), typeof(Vector2).MakeByRefType()
                ],
                null
            )!,
            TinkEffectOnTriggerEnter2D
        );

        _healthManagerInvincibleHook = new ILHook(
            typeof(HealthManager).GetMethod(
                "Invincible", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            )!,
            HealthManagerOnInvincible
        );

        _healthManagerTakeDamageHook = new ILHook(
            typeof(HealthManager).GetMethod(
                "TakeDamage", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            )!,
            HealthManagerOnTakeDamage
        );

        _callMethodProperDoMethodCallHook = new Hook(
            typeof(CallMethodProper).GetMethod(
                "DoMethodCall", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            )!,
            CallMethodProperOnDoMethodCall
        );

        _cameraLockAreaIsInApplicableGameStateHook = new Hook(
            typeof(CameraLockArea).GetMethod(
                "IsInApplicableGameState", StaticNonPublicPublicFlags
            )!,
            CameraLockAreaOnIsInApplicableGameState
        );

        EventHooks.InteractableBaseAddInsideIL += ILInteractableBaseAddInside;
        EventHooks.InteractableBaseLocalAddInsideIL += ILInteractableBaseAddInside;

        EventHooks.TransitionPointOnTriggerEnter2DIL += ILTransitionPointOnTrigger2D;
        EventHooks.TransitionPointOnTriggerStay2DIL += ILTransitionPointOnTrigger2D;

        EventHooks.CameraLockAreaAwake += OnCameraLockAreaAwake;

        _chaseObjectDoBuzzHook = new Hook(
            typeof(ChaseObject).GetMethod("DoBuzz", InstanceNonPublicFlags)!,
            OnChaseObjectDoBuzz
        );

        _chaseObjectV2DoChaseHook = new Hook(
            typeof(ChaseObjectV2).GetMethod("DoChase", InstanceNonPublicFlags)!,
            OnChaseObjectV2DoChase
        );

        RegisterDetectionHooks();

        _crawlerStopCrawlingHook = new Hook(
            typeof(Crawler).GetMethod("StopCrawling", InstancePublicFlags | InstanceNonPublicFlags)!,
            OnCrawlerStopCrawling
        );

        RegisterAggressionHooks();
    }

    /// <summary>
    /// Deregisters and disposes all gameplay hooks owned by this patcher.
    /// </summary>
    public void DeregisterHooks() {
        _tinkEffectOnTriggerEnter2DHook?.Dispose();
        _tinkEffectOnTriggerEnter2DHook = null;

        _healthManagerInvincibleHook?.Dispose();
        _healthManagerInvincibleHook = null;

        _healthManagerTakeDamageHook?.Dispose();
        _healthManagerTakeDamageHook = null;

        _callMethodProperDoMethodCallHook?.Dispose();
        _callMethodProperDoMethodCallHook = null;

        _cameraLockAreaIsInApplicableGameStateHook?.Dispose();
        _cameraLockAreaIsInApplicableGameStateHook = null;

        EventHooks.InteractableBaseAddInsideIL -= ILInteractableBaseAddInside;
        EventHooks.InteractableBaseLocalAddInsideIL -= ILInteractableBaseAddInside;

        EventHooks.TransitionPointOnTriggerEnter2DIL -= ILTransitionPointOnTrigger2D;
        EventHooks.TransitionPointOnTriggerStay2DIL -= ILTransitionPointOnTrigger2D;

        EventHooks.CameraLockAreaAwake -= OnCameraLockAreaAwake;

        _chaseObjectDoBuzzHook?.Dispose();
        _chaseObjectDoBuzzHook = null;

        _chaseObjectV2DoChaseHook?.Dispose();
        _chaseObjectV2DoChaseHook = null;

        DisposeDetectionHooks();
        DisposeAggressionHooks();

        _crawlerStopCrawlingHook?.Dispose();
        _crawlerStopCrawlingHook = null;
    }

    /// <summary>
    /// Forces <see cref="ChaseObject"/> to consume the enemy-approved multiplayer target before running chase logic.
    /// </summary>
    /// <param name="orig">The original chase method.</param>
    /// <param name="self">The chase action instance.</param>
    private static void OnChaseObjectDoBuzz(Action<ChaseObject> orig, ChaseObject self) {
        ForceApprovedTargetOnFsmAction(self);

        if (self.target?.Value == null) {
            return;
        }

        orig(self);
    }

    /// <summary>
    /// Forces <see cref="ChaseObjectV2"/> to consume the enemy-approved multiplayer target before running chase logic.
    /// </summary>
    /// <param name="orig">The original chase method.</param>
    /// <param name="self">The chase action instance.</param>
    private static void OnChaseObjectV2DoChase(Action<ChaseObjectV2> orig, ChaseObjectV2 self) {
        ForceApprovedTargetOnFsmAction(self);

        if (self.target?.Value == null) {
            return;
        }

        orig(self);
    }

    /// <summary>
    /// Guards <see cref="Crawler.StopCrawling"/> against NullReferenceExceptions during destruction/disable.
    /// </summary>
    private static void OnCrawlerStopCrawling(Action<Crawler> orig, Crawler self) {
        try {
            orig(self);
        } catch (NullReferenceException) {
            Logger.Debug("Safely caught NullReferenceException in Crawler.StopCrawling");
        }
    }

    /// <summary>
    /// Resolves the top-level transform for a hit source.
    /// </summary>
    /// <param name="source">The source object stored on a hit or attack object.</param>
    /// <returns>The highest reachable transform, or null when the source is unavailable.</returns>
    private static Transform? GetHitSourceRoot(GameObject? source) {
        var transform = source?.transform;
        if (transform == null) {
            return null;
        }

        while (transform.parent != null) {
            transform = transform.parent;
        }

        return transform;
    }

    /// <summary>
    /// Determines whether a hit source belongs to the local player.
    /// </summary>
    /// <param name="source">The source object stored on a hit or attack object.</param>
    /// <returns>True when the source is local or unknown; otherwise false.</returns>
    private static bool IsLocalHitSource(GameObject? source) {
        var root = GetHitSourceRoot(source);
        if (root == null) {
            return true;
        }

        var rootObject = root.gameObject;
        return rootObject.CompareTag("Player") || rootObject.name == "Knight";
    }

    /// <summary>
    /// Determines whether the provided hit instance belongs to the local player.
    /// </summary>
    /// <param name="hitInstance">The hit instance to inspect.</param>
    /// <returns>True when the hit is local or unknown; otherwise false.</returns>
    private static bool IsLocalHitSource(HitInstance hitInstance) {
        return IsLocalHitSource(hitInstance.Source);
    }

    /// <summary>
    /// Gets the best available world position for a hit source.
    /// </summary>
    /// <param name="source">The source object stored on a hit or attack object.</param>
    /// <returns>The root transform position when available, otherwise the source position or zero.</returns>
    private static Vector3 GetHitSourcePosition(GameObject? source) {
        var root = GetHitSourceRoot(source);
        if (root != null) {
            return root.position;
        }

        return source != null ? source.transform.position : Vector3.zero;
    }

    /// <summary>
    /// IL hook to change the behaviour of the <see cref="InteractableBase"/> to add a check for whether it is dealing
    /// with the local player.
    /// </summary>
    private void ILInteractableBaseAddInside(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            var beforeFirstReturnLabel = c.DefineLabel();

            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdarg(1),
                i => i.MatchCallvirt(typeof(Component), "get_gameObject"),
                i => i.MatchCallvirt(typeof(GameObject), "get_layer"),
                i => i.MatchLdcI4(9),
                i => i.MatchBeq(out _)
            );

            c.Emit(OpCodes.Ldarg_1);
            // Emit a delegate that pops the collider argument off the stack and pushes a boolean onto the stack
            // that indicates whether the collider's game object has the tag "Player"
            c.EmitDelegate<Func<Collider2D, bool>>(col => col.gameObject.tag == "Player");

            // Branch if the tag is not "Player" to the pre-defined label, which is before the return
            // In other words, we return if it is not the local player
            c.Emit(OpCodes.Brfalse, beforeFirstReturnLabel);

            // Goto before the next return to mark our label there, so we can branch to it
            c.GotoNext(
                MoveType.Before,
                i => i.MatchRet()
            );

            c.MarkLabel(beforeFirstReturnLabel);
        } catch (Exception e) {
            Logger.Error($"Could not change InteractableBase#AddInside IL:\n{e}");
        }
    }

    /// <summary>
    /// IL hook to change the behaviour of the <see cref="TransitionPoint"/> to add a check for whether it is dealing
    /// with the local player.
    /// </summary>
    private void ILTransitionPointOnTrigger2D(ILContext il) {
        try {
            // Create a cursor for this context
            var c = new ILCursor(il);

            ILLabel? returnLabel = null;

            c.GotoNext(
                MoveType.After,
                i => i.MatchLdarg(1),
                i => i.MatchCallvirt(typeof(Component), "get_gameObject"),
                i => i.MatchCallvirt(typeof(GameObject), "get_layer"),
                i => i.MatchLdcI4(9),
                i => i.MatchBneUn(out returnLabel)
            );

            if (returnLabel == null) {
                Logger.Error($"Could not change TransitionPoint#OnTrigger{{Enter,Stay}}2D IL:\nCould not find label");
                return;
            }

            c.Emit(OpCodes.Ldarg_1);
            // Emit a delegate that pops the collider argument off the stack and pushes a boolean onto the stack
            // that indicates whether the collider's game object has the tag "Player"
            c.EmitDelegate<Func<Collider2D, bool>>(movingObj => movingObj.gameObject.tag == "Player");

            // Branch if the tag is not "Player" to the pre-defined label, which is before the return
            // In other words, we return if it is not the local player
            c.Emit(OpCodes.Brfalse, returnLabel);
        } catch (Exception e) {
            Logger.Error($"Could not change TransitionPoint#OnTrigger{{Enter,Stay}}2D IL:\n{e}");
        }
    }

    /// <summary>
    /// IL hook to change the tink reaction flow so remote player hits do not trigger local-only feedback.
    /// </summary>
    private void TinkEffectOnTriggerEnter2D(ILContext il) {
        try {
            var c = new ILCursor(il);
            c.Emit(OpCodes.Ldarg_1);

            var isLocalPlayer = true;
            c.EmitDelegate<Action<GameObject>>(source => { isLocalPlayer = IsLocalHitSource(source); });

            // Replace the local-hero position with the remote player's root position for spawned tink effects.
            c.GotoNext(
                MoveType.After,
                i => i.MatchStloc(6)
            );

            var afterRemotePositionLabel = c.DefineLabel();
            c.EmitDelegate(() => isLocalPlayer);
            c.Emit(OpCodes.Brtrue, afterRemotePositionLabel);
            c.Emit(OpCodes.Ldarg_1);
            c.EmitDelegate<Func<GameObject, Vector3>>(GetHitSourcePosition);
            c.Emit(OpCodes.Stloc, 6);
            c.MarkLabel(afterRemotePositionLabel);

            // Skip the camera shake block for remote hits.
            c.GotoNext(
                MoveType.After,
                i => i.MatchLdarg(2),
                i => i.MatchLdloc(1),
                i => i.MatchAnd(),
                i => i.MatchBrfalse(out _)
            );

            var afterCameraShakeLabel = c.DefineLabel();
            c.EmitDelegate(() => isLocalPlayer);
            c.Emit(OpCodes.Brfalse, afterCameraShakeLabel);

            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdloca(13)
            );

            c.MarkLabel(afterCameraShakeLabel);

            // Skip local recoil feedback for remote hits.
            foreach (var recoilMethod in new[] { "RecoilLeft", "RecoilDown", "RecoilRight" }) {
                c.GotoNext(
                    MoveType.Before,
                    inst => inst.MatchLdloc(4),
                    inst => inst.MatchCallvirt(typeof(HeroController), recoilMethod)
                );

                var afterRecoilLabel = c.DefineLabel();
                c.EmitDelegate(() => isLocalPlayer);
                c.Emit(OpCodes.Brfalse, afterRecoilLabel);
                c.GotoNext(
                    MoveType.After,
                    inst => inst.MatchLdloc(4),
                    inst => inst.MatchCallvirt(typeof(HeroController), recoilMethod)
                );
                c.MarkLabel(afterRecoilLabel);
            }

            // Skip the optional FSM event for remote hits.
            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdarg(0),
                i => i.MatchLdfld(typeof(TinkEffect), "sendFSMEvent"),
                i => i.MatchBrfalse(out _),
                i => i.MatchLdarg(0),
                i => i.MatchLdfld(typeof(TinkEffect), "fsm"),
                i => i.MatchCall(typeof(UnityEngine.Object), "op_Implicit"),
                i => i.MatchBrfalse(out _)
            );

            var afterSendEventLabel = c.DefineLabel();
            c.EmitDelegate(() => isLocalPlayer);
            c.Emit(OpCodes.Brfalse, afterSendEventLabel);
            c.GotoNext(
                MoveType.After,
                i => i.MatchLdarg(0),
                i => i.MatchLdfld(typeof(TinkEffect), "fsm"),
                i => i.MatchLdarg(0),
                i => i.MatchLdfld(typeof(TinkEffect), "FSMEvent"),
                i => i.MatchCallvirt(typeof(PlayMakerFSM), "SendEvent")
            );
            c.MarkLabel(afterSendEventLabel);
        } catch (Exception e) {
            Logger.Error($"Could not change TinkEffect#OnTriggerEnter2D IL:\n{e}");
        }
    }

    private void HealthManagerOnInvincible(ILContext il) {
        try {
            var c = new ILCursor(il);
            c.Emit(OpCodes.Ldarg_1);

            var isLocalPlayer = true;
            c.EmitDelegate<Action<HitInstance>>(hitInstance => { isLocalPlayer = IsLocalHitSource(hitInstance); });

            // Skip the local recoil and blocked-hit shake for remote hits.
            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdarg(0),
                i => i.MatchLdfld(typeof(HealthManager), "preventInvincibleShake"),
                i => i.MatchBrtrue(out _)
            );

            var afterLocalFeedbackLabel = c.DefineLabel();
            c.EmitDelegate(() => isLocalPlayer);
            c.Emit(OpCodes.Brfalse, afterLocalFeedbackLabel);

            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdarg(0),
                i => i.MatchLdfld(typeof(HealthManager), "boxCollider"),
                i => i.MatchStloc(6)
            );

            c.MarkLabel(afterLocalFeedbackLabel);
        } catch (Exception e) {
            Logger.Error($"Could not change HealthManager#OnInvincible IL:\n{e}");
        }
    }

    /// <summary>
    /// IL Hook to modify the behaviour of the TakeDamage method in HealthManager. This modification adds a
    /// conditional branch in case the nail swing from the HitInstance was from a remote player to ensure that
    /// soul is not gained for remote hits.
    /// </summary>
    private void HealthManagerOnTakeDamage(ILContext il) {
        try {
            var c = new ILCursor(il);

            // Remote hits must not grant the local player silk or reaper payouts.
            ILLabel? targetLabel = null;
            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdarg(0),
                i => i.MatchLdfld(typeof(HealthManager), "enemyType"),
                i => i.MatchLdcI4(3),
                i => i.MatchBeq(out targetLabel)
            );

            c.Emit(OpCodes.Ldarg_1);
            c.EmitDelegate<Func<HitInstance, bool>>(hitInstance => !IsLocalHitSource(hitInstance));
            c.Emit(OpCodes.Brtrue, targetLabel);
        } catch (Exception e) {
            Logger.Error($"Could not change HealthManager#TakeDamage IL:\n{e}");
        }
    }

    /// <summary>
    /// Hook for the 'DoMethodCall' method in the 'CallMethodProper' FSM action. This is used for the Crystal Shot
    /// game object to ensure that knockback is not applied to the local player if a remote player hits the crystal.
    /// </summary>
    private void CallMethodProperOnDoMethodCall(
        Action<CallMethodProper> orig,
        CallMethodProper self
    ) {
        // If the 'behaviour' and 'methodName' strings do not match, we execute the original method and return
        if (self.behaviour.Value != "HeroController" ||
            self.methodName.Value != "RecoilLeft" &&
            self.methodName.Value != "RecoilRight" &&
            self.methodName.Value != "RecoilDown" &&
            self.methodName.Value != "Bounce"
           ) {
            orig(self);
            return;
        }

        // If the state does not match, we return as well
        if (!self.State.Name.StartsWith("No Box ") && !self.State.Name.StartsWith("Blocked ")) {
            orig(self);
            return;
        }

        Transform attacks;

        // Find either the 'Damager' or the 'Collider' game object from the FSM variables, and check up the hierarchy.
        // If it matches the local player's object, then we know it was the local player's slash and not a remote
        // player's slash
        var damager = self.Fsm.Variables.FindFsmGameObject("Damager");
        var collider = self.Fsm.Variables.FindFsmGameObject("Collider");
        if (damager != null && damager.Value != null) {
            attacks = damager.Value.transform.parent;
        } else if (collider != null && collider.Value != null && collider.Value.transform.parent != null) {
            attacks = collider.Value.transform.parent.parent;
        } else {
            orig(self);
            return;
        }

        if (attacks == null) {
            orig(self);
            return;
        }

        var knight = attacks.parent;
        if (knight == null) {
            orig(self);
            return;
        }

        if (knight.name.Equals("Knight")) {
            orig(self);
        }
    }

    /// <summary>
    /// Hook for the 'IsInApplicableGameState' method in 'CameraLockArea'. This is used to add a check
    /// for being in the pause menu and connected to a server. Otherwise, the camera will sometimes not lock while
    /// in the pause menu during host transfers.
    /// </summary>
    private bool CameraLockAreaOnIsInApplicableGameState(Func<bool> orig) {
        try {
            if (_netClient.IsConnected && PauseManager.IsMultiplayerPauseMenuOpen) {
                return true;
            }

            return orig();
        } catch (Exception e) {
            Logger.Error($"Could not change CameraLockArea#IsInApplicableGameState hook: \n{e}");
            return orig();
        }
    }

    /// <summary>
    /// Hook to add a tag include to the <see cref="TrackTriggerObjects"/> of <see cref="CameraLockArea"/> to ensure
    /// that it only triggers on the local player.
    /// </summary>
    private void OnCameraLockAreaAwake(CameraLockArea cameraLockArea) {
        cameraLockArea.tagIncludeList ??= [];
        cameraLockArea.tagIncludeList.Add("Player");
    }
}

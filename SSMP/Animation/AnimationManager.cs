using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SSMP.Animation.Effects;
using SSMP.Collection;
using SSMP.Game;
using SSMP.Game.Client;
using SSMP.Game.Settings;
using SSMP.Hooks;
using SSMP.Networking.Client;
using SSMP.Networking.Packet.Data;
using SSMP.Util;
using UnityEngine.SceneManagement;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Animation;

/// <summary>
/// Class that manages all forms of animation from clients.
/// </summary>
internal class AnimationManager {
    /// <summary>
    /// Whether to log debug messages about animations. For debugging purposes this can be enabled so that all
    /// animation events are logged.
    /// </summary>
    private static bool _debugLogAnimations = false;
    
    /// <summary>
    /// The distance threshold for playing certain effects.
    /// </summary>
    public const float EffectDistanceThreshold = 25f;

    /// <summary>
    /// Animations that are allowed to loop, because they need to transmit the effect.
    /// </summary>
    private static readonly string[] AllowedLoopAnimations = [
        "Airborne", 
        "Wall Slide"
    ];

    /// <summary>
    /// Clip names of animations that are handled by the animation controller.
    /// </summary>
    private static readonly string[] AnimationControllerClipNames = [
        // "Airborne"
    ];

    /// <summary>
    /// Animation effect for various dash slashes, which is used for multiple animation clips.
    /// </summary>
    private static readonly AnimationEffect DashSlashAntic = new DashSlashAntic();

    /// <summary>
    /// Bi-directional lookup table for linking animation clip names with their respective animation clip enum
    /// values.
    /// </summary>
    private static readonly BiLookup<string, AnimationClip> ClipEnumNames = new() {
        { "Wake To Sit", AnimationClip.WakeToSit },
        { "Sit Idle", AnimationClip.SitIdle },
        { "SitLook 1", AnimationClip.SitLook1 },
        { "SitLook 2", AnimationClip.SitLook2 },
        { "SitLook 3", AnimationClip.SitLook3 },
        { "SitLook 4", AnimationClip.SitLook4 },
        { "Get Off", AnimationClip.GetOff },
        { "Land", AnimationClip.Land },
        { "Run", AnimationClip.Run },
        { "RunQ", AnimationClip.RunQ },
        { "Run To Idle", AnimationClip.RunToIdle },
        { "Idle", AnimationClip.Idle },
        { "Idle To Run", AnimationClip.IdleToRun },
        { "Turn", AnimationClip.Turn },
        { "Dash", AnimationClip.Dash },
        { "Sprint Lv2", AnimationClip.SprintLv2 },
        { "Mantle Cling", AnimationClip.MantleCling },
        { "Mantle Land", AnimationClip.MantleLand },
        { "Mantle Land To Idle", AnimationClip.MantleLandToIdle },
        { "Mantle Vault", AnimationClip.MantleVault },
        { "Sprint Air Short", AnimationClip.SprintAirShort },
        { "Sprint Turn", AnimationClip.SprintTurn },
        { "Shuttlecock Antic", AnimationClip.ShuttlecockAntic },
        { "Shuttlecock", AnimationClip.Shuttlecock },
        { "Sprint Air", AnimationClip.SprintAir },
        { "Sprint Air Loop", AnimationClip.SprintAirLoop },
        { "Double Jump", AnimationClip.DoubleJump },
        { "Land To Run", AnimationClip.LandToRun },
        { "Dash Attack Antic", AnimationClip.DashAttackAntic },
        { "Dash Attack", AnimationClip.DashAttack },
        { "Dash Attack Recover", AnimationClip.DashAttackRecover },
        { "Skid End 1", AnimationClip.SkidEnd1 },
        { "Skid End 2", AnimationClip.SkidEnd2 },
        { "Dash To Idle", AnimationClip.DashToIdle },
        { "Dash To Run", AnimationClip.DashToRun },
        { "Slash", AnimationClip.Slash },
        { "SlashAlt", AnimationClip.SlashAlt },
        { "Slash To Run", AnimationClip.SlashToRun },
        { "Air Dash", AnimationClip.AirDash },
        { "Slash Land Run Alt", AnimationClip.SlashLandRunAlt },
        { "DownSpike Antic", AnimationClip.DownSpikeAntic },
        { "DownSpike", AnimationClip.DownSpike },
        { "Downspike Recovery", AnimationClip.DownSpikeRecovery },
        { "Downspike Recovery Land", AnimationClip.DownSpikeRecoveryLand },
        { "Downspike Followup", AnimationClip.DownSpikeFollowup },
        { "UpSlash", AnimationClip.UpSlash },
        { "Slash Land", AnimationClip.SlashLand },
        { "Slash_Charged", AnimationClip.SlashCharged },
        { "Umbrella Inflate Antic", AnimationClip.UmbrellaInflateAntic },
        { "Walljump", AnimationClip.WallJump },
        { "Wall Slide", AnimationClip.WallSlide },
        { "Wall Cling", AnimationClip.WallCling },
        { "Sit Fall Asleep", AnimationClip.SitFallAsleep },
        { "Umbrella Inflate", AnimationClip.UmbrellaInflate },
        { "Umbrella Float", AnimationClip.UmbrellaFloat },
        { "Umbrella Deflate", AnimationClip.UmbrellaDeflate },
        { "Idle Rest", AnimationClip.IdleRest },
        { "Mantle Land To Run", AnimationClip.MantleLandToRun },
        { "Sit", AnimationClip.Sit },
        { "Sprint", AnimationClip.Sprint },
        { "Sprint Backflip", AnimationClip.SprintBackflip },
        { "AirSphere Antic", AnimationClip.AirSphereAntic },
        { "Silk Boss Needle Cast", AnimationClip.SilkBossNeedleCast },
        { "Taunt", AnimationClip.Taunt },
        { "Needolin Start", AnimationClip.NeedolinStart },
        { "Needolin StartCancelable", AnimationClip.NeedolinStartCancelable },
        { "Needolin End", AnimationClip.NeedolinEnd },
        { "Needolin Play", AnimationClip.NeedolinPlay },
        { "NeedolinSit Start", AnimationClip.NeedolinSitStart },
        { "NeedolinSit Play", AnimationClip.NeedolinSitPlay },
        { "NeedolinSit End", AnimationClip.NeedolinSitEnd },
        { "Harpoon Antic", AnimationClip.HarpoonAntic },
        { "Harpoon Throw", AnimationClip.HarpoonThrow },
        { "Harpoon Catch", AnimationClip.HarpoonCatch },
        { "Silk Charge End", AnimationClip.SilkChargeEnd },
        { "Map Open", AnimationClip.MapOpen },
        { "Map Idle", AnimationClip.MapIdle },
        { "Map Away", AnimationClip.MapAway },
        { "Sit Map Open", AnimationClip.SitMapOpen },
        { "Sit Map Close", AnimationClip.SitMapClose },
        { "BindCharge Ground", AnimationClip.BindChargeGround },
        { "BindCharge Air", AnimationClip.BindChargeAir },
        { "BindBurst Air", AnimationClip.BindBurstAir },
        { "BindBurst Ground", AnimationClip.BindBurstGround },
        { "LookDown", AnimationClip.LookDown },
        { "LookDownEnd", AnimationClip.LookDownEnd },
        { "Needolin Play Low Transition", AnimationClip.NeedolinPlayLowTransition },
        { "Needolin Play Low", AnimationClip.NeedolinPlayLow },
        { "Needolin Play High Transition", AnimationClip.NeedolinPlayHighTransition },
        { "Needolin Play High", AnimationClip.NeedolinPlayHigh },
        { "Super Jump Antic", AnimationClip.SuperJumpAntic },
        { "Super Jump Throw", AnimationClip.SuperJumpThrow },
        { "Super Jump Throw Wait", AnimationClip.SuperJumpThrowWait },
        { "Super Jump Jump Antic", AnimationClip.SuperJumpJumpAntic },
        { "Super Jump Loop", AnimationClip.SuperJumpLoop },
        { "Super Jump Hit Roof", AnimationClip.SuperJumpHitRoof },
        { "Super Jump Fall", AnimationClip.SuperJumpFall },
        { "LookUp", AnimationClip.LookUp },
        { "LookUpEnd", AnimationClip.LookUpEnd },
        { "Surface In", AnimationClip.SurfaceIn },
        { "Surface InToIdle", AnimationClip.SurfaceInToIdle },
        { "Surface Idle", AnimationClip.SurfaceIdle },
        { "Surface IdleToSwim", AnimationClip.SurfaceIdleToSwim },
        { "Surface TurnToSwim", AnimationClip.SurfaceTurnToSwim },
        { "Swim Dash", AnimationClip.SwimDash },
        { "Swim Dash Turn", AnimationClip.SwimDashTurn },
        { "Swim Dash Bonk", AnimationClip.SwimDashBonk },
        { "Mantle Cancel To Jump", AnimationClip.MantleCancelToJump },
        { "Mantle Cancel To Jump Backwards", AnimationClip.MantleCancelToJumpBackwards },
        { "Wall Scramble Antic", AnimationClip.WallScrambleAntic },
        { "Wall Scramble", AnimationClip.WallScramble },
        { "Wall Scramble End", AnimationClip.WallScrambleEnd },
        { "Wall Slash", AnimationClip.WallSlash },
        { "Walljump Somersault", AnimationClip.WallJumpSomersault },
        { "Airborne", AnimationClip.Airborne },
        { "Sprint Bonk", AnimationClip.SprintBonk },
        { "Slash Land Run", AnimationClip.SlashLandRun },
        { "v3 Down Slash Antic", AnimationClip.V3DownSlashAntic },
        { "v3 Down Slash", AnimationClip.V3DownSlash },
        { "Dash Upper Antic", AnimationClip.DashUpperAntic },
        { "Dash Upper", AnimationClip.DashUpper },
        { "Dash Upper Recovery", AnimationClip.DashUpperRecovery },
        { "Wanderer Dash Attack", AnimationClip.WandererDashAttack },
        { "Wanderer Dash Attack Alt", AnimationClip.WandererDashAttackAlt },
        { "DownSlash", AnimationClip.DownSlash },
        { "DownSlashAlt", AnimationClip.DownSlashAlt },
        { "SpinBall Antic", AnimationClip.SpinBallAntic },
        { "SpinBall Launch", AnimationClip.SpinBallLaunch },
        { "SpinBall", AnimationClip.SpinBall },
        { "NeedleArt Dash", AnimationClip.NeedleArtDash },
        
        { "Rage Bind", AnimationClip.RageBind },
        { "Rage Bind Quick", AnimationClip.RageBindQuick },
        { "Rage Bind Long", AnimationClip.RageBindLong },
        { "Rage Bind Grounded", AnimationClip.RageBindGrounded },
        { "Rage Bind Quick Grounded", AnimationClip.RageBindQuickGrounded },
        { "Rage Bind Long Grounded", AnimationClip.RageBindLongGrounded },
        { "Rage Bind Burst", AnimationClip.RageBindBurst },
        { "Rage Bind Burst Quick", AnimationClip.RageBindBurstQuick },
        { "Rage Bind Burst Long", AnimationClip.RageBindBurstLong },
        { "Land Q", AnimationClip.LandQ },
        { "Rage Idle", AnimationClip.RageIdle },
        { "Rage Idle End", AnimationClip.RageIdleEnd },
        { "Dash Attack Antic 1", AnimationClip.DashAttackAntic1 },
        { "Dash Attack 1", AnimationClip.DashAttack1 },
        { "Dash Attack Antic 2", AnimationClip.DashAttackAntic2 },
        { "Dash Attack 2", AnimationClip.DashAttack2 },
        
        { "BindCharge Witch", AnimationClip.BindChargeWitch },
        { "BindCharge Witch Quick", AnimationClip.BindChargeWitchQuick },
        { "BindCharge Witch Long", AnimationClip.BindChargeWitchLong },
        
        { "DownSpike Charge", AnimationClip.DownSpikeCharge },
        { "Dash Attack Charge", AnimationClip.DashAttackCharge },
        { "Quick Craft Ground", AnimationClip.QuickCraftGround },
        { "Quick Craft Air", AnimationClip.QuickCraftAir },
        { "Dash Attack Leap", AnimationClip.DashAttackLeap },
        { "Dash Attack Slash", AnimationClip.DashAttackSlash },
        { "BindCharge Ground Land", AnimationClip.BindChargeGroundLand },
        { "BindCharge Heal Burst", AnimationClip.BindChargeHealBurst },
        { "BindCharge End", AnimationClip.BindChargeEnd },
        
        { "BindCharge Twirl Air", AnimationClip.BindChargeTwirlAir },
        { "BindCharge Twirl Ground", AnimationClip.BindChargeTwirlGround },
        
        { "NeedleThrow AnticG", AnimationClip.NeedleThrowAnticG },
        { "NeedleThrow AnticA", AnimationClip.NeedleThrowAnticA },
        { "NeedleThrow Throwing", AnimationClip.NeedleThrowThrowing },
        { "NeedleThrow Catch", AnimationClip.NeedleThrowCatch },
        
        { "Parry Stance", AnimationClip.ParryStance },
        { "Parry Stance Ground", AnimationClip.ParryStanceGround },
        { "Parry Recover", AnimationClip.ParryRecover },
        { "Parry Recover Ground", AnimationClip.ParryRecoverGround },
        
        { "AirSphere Attack", AnimationClip.AirSphereAttack },
        { "AirSphere End", AnimationClip.AirSphereEnd },
        
        { "Silk Charge Antic", AnimationClip.SilkChargeAntic },
        { "Silk Charge", AnimationClip.SilkCharge },
        { "Silk Charge Recover", AnimationClip.SilkChargeRecover },
        
        { "Silk Bomb Antic", AnimationClip.SilkBombAntic },
        { "Silk Bomb Loop", AnimationClip.SilkBombLoop },
        { "Silk Bomb Recover", AnimationClip.SilkBombRecover },
        
        { "Sit Craft", AnimationClip.SitCraft },
        { "ToolThrow Up", AnimationClip.ToolThrowUp },
        { "ToolThrow Q", AnimationClip.ToolThrowQ },
        { "ToolThrowAlt Q", AnimationClip.ToolThrowAltQ },
        
        { "Recoil Twirl", AnimationClip.RecoilTwirl },
        { "DownSpikeBounce 1", AnimationClip.DownSpikeBounce1 },
        { "DownSpikeBounce 2", AnimationClip.DownSpikeBounce2 },
        
        { "Charge Up", AnimationClip.ChargeUp },
        
        { "Idle Hurt", AnimationClip.IdleHurt },
        { "TurnWalk", AnimationClip.TurnWalk },
        { "Walk", AnimationClip.Walk },
        { "Look Up Half", AnimationClip.LookUpHalf },
        { "Look Up Half End", AnimationClip.LookUpHalfEnd },
        { "LookDown Slight", AnimationClip.LookDownSlight },
        { "LookDown Slight End", AnimationClip.LookDownSlightEnd },
        
        { "Scuttle Start", AnimationClip.ScuttleStart },
        { "Scuttle Loop", AnimationClip.ScuttleLoop },
        { "Scuttle End", AnimationClip.ScuttleEnd },
        { "Scuttle Fall", AnimationClip.ScuttleFall },
        { "Scuttle Vault", AnimationClip.ScuttleVault },
        
        { "Stun", AnimationClip.Stun },
        { "Recoil", AnimationClip.Recoil },
        
        { "Grab Escape", AnimationClip.GrabEscape },
        
        { "Idle Updraft", AnimationClip.IdleUpdraft },
        { "Updraft Antic DJ", AnimationClip.UpdraftAnticDJ },
        { "Updraft Rise", AnimationClip.UpdraftRise },
        { "Updraft Rise Turn", AnimationClip.UpdraftRiseTurn },
    };

    /// <summary>
    /// Dictionary mapping animation clip enum values to IAnimationEffect instantiations.
    /// </summary>
    private static readonly Dictionary<AnimationClip, IAnimationEffect> AnimationEffects = new() {
        { AnimationClip.Slash, new Slash(SlashBase.SlashType.Normal) },
        { AnimationClip.SlashAlt, new Slash(SlashBase.SlashType.NormalAlt) },
        { AnimationClip.UpSlash, new Slash(SlashBase.SlashType.Up) },
        { AnimationClip.WallSlash, new Slash(SlashBase.SlashType.Wall) },
        { AnimationClip.DownSpike, new DownSpike() }, // Hunter Crest down slash
        { AnimationClip.V3DownSlash, new Slash(SlashBase.SlashType.Down) },
        { AnimationClip.DownSlash, new Slash(SlashBase.SlashType.Down) },
        { AnimationClip.DownSlashAlt, new Slash(SlashBase.SlashType.DownAlt) },
        { AnimationClip.SpinBall, new Slash(SlashBase.SlashType.Down) },
        { AnimationClip.DashAttackAntic, DashSlashAntic },
        { AnimationClip.DashAttack, new DashSlash() },
        { AnimationClip.DashUpperAntic, DashSlashAntic },
        { AnimationClip.DashUpper, new DashSlashReaper() },
        { AnimationClip.WandererDashAttack, new Slash(SlashBase.SlashType.Dash) },
        { AnimationClip.WandererDashAttackAlt, new Slash(SlashBase.SlashType.DashAlt) },
        { AnimationClip.DashAttackCharge, DashSlashAntic },
        
    };
    // TODO: implement all animation effects for sprint slashes (dash slashes/stabs). See Sprint FSM in shared templates

    /// <summary>
    /// The net client for sending animation updates.
    /// </summary>
    private readonly NetClient _netClient;

    /// <summary>
    /// The player manager to get player objects.
    /// </summary>
    private readonly PlayerManager _playerManager;

    /// <summary>
    /// The last animation clip sent.
    /// </summary>
    private string? _lastAnimationClip;

    // /// <summary>
    // /// Whether the animation controller was responsible for the last clip that was sent.
    // /// </summary>
    // private bool _animationControllerWasLastSent;

    /// <summary>
    /// Whether we should stop sending animations until the scene has changed.
    /// </summary>
    private bool _stopSendingAnimationUntilSceneChange;

    /// <summary>
    /// Whether the current dash has ended and we can start a new one.
    /// </summary>
    private bool _dashHasEnded = true;

    // /// <summary>
    // /// Whether the player has sent that they stopped crystal dashing.
    // /// </summary>
    // private bool _hasSentCrystalDashEnd = true;

    // /// <summary>
    // /// Whether the charge effect was last update active.
    // /// </summary>
    // private bool _lastChargeEffectActive;
    //
    // /// <summary>
    // /// Whether the charged effect was last update active
    // /// </summary>
    // private bool _lastChargedEffectActive;

    // /// <summary>
    // /// Stopwatch to keep track of a delay before being able to send another update for the charged effect.
    // /// </summary>
    // private readonly Stopwatch _chargedEffectStopwatch;
    //
    // /// <summary>
    // /// Stopwatch to keep track of a delay before being able to send another update for the charged end effect.
    // /// </summary>
    // private readonly Stopwatch _chargedEndEffectStopwatch;

    // /// <summary>
    // /// Whether the player was wall sliding last update.
    // /// </summary>
    // private bool _lastWallSlideActive;

    public AnimationManager(
        NetClient netClient,
        PlayerManager playerManager
    ) {
        _netClient = netClient;
        _playerManager = playerManager;

        // _chargedEffectStopwatch = new Stopwatch();
        // _chargedEndEffectStopwatch = new Stopwatch();
    }

    /// <summary>
    /// Initialize the animation manager by registering packet handlers and initializing animation effects.
    /// </summary>
    public void Initialize(ServerSettings serverSettings) {
        // Set the server settings for all animation effects
        foreach (var effect in AnimationEffects.Values) {
            effect.SetServerSettings(serverSettings);
        }
    }

    /// <summary>
    /// Register the game hooks for the animation manager.
    /// </summary>
    public void RegisterHooks() {
        // Register scene change, which is where we update the animation event handler
        SceneManager.activeSceneChanged += OnSceneChange;

        // Register callbacks for the hero animation controller for the Airborne animation
        // On.HeroAnimationController.Play += HeroAnimationControllerOnPlay;
        // On.HeroAnimationController.PlayFromFrame += HeroAnimationControllerOnPlayFromFrame;

        // Register callbacks for tracking the start of playing animation clips
        EventHooks.SpriteAnimatorWarpClipToLocalTime += Tk2dSpriteAnimatorOnWarpClipToLocalTime;
        EventHooks.SpriteAnimatorProcessEvents += Tk2dSpriteAnimatorOnProcessEvents;

        // Register a callback so we know when the dash has finished
        // On.HeroController.CancelDash += HeroControllerOnCancelDash;

        // Register a callback so we can check the nail art charge status
        // ModHooks.HeroUpdateHook += OnHeroUpdateHook;

        // Register a callback for when we get hit by a hazard
        // On.HeroController.DieFromHazard += HeroControllerOnDieFromHazard;
        // Also register a callback from when we respawn from a hazard
        // On.GameManager.HazardRespawn += GameManagerOnHazardRespawn;

        // Relinquish Control cancels a lot of effects, so we need to broadcast the end of these effects
        // On.HeroController.RelinquishControl += HeroControllerOnRelinquishControl;

        // Register when the player dies to send the animation
        // ModHooks.BeforePlayerDeadHook += OnDeath;
    }

    /// <summary>
    /// Deregister the game hooks for the animation manager.
    /// </summary>
    public void DeregisterHooks() {
        SceneManager.activeSceneChanged -= OnSceneChange;

        // On.HeroAnimationController.Play -= HeroAnimationControllerOnPlay;
        // On.HeroAnimationController.PlayFromFrame -= HeroAnimationControllerOnPlayFromFrame;

        // On.tk2dSpriteAnimator.WarpClipToLocalTime -= Tk2dSpriteAnimatorOnWarpClipToLocalTime;
        // On.tk2dSpriteAnimator.ProcessEvents -= Tk2dSpriteAnimatorOnProcessEvents;

        // On.HeroController.CancelDash -= HeroControllerOnCancelDash;

        // ModHooks.HeroUpdateHook -= OnHeroUpdateHook;

        // On.HeroController.DieFromHazard -= HeroControllerOnDieFromHazard;
        // On.GameManager.HazardRespawn -= GameManagerOnHazardRespawn;

        // On.HeroController.RelinquishControl -= HeroControllerOnRelinquishControl;

        // ModHooks.BeforePlayerDeadHook -= OnDeath;
    }

    /// <summary>
    /// Callback method when a player animation update is received. Will update the player object with the new
    /// animation.
    /// </summary>
    /// <param name="id">The ID of the player.</param>
    /// <param name="clipId">The ID of the animation clip.</param>
    /// <param name="frame">The frame that the animation should play from.</param>
    /// <param name="effectInfo">A byte array containing effect info for the animation.</param>
    public void OnPlayerAnimationUpdate(ushort id, int clipId, int frame, byte[]? effectInfo) {
        UpdatePlayerAnimation(id, clipId, frame);

        var animationClip = (AnimationClip) clipId;

        if (AnimationEffects.TryGetValue(animationClip, out var animationEffect)) {
            var playerObject = _playerManager.GetPlayerObject(id);
            if (!playerObject) {
                // Logger.Get().Warn(this, $"Tried to play animation effect {clipName} with ID: {id}, but player object doesn't exist");
                return;
            }

            // Check if the animation effect is a DamageAnimationEffect and if so,
            // set whether it should deal damage based on player teams
            if (animationEffect is DamageAnimationEffect damageAnimationEffect) {
                var localPlayerTeam = _playerManager.LocalPlayerTeam;
                var otherPlayerTeam = _playerManager.GetPlayerTeam(id);

                damageAnimationEffect.SetShouldDoDamage(
                    otherPlayerTeam != localPlayerTeam
                    || otherPlayerTeam.Equals(Team.None)
                    || localPlayerTeam.Equals(Team.None)
                );
            }

            animationEffect.Play(
                playerObject,
                effectInfo
            );
        }
    }

    /// <summary>
    /// Update the animation of the player sprite animator.
    /// </summary>
    /// <param name="id">The ID of the player.</param>
    /// <param name="clipId">The ID of the animation clip.</param>
    /// <param name="frame">The frame that the animation should play from.</param>
    public void UpdatePlayerAnimation(ushort id, int clipId, int frame) {
        var playerObject = _playerManager.GetPlayerObject(id);
        if (!playerObject) {
            // Logger.Get().Warn(this, $"Tried to update animation, but there was not matching player object for ID {id}");
            return;
        }

        var animationClip = (AnimationClip) clipId;

        if (_debugLogAnimations) Logger.Info($"Received PlayerAnimationUpdate: {animationClip}");

        if (!ClipEnumNames.ContainsSecond(animationClip)) {
            // This happens when we send custom clips, that can't be played by the sprite animator, so for now we
            // don't log it. This warning might be useful if we seem to be missing animations from the Knights
            // sprite animator.

            if (_debugLogAnimations) Logger.Warn($"Tried to update animation, but there was no entry for clip ID: {clipId}, enum: {animationClip}");
            return;
        }

        var clipName = ClipEnumNames[animationClip];

        if (_debugLogAnimations) Logger.Info($"  clipName: {clipName}");

        // Get the sprite animator and check whether this clip can be played before playing it
        var spriteAnimator = playerObject.GetComponent<tk2dSpriteAnimator>();

        var clip = spriteAnimator.GetClipByName(clipName);
        if (clip == null) {
            // Clip does not exist in the default library, so we look for override libraries from the configs
            foreach (var config in HeroController.instance.configs) {
                var overrideLib = config.Config.heroAnimOverrideLib;
                if (overrideLib == null) {
                    continue;
                }
                
                var configOverriddenClip = overrideLib.GetClipByName(clipName);
                if (configOverriddenClip != null) {
                    clip = configOverriddenClip;
                    break;
                }
            }

            if (clip == null) {
                Logger.Info("Could not find clip in override libraries of hero controller configs");
                return;
            }
        }

        if (_debugLogAnimations) Logger.Info($"  playing clip: {clipName}");
        spriteAnimator.PlayFromFrame(clip, frame);
    }

    /// <summary>
    /// Callback method when the scene changes.
    /// </summary>
    /// <param name="oldScene">The old scene instance.</param>
    /// <param name="newScene">The name scene instance.</param>
    private void OnSceneChange(Scene oldScene, Scene newScene) {
        // A scene change occurs, so we can send again
        _stopSendingAnimationUntilSceneChange = false;
    }

    /// <summary>
    /// Callback method when an animation fires in the sprite animator.
    /// </summary>
    /// <param name="clip">The sprite animation clip.</param>
    private void OnAnimationEvent(tk2dSpriteAnimationClip clip) {
        if (_debugLogAnimations) Logger.Info($"Animation event with name: {clip.name}");

        // If we are not connected, there is nothing to send to
        if (!_netClient.IsConnected) {
            return;
        }

        // If we need to stop sending until a scene change occurs, we skip
        if (_stopSendingAnimationUntilSceneChange) {
            return;
        }

        // If this is a clip that should be handled by the animation controller hook, we return
        if (AnimationControllerClipNames.Contains(clip.name)) {
            // Update the last clip name
            _lastAnimationClip = clip.name;

            return;
        }

        if (_debugLogAnimations) Logger.Info($"  conditions 1: {clip.name.Equals(_lastAnimationClip)}, {clip.wrapMode != tk2dSpriteAnimationClip.WrapMode.Once}, {!AllowedLoopAnimations.Contains(clip.name)}");

        // Skip event handling when we already handled this clip, unless it is a clip with wrap mode once
        if (clip.name.Equals(_lastAnimationClip)
            && clip.wrapMode != tk2dSpriteAnimationClip.WrapMode.Once
            && !AllowedLoopAnimations.Contains(clip.name)) {
            return;
        }

        if (_debugLogAnimations) Logger.Info($"  conditions 2: {clip.wrapMode != tk2dSpriteAnimationClip.WrapMode.Loop}, {clip.wrapMode != tk2dSpriteAnimationClip.WrapMode.LoopSection}, {clip.wrapMode != tk2dSpriteAnimationClip.WrapMode.Once}");

        // Skip clips that do not have the wrap mode loop, loop-section or once
        if (clip.wrapMode != tk2dSpriteAnimationClip.WrapMode.Loop &&
            clip.wrapMode != tk2dSpriteAnimationClip.WrapMode.LoopSection &&
            clip.wrapMode != tk2dSpriteAnimationClip.WrapMode.Once) {
            return;
        }

        // Make sure that when we enter a building, we don't transmit any more animation events
        // TODO: the same issue applied to exiting a building, but that is less trivial to solve
        if (clip.name.Equals("Enter")) {
            _stopSendingAnimationUntilSceneChange = true;
        }

        // Check special case of downwards dashes that trigger the animation event twice
        // We only send it once if the current dash has ended
        if (clip.name.Equals("Dash Down Test1")
            || clip.name.Equals("Shadow Dash Down Test1")
            || clip.name.Equals("Shadow Dash Down Sharp Test1")) {
            if (!_dashHasEnded) {
                return;
            }

            _dashHasEnded = false;
        }

        if (!ClipEnumNames.ContainsFirst(clip.name)) {
            Logger.Warn($"Player sprite animator played unknown clip, name: {clip.name}");
            return;
        }

        var animationClip = ClipEnumNames[clip.name];

        // Check whether there is an effect that adds info to this packet
        if (AnimationEffects.TryGetValue(animationClip, out var effect)) {
            var effectInfo = effect.GetEffectInfo();

            _netClient.UpdateManager.UpdatePlayerAnimation(animationClip, 0, effectInfo);
        } else {
            _netClient.UpdateManager.UpdatePlayerAnimation(animationClip);
        }

        if (_debugLogAnimations) Logger.Info($"  Sending animation: {animationClip}");

        // Update the last clip name, since it changed
        _lastAnimationClip = clip.name;

        // // We have sent a different clip, so we can reset this
        // _animationControllerWasLastSent = false;
    }

    // /// <summary>
    // /// Callback method on the HeroAnimationController#Play method.
    // /// </summary>
    // /// <param name="self">The hero animation controller instance.</param>
    // /// <param name="clipName">The name of the clip to play.</param>
    // private void HeroAnimationControllerOnPlay(HeroAnimationController self, string clipName) {
    //     OnAnimationControllerPlay(clipName, 0);
    // }
    //
    // /// <summary>
    // /// Callback method on the HeroAnimationController#PlayFromFrame method.
    // /// </summary>
    // /// <param name="self">The hero animation controller instance.</param>
    // /// <param name="clipName">The name of the clip to play.</param>
    // /// <param name="frame">The frame from which to play the clip.</param>
    // private void HeroAnimationControllerOnPlayFromFrame(HeroAnimationController self, string clipName, int frame) {
    //     OnAnimationControllerPlay(clipName, frame);
    // }
    //
    // /// <summary>
    // /// Callback method when the HeroAnimationController plays an animation.
    // /// </summary>
    // /// <param name="clipName">The name of the clip to play.</param>
    // /// <param name="frame">The frame from which to play the clip.</param>
    // private void OnAnimationControllerPlay(string clipName, int frame) {
    //     // If we are not connected, there is nothing to send to
    //     if (!_netClient.IsConnected) {
    //         return;
    //     }
    //
    //     // If this is not a clip that should be handled by the animation controller hook, we return
    //     if (!AnimationControllerClipNames.Contains(clipName)) {
    //         return;
    //     }
    //
    //     // If the animation controller is responsible for the last sent clip, we skip
    //     // this is to ensure that we don't spam packets of the same clip
    //     if (!_animationControllerWasLastSent) {
    //         if (!ClipEnumNames.ContainsFirst(clipName)) {
    //             Logger.Warn($"Player animation controller played unknown clip, name: {clipName}");
    //             return;
    //         }
    //
    //         var clipId = ClipEnumNames[clipName];
    //
    //         _netClient.UpdateManager.UpdatePlayerAnimation(clipId, frame);
    //
    //         // This was the last clip we sent
    //         _animationControllerWasLastSent = true;
    //     }
    // }
    //
    // /// <summary>
    // /// Callback method on the HeroController#CancelDash method.
    // /// </summary>
    // /// <param name="orig">The original method.</param>
    // /// <param name="self">The HeroController instance.</param>
    // private void HeroControllerOnCancelDash(On.HeroController.orig_CancelDash orig, HeroController self) {
    //     orig(self);
    //
    //     // If we are not connected, there is nothing to send to
    //     if (!_netClient.IsConnected) {
    //         return;
    //     }
    //
    //     _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.DashEnd);
    //
    //     // The dash has ended, so we can send a new one when we dash
    //     _dashHasEnded = true;
    // }
    //
    // /// <summary>
    // /// Callback method for when the hero updates.
    // /// </summary>
    // private void OnHeroUpdateHook() {
    //     // If we are not connected, there is nothing to send to
    //     if (!_netClient.IsConnected) {
    //         return;
    //     }
    //
    //     var chargeEffectActive = HeroController.instance.artChargeEffect.activeSelf;
    //     var chargedEffectActive = HeroController.instance.artChargedEffect.activeSelf;
    //
    //     if (chargeEffectActive && !_lastChargeEffectActive) {
    //         // Charge effect is now active, which wasn't last update, so we can send the charge animation packet
    //         _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.NailArtCharge);
    //     }
    //
    //     if (chargedEffectActive && !_lastChargedEffectActive) {
    //         if (!_chargedEffectStopwatch.IsRunning || _chargedEffectStopwatch.ElapsedMilliseconds > 100) {
    //             // Charged effect is now active, which wasn't last update, so we can send the charged animation packet
    //             _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.NailArtCharged);
    //
    //             // Start the stopwatch to make sure this animation is not triggered repeatedly
    //             _chargedEffectStopwatch.Restart();
    //         }
    //     }
    //
    //     if (!chargeEffectActive && _lastChargeEffectActive && !chargedEffectActive) {
    //         // The charge effect is now inactive and we are not fully charged
    //         // This means that we cancelled the nail art charge
    //         _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.NailArtChargeEnd);
    //     }
    //
    //     if (!chargedEffectActive && _lastChargedEffectActive) {
    //         if (!_chargedEndEffectStopwatch.IsRunning || _chargedEndEffectStopwatch.ElapsedMilliseconds > 100) {
    //             // The charged effect is now inactive, so we are done with the nail art
    //             _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.NailArtChargeEnd);
    //
    //             // Set the delay variable to make sure this animation is not triggered repeatedly
    //             _chargedEndEffectStopwatch.Restart();
    //         }
    //     }
    //
    //     // Update the latest states
    //     _lastChargeEffectActive = chargeEffectActive;
    //     _lastChargedEffectActive = chargedEffectActive;
    //
    //     // Obtain the current wall slide state
    //     var wallSlideActive = HeroController.instance.cState.wallSliding;
    //
    //     if (!wallSlideActive && _lastWallSlideActive) {
    //         // We were wall sliding last update, but not anymore, so we send a wall slide end animation
    //         _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.WallSlideEnd);
    //     }
    //
    //     // Update the last state
    //     _lastWallSlideActive = wallSlideActive;
    // }

    /// <summary>
    /// Callback method on the tk2dSpriteAnimator#WarpClipToLocalTime method. This method executes
    /// the animation event for clips, and we want to know when those clips start playing.
    /// </summary>
    /// <param name="self">The tk2dSpriteAnimator instance.</param>
    /// <param name="clip">The tk2dSpriteAnimationClip instance.</param>
    /// <param name="time">The time to warp to.</param>
    private void Tk2dSpriteAnimatorOnWarpClipToLocalTime(
        tk2dSpriteAnimator self,
        tk2dSpriteAnimationClip clip,
        float time
    ) {
        var localPlayer = HeroController.instance;
        if (!localPlayer) {
            return;
        }
    
        var spriteAnimator = localPlayer.GetComponent<tk2dSpriteAnimator>();
        if (self != spriteAnimator) {
            return;
        }

        var clipTime = self.clipTime;
        var index = (int) clipTime % clip.frames.Length;
        var frame = clip.frames[index];
    
        if (index == 0 || frame.triggerEvent || AllowedLoopAnimations.Contains(clip.name)) {
            if (_debugLogAnimations) Logger.Info($"OnAnimationEvent from tk2dSpriteAnimatorOnWarpClipToLocalTime: {clip.name}, conditions: {index == 0}, {frame.triggerEvent}, {AllowedLoopAnimations.Contains(clip.name)}");
            OnAnimationEvent(clip);
        }
    }

    /// <summary>
    /// Callback method on the tk2dSpriteAnimator#OnProcessEvents method. This method executes
    /// the animation event for clips, and we want to know when those clips start playing.
    /// </summary>
    /// <param name="self">The tk2dSpriteAnimator instance.</param>
    /// <param name="start">The start of frames to process.</param>
    /// <param name="last">The last frame to process.</param>
    /// <param name="direction">The direction in which to process.</param>
    private void Tk2dSpriteAnimatorOnProcessEvents(
        tk2dSpriteAnimator self,
        int start,
        int last,
        int direction
    ) {
        var localPlayer = HeroController.instance;
        if (!localPlayer) {
            return;
        }
    
        var spriteAnimator = localPlayer.GetComponent<tk2dSpriteAnimator>();
        if (self != spriteAnimator) {
            return;
        }
    
        if (start == last) {
            return;
        }
    
        var num = last + direction;
        var frames = self.CurrentClip.frames;
    
        var ignoreClipNames = new[] { "v3 Down Slash", "DownSpike", "DownSlash" };
    
        for (var i = start + direction; i != num; i += direction) {
            if (i >= frames.Length || i < 0) {
                Logger.Warn("tk2dSpriteAnimator ProcessEvents index out of bounds!");
                continue;
            }

            if (i != 0 && !frames[i].triggerEvent || ignoreClipNames.Contains(self.CurrentClip.name)) {
                continue;
            }
    
            if (_debugLogAnimations) Logger.Info($"OnAnimationEvent from tk2dSpriteAnimatorOnProcessEvents: {self.CurrentClip.name}, conditions: {i}, {frames[i].triggerEvent}");
            OnAnimationEvent(self.CurrentClip);
        }
    }

    // /// <summary>
    // /// Callback method on the HeroController#DieFromHazard method.
    // /// </summary>
    // /// <param name="self">The HeroController instance.</param>
    // /// <param name="hazardType">The type of hazard.</param>
    // /// <param name="angle">The angle at which the hero entered the hazard.</param>
    // /// <returns>An enumerator for this coroutine.</returns>
    // private void HeroControllerOnDieFromHazard(HeroController self, HazardType hazardType, float angle) {
    //     // If we are not connected, there is nothing to send to
    //     if (!_netClient.IsConnected) {
    //         return;
    //     }
    //
    //     _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.HazardDeath, 0, [
    //         // hazardType.Equals(HazardType.SPIKES),
    //         // hazardType.Equals(HazardType.ACID)
    //     ]);
    // }
    //
    // /// <summary>
    // /// Callback method on the GameManager#HazardRespawn method.
    // /// </summary>
    // /// <param name="self">The GameManager instance.</param>
    // private void GameManagerOnHazardRespawn(GameManager self) {
    //     // If we are not connected, there is nothing to send to
    //     if (!_netClient.IsConnected) {
    //         return;
    //     }
    //
    //     // _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.HazardRespawn);
    // }

    /// <summary>
    /// Callback method for when a player death is received.
    /// </summary>
    /// <param name="data">The generic client data for this event.</param>
    public void OnPlayerDeath(GenericClientData data) {
        // And play the death animation for the ID in the packet
        MonoBehaviourUtil.Instance.StartCoroutine(PlayDeathAnimation(data.Id));
    }

    // /// <summary>
    // /// Callback method for when the local player dies.
    // /// </summary>
    // private void OnDeath() {
    //     // If we are not connected, there is nothing to send to
    //     if (!_netClient.IsConnected) {
    //         return;
    //     }
    //
    //     Logger.Debug("Client has died, sending PlayerDeath data");
    //
    //     // Let the server know that we have died            
    //     _netClient.UpdateManager.SetDeath();
    // }

    /// <summary>
    /// Play the death animation for the player with the given ID.
    /// </summary>
    /// <param name="id">The ID of the player.</param>
    /// <returns>An enumerator for the coroutine.</returns>
    private IEnumerator PlayDeathAnimation(ushort id) {
        Logger.Debug($"Starting death animation for ID: {id}");
        yield break;

        // // Get the player object corresponding to this ID
        // var playerObject = _playerManager.GetPlayerObject(id);
        //
        // // Get the sprite animator and start playing the Death animation
        // var animator = playerObject.GetComponent<tk2dSpriteAnimator>();
        // animator.Stop();
        // animator.PlayFromFrame("Death", 0);
        //
        // // Obtain the duration for the animation
        // var deathAnimationDuration = animator.GetClipByName("Death").Duration;
        //
        // // After half a second we want to throw out the nail (as defined in the FSM)
        // yield return new WaitForSeconds(0.5f);
        //
        // // Calculate the duration remaining until the death animation is finished
        // var remainingDuration = deathAnimationDuration - 0.5f;
        //
        // // Obtain the local player object, to copy actions from
        // var localPlayerObject = HeroController.instance.gameObject;
        //
        // // Get the FSM for the Hero Death
        // var heroDeathAnimFsm = localPlayerObject
        //     .FindGameObjectInChildren("Hero Death")
        //     .LocateMyFSM("Hero Death Anim");
        //
        // // Get the nail fling object from the Blow state
        // var nailObject = heroDeathAnimFsm.GetFirstAction<FlingObjectsFromGlobalPool>("Blow");
        //
        // // Spawn it relative to the player
        // var nailGameObject = nailObject.gameObject.Value.Spawn(
        //     playerObject.transform.position,
        //     Quaternion.Euler(Vector3.zero)
        // );
        //
        // // Get the rigidbody component that we need to throw around
        // var nailRigidBody = nailGameObject.GetComponent<Rigidbody2D>();
        //
        // // Get a random speed and angle and calculate the rigidbody velocity
        // var speed = Random.Range(18, 22);
        // float angle = Random.Range(50, 130);
        // var velX = speed * Mathf.Cos(angle * ((float) System.Math.PI / 180f));
        // var velY = speed * Mathf.Sin(angle * ((float) System.Math.PI / 180f));
        //
        // // Set the velocity so it starts moving
        // nailRigidBody.velocity = new Vector2(velX, velY);
        //
        // // Wait for the remaining duration of the death animation
        // yield return new WaitForSeconds(remainingDuration);
        //
        // // Now we can disable the player object so it isn't visible anymore
        // playerObject.SetActive(false);
        //
        // // Check which direction we are facing, we need this in a few variables
        // var facingRight = playerObject.transform.localScale.x > 0;
        //
        // // Depending on which direction the player was facing, choose a state
        // var stateName = "Head Left";
        // if (facingRight) {
        //     stateName = "Head Right";
        // }
        //
        // // Obtain a head object from the either Head states and instantiate it
        // var headObject = heroDeathAnimFsm.GetFirstAction<CreateObject>(stateName);
        // var headGameObject = Object.Instantiate(
        //     headObject.gameObject.Value,
        //     playerObject.transform.position + new Vector3(facingRight ? 0.2f : -0.2f, -0.02f, -0.01f),
        //     Quaternion.identity
        // );
        //
        // // Get the rigidbody component of the head object
        // var headRigidBody = headGameObject.GetComponent<Rigidbody2D>();
        //
        // // Calculate the angle at which we are going to throw 
        // var headAngle = 15f * Mathf.Cos((facingRight ? 100f : 80f) * ((float) System.Math.PI / 180f));
        //
        // // Now set the velocity as this angle
        // headRigidBody.velocity = new Vector2(headAngle, headAngle);
        //
        // // Finally add required torque (according to the FSM)
        // headRigidBody.AddTorque(facingRight ? 20f : -20f);
    }

    // /// <summary>
    // /// Callback method on the HeroController#RelinquishControl method.
    // /// </summary>
    // /// <param name="self">The HeroController instance.</param>
    // private void HeroControllerOnRelinquishControl(HeroController self) {
    //     // If we are not connected, there is no need to send
    //     if (!_netClient.IsConnected) {
    //         return;
    //     }
    //
    //     // If we need to stop sending until a scene change occurs, we skip
    //     if (_stopSendingAnimationUntilSceneChange) {
    //         return;
    //     }
    //
    //     // If the player has not sent the end of the crystal dash animation then we need to do it now,
    //     // because crystal dash is cancelled when relinquishing control
    //     if (!_hasSentCrystalDashEnd) {
    //         // _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.SDAirBrake);
    //     }
    //
    //     // _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.DashEnd);
    // }

    /// <summary>
    /// Get the AnimationClip enum value for the currently playing animation of the local player.
    /// </summary>
    /// <returns>An AnimationClip enum value.</returns>
    public static AnimationClip GetCurrentAnimationClip() {
        var currentClipName = HeroController.instance.GetComponent<tk2dSpriteAnimator>().CurrentClip.name;

        if (ClipEnumNames.ContainsFirst(currentClipName)) {
            return ClipEnumNames[currentClipName];
        }

        return 0;
    }
}

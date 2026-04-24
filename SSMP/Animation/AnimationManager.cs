using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker.Actions;
using SSMP.Animation.Effects;
using SSMP.Animation.Effects.SilkSkills;
using SSMP.Collection;
using SSMP.Fsm;
using SSMP.Game;
using SSMP.Game.Client;
using SSMP.Game.Settings;
using SSMP.Hooks;
using SSMP.Internals;
using SSMP.Networking.Client;
using SSMP.Util;
using UnityEngine;
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
        { "Sit Lean", AnimationClip.SitLean },
        { "Sit Look Left", AnimationClip.SitLookLeft },
        { "Sitting Asleep", AnimationClip.SittingAsleep },
        { "Get Off", AnimationClip.GetOff },
        { "Land", AnimationClip.Land },
        { "HardLand", AnimationClip.HardLand },
        { "HardLand Greymoor", AnimationClip.HardLandGreymoor },
        { "HardLand Quick", AnimationClip.HardLandQuick },
        { "Super Hard Land", AnimationClip.SuperHardLand },
        { "Hop", AnimationClip.Hop },
        { "Hop Land", AnimationClip.HopLand },
        { "Hop To Somersault", AnimationClip.HopToSomersault },
        { "Run", AnimationClip.Run },
        { "RunQ", AnimationClip.RunQ },
        { "Run To Idle", AnimationClip.RunToIdle },
        { "Run To Idle C", AnimationClip.RunToIdleC },
        { "Idle", AnimationClip.Idle },
        { "Idle Backward", AnimationClip.IdleBackward},
        { "Idle BG", AnimationClip.IdleBG},
        { "Idle Hurt", AnimationClip.IdleHurt},
        { "Idle Hurt Listen", AnimationClip.IdleHurtListen},
        { "Idle Hurt Listen Backward", AnimationClip.IdleHurtListenBackward},
        { "Idle Hurt Listen Windy", AnimationClip.IdleHurtListenWindy},
        { "Idle Hurt NoNeedle", AnimationClip.IdleHurtNoNeedle},
        { "Idle Hurt Talk", AnimationClip.IdleHurtTalk},
        { "Idle Hurt Talk Backward", AnimationClip.IdleHurtTalkBackward},
        { "Idle Hurt Talk Turn Backward", AnimationClip.IdleHurtTalkTurnBackward},
        { "Idle Hurt Talk Turn Forward", AnimationClip.IdleHurtTalkTurnForward},
        { "Idle Hurt Talk Windy", AnimationClip.IdleHurtTalkWindy},
        { "Idle Hurt Windy", AnimationClip.IdleHurtWindy},
        { "Idle Slap", AnimationClip.IdleSlap},
        { "Idle To Run Short", AnimationClip.IdleToRunShort},
        { "Idle To Run Weak", AnimationClip.IdleToRunWeak},
        { "Idle To Run", AnimationClip.IdleToRun },
        { "Idle Windy", AnimationClip.IdleWindy },
        { "Walk To Idle", AnimationClip.WalkToIdle },
        { "Run To Walk", AnimationClip.RunToWalk },
        { "Run To Walk Weak", AnimationClip.RunToWalkWeak },
        { "Land To Walk", AnimationClip.LandToWalk },
        { "Land to Sprint", AnimationClip.LandToSprint },
        { "Turn", AnimationClip.Turn },
        { "Dash", AnimationClip.Dash },
        { "Dash Down", AnimationClip.DashDown },
        { "Dash Down Land", AnimationClip.DashDownLand },
        { "Dash Down End", AnimationClip.DashDownEnd },
        //{ "DashStabEffect", AnimationClip.DashStabEffect },
        //{ "DashStabEffect_Glow", AnimationClip.DashStabEffectGlow },
        { "Sprint Lv2", AnimationClip.SprintLv2 },
        { "Sprint Lv3", AnimationClip.SprintLv3 },
        { "Sprint Recoil", AnimationClip.SprintRecoil },
        { "Sprint Skid To Run", AnimationClip.SprintSkidToRun },
        { "Sprint To Run", AnimationClip.SprintToRun },
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
        { "Double Jump Wings 2", AnimationClip.DoubleJumpWings2 },
        { "Land To Run", AnimationClip.LandToRun },
        { "Dash Attack Antic", AnimationClip.DashAttackAntic },
        { "Dash Attack", AnimationClip.DashAttack },
        { "Dash Attack Recover", AnimationClip.DashAttackRecover },
        { "Dash Attack Antic Long", AnimationClip.DashAttackAnticLong },
        { "Dash Attack Antic Short", AnimationClip.DashAttackAnticShort },
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
        { "DownSpike Burst", AnimationClip.DownSpikeBurst },
        { "Downspike Recovery", AnimationClip.DownSpikeRecovery },
        { "Downspike Recovery Land", AnimationClip.DownSpikeRecoveryLand },
        { "Downspike Followup", AnimationClip.DownSpikeFollowup },
        { "UpSlash", AnimationClip.UpSlash },
        { "Slash Land", AnimationClip.SlashLand },
        { "Slash_Charged", AnimationClip.SlashCharged },
        { "Slash_Charged_Loop", AnimationClip.SlashChargedLoop },
        { "Umbrella Inflate Antic", AnimationClip.UmbrellaInflateAntic },
        { "Walljump", AnimationClip.WallJump },
        { "Walljump Antic", AnimationClip.WallJumpAntic },
        { "Walljump Puff", AnimationClip.WallJumpPuff },
        { "Walljump Somersault", AnimationClip.WallJumpSomersault },
        { "Wallrun", AnimationClip.Wallrun },
        { "Wallrun Antic", AnimationClip.WallrunAntic },
        { "Wall Slide", AnimationClip.WallSlide },
        { "Wall Cling", AnimationClip.WallCling },
        { "Slide Brake", AnimationClip.SlideBrake },
        { "Slide Brake End", AnimationClip.SlideBrakeEnd },
        { "Slide Fast", AnimationClip.SlideFast },
        { "Slide Fast End", AnimationClip.SlideFastEnd },
        { "Slide Normal", AnimationClip.SlideNormal },
        { "Slide Start", AnimationClip.SlideStart },
        { "Sit Fall Asleep", AnimationClip.SitFallAsleep },
        { "Umbrella Inflate", AnimationClip.UmbrellaInflate },
        { "Umbrella Float", AnimationClip.UmbrellaFloat },
        { "Umbrella Deflate", AnimationClip.UmbrellaDeflate },
        { "Umbrella Turn", AnimationClip.UmbrellaTurn },
        { "Idle Rest", AnimationClip.IdleRest },
        { "Mantle Land To Run", AnimationClip.MantleLandToRun },
        { "Sit", AnimationClip.Sit },
        { "Sprint", AnimationClip.Sprint },
        { "Sprint Backflip", AnimationClip.SprintBackflip },
        { "AirSphere", AnimationClip.AirSphere },
        { "AirSphere Antic", AnimationClip.AirSphereAntic },
        { "AirSphere Dissipate", AnimationClip.AirSphereDissipate },
        { "AirSphere RepeatAntic", AnimationClip.AirSphereRepeatAntic },
        { "Silk Boss Needle Cast", AnimationClip.SilkBossNeedleCast },
        { "Silk Boss Needle Fire", AnimationClip.SilkBossNeedleFire },
        { "Taunt", AnimationClip.Taunt },
        { "Taunt Back", AnimationClip.TauntBack },
        { "Taunt Back End", AnimationClip.TauntBackEnd },
        { "Taunt Back Up", AnimationClip.TauntBackUp },
        { "Taunt End to Idle", AnimationClip.TauntEndtoIdle },
        { "Taunt Idle", AnimationClip.TauntIdle },
        { "Taunt Rings", AnimationClip.TauntRings },
        { "Taunt Rings Flash", AnimationClip.TauntRingsFlash },
        { "Taunt Straight Back", AnimationClip.TauntStraightBack },
        { "Taunt Straight Back Q", AnimationClip.TauntStraightBackQ },
        { "Taunt Thread", AnimationClip.TauntThread },
        { "Challenge Strong", AnimationClip.ChallengeStrong },
        { "Challenge Talk", AnimationClip.ChallengeTalk },
        { "ToChallengeTalk", AnimationClip.ToChallengeTalk },
        { "Challenge Talk End", AnimationClip.ChallengeTalkEnd },
        { "Challenge Talk End ToIdle", AnimationClip.ChallengeTalkEndToIdle },
        { "Challenge Talk End ToTalk", AnimationClip.ChallengeTalkEndToTalk },
        { "Challenge Talk Idle", AnimationClip.ChallengeTalkIdle },
        { "Challenge Talk Idle Start", AnimationClip.ChallengeTalkIdleStart },
        { "Challenge Talk Start", AnimationClip.ChallengeTalkStart },
        { "ChallengeStrongToIdle", AnimationClip.ChallengeStrongToIdle },

        { "Needolin Start", AnimationClip.NeedolinStart },
        { "Needolin StartCancelable", AnimationClip.NeedolinStartCancelable },
        { "Needolin End", AnimationClip.NeedolinEnd },
        { "Needolin Play", AnimationClip.NeedolinPlay },
        { "NeedolinSit Start", AnimationClip.NeedolinSitStart },
        { "NeedolinSit Play", AnimationClip.NeedolinSitPlay },
        { "Needolin Play Low", AnimationClip.NeedolinPlayLow },
        { "Needolin Play Low Transition", AnimationClip.NeedolinPlayLowTransition },
        { "Needolin Play High", AnimationClip.NeedolinPlayHigh },
        { "Needolin Play High Transition", AnimationClip.NeedolinPlayHighTransition },
        { "NeedolinSit End", AnimationClip.NeedolinSitEnd },
        { "NeedolinSit Turn", AnimationClip.NeedolinSitTurn },
        { "Needolin Deep End", AnimationClip.NeedolinDeepEnd },
        { "Needolin Prompted Idle", AnimationClip.NeedolinPromptedIdle },
        { "Needolin Prompted Play End", AnimationClip.NeedolinPromptedPlayEnd },
        { "Needolin Prompted Play Start", AnimationClip.NeedolinPromptedPlayStart },
        { "Needolin Prompted Start", AnimationClip.NeedolinPromptedStart },
        { "Needolin Thread", AnimationClip.NeedolinThread },
        { "Needolin Turn", AnimationClip.NeedolinTurn },
        { "Harpoon Antic", AnimationClip.HarpoonAntic },
        { "Harpoon Throw", AnimationClip.HarpoonThrow },
        { "Harpoon Catch", AnimationClip.HarpoonCatch },
        { "Harpoon Catch Back", AnimationClip.HarpoonCatchBack },
        { "Harpoon Dash", AnimationClip.HarpoonDash },
        { "Harpoon Needle", AnimationClip.HarpoonNeedle },
        { "Harpoon Needle Return", AnimationClip.HarpoonNeedleReturn },
        { "Harpoon Needle Wall Hit", AnimationClip.HarpoonNeedleWallHit },
        { "Harpoon Thread", AnimationClip.HarpoonThread },

        { "Silk Charge End", AnimationClip.SilkChargeEnd },
        { "Map Open", AnimationClip.MapOpen },
        { "Map Idle", AnimationClip.MapIdle },
        { "Map Away", AnimationClip.MapAway },
        { "Map Turn", AnimationClip.MapTurn },
        { "Map Update", AnimationClip.MapUpdate },
        { "Map Walk", AnimationClip.MapWalk },
        { "Sit Map Open", AnimationClip.SitMapOpen },
        { "Sit Map Close", AnimationClip.SitMapClose },
        { "BindCharge Ground", AnimationClip.BindChargeGround },
        { "BindCharge Air", AnimationClip.BindChargeAir },
        { "BindBurst Air", AnimationClip.BindBurstAir },
        { "BindBurst Ground", AnimationClip.BindBurstGround },
        { "Reserve BindBurst Air", AnimationClip.ReserveBindBurstAir },
        { "Reserve BindBurst Ground", AnimationClip.ReserveBindBurstGround },
        { "Bind Cursed End", AnimationClip.BindCursedEnd },
        { "Bind Cursed Start", AnimationClip.BindCursedStart },
        { "Bind Cursed Mid", AnimationClip.BindCursedMid },
        { "Bind First End", AnimationClip.BindFirstEnd },
        { "Bind First Stand", AnimationClip.BindFirstStand },
        { "Bind Flash", AnimationClip.BindFlash },
        { "Bind Silk", AnimationClip.BindSilk },
        { "Bind Silk FirstWeaver", AnimationClip.BindSilkFirstWeaver },
        { "Bind Silk Loop", AnimationClip.BindSilkLoop },
        { "Bind Silk Quick", AnimationClip.BindSilkQuick },
        { "BindCancel Air", AnimationClip.BindCancelAir },
        { "BindCancel Ground", AnimationClip.BindCancelGround },
        { "BindCharge GrabNeedle", AnimationClip.BindChargeGrabNeedle },
        { "Reserve BindCharge Air", AnimationClip.ReserveBindChargeAir },
        { "Reserve BindCharge Ground", AnimationClip.ReserveBindChargeGround },
        { "LookDown", AnimationClip.LookDown },
        { "LookDownEnd", AnimationClip.LookDownEnd },
        
        { "Super Jump Antic", AnimationClip.SuperJumpAntic },
        { "Super Jump Throw", AnimationClip.SuperJumpThrow },
        { "Super Jump Throw Wait", AnimationClip.SuperJumpThrowWait },
        { "Super Jump Jump Antic", AnimationClip.SuperJumpJumpAntic },
        { "Super Jump Loop", AnimationClip.SuperJumpLoop },
        { "Super Jump Hit Roof", AnimationClip.SuperJumpHitRoof },
        { "Super Jump Fall", AnimationClip.SuperJumpFall },
        { "Super Jump Antic Cancel", AnimationClip.SuperJumpAnticCancel },
        { "Super Jump Antic Effect", AnimationClip.SuperJumpAnticEffect },
        { "Super Jump Antic Effect End", AnimationClip.SuperJumpAnticEffectEnd },
        { "Super Jump Catch Cancel", AnimationClip.SuperJumpCatchCancel },
        { "Super Jump Hit Roof Q", AnimationClip.SuperJumpHitRoofQ },
        { "Super Jump Loop Cancel", AnimationClip.SuperJumpLoopCancel },
        { "Super Jump Thread", AnimationClip.SuperJumpThread },
        { "Super Jump Thread Loop", AnimationClip.SuperJumpThreadLoop },

        { "Fall", AnimationClip.Fall },
        { "FallToProstrate", AnimationClip.FallToProstrate },

        { "LookUp", AnimationClip.LookUp },
        { "LookUpEnd", AnimationClip.LookUpEnd },
        { "Surface In", AnimationClip.SurfaceIn },
        { "Surface InToIdle", AnimationClip.SurfaceInToIdle },
        { "Surface Idle", AnimationClip.SurfaceIdle },
        { "Surface IdleToSwim", AnimationClip.SurfaceIdleToSwim },
        { "Surface TurnToSwim", AnimationClip.SurfaceTurnToSwim },
        { "Surface Current In Recover", AnimationClip.SurfaceCurrentInRecover },
        { "Surface Current In Tumble", AnimationClip.SurfaceCurrentInTumble },
        { "Swim Dash", AnimationClip.SwimDash },
        { "Swim Dash Turn", AnimationClip.SwimDashTurn },
        { "Swim Dash Bonk", AnimationClip.SwimDashBonk },
        { "Mantle Cancel To Jump", AnimationClip.MantleCancelToJump },
        { "Mantle Cancel To Jump Backwards", AnimationClip.MantleCancelToJumpBackwards },
        { "Wall Scramble Antic", AnimationClip.WallScrambleAntic },
        { "Wall Scramble", AnimationClip.WallScramble },
        { "Wall Scramble End", AnimationClip.WallScrambleEnd },
        { "Wall Scramble Mantle", AnimationClip.WallScrambleMantle },
        { "Wall Scramble Quickened", AnimationClip.WallScrambleQuickened },
        { "Wall Scramble Repeat", AnimationClip.WallScrambleRepeat },
        { "Wall Slash", AnimationClip.WallSlash },
        { "Somersault Pin Drop", AnimationClip.SomersaultPinDrop },
        { "Airborne", AnimationClip.Airborne },
        { "Bonked", AnimationClip.Bonked },
        { "Sprint Bonk", AnimationClip.SprintBonk },
        { "Bonked Fast", AnimationClip.BonkedFast },
        { "Bonked Land", AnimationClip.BonkedLand },
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
        { "DownSpike Charged", AnimationClip.DownSpikeCharged },
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
        { "NeedleThrow Burst", AnimationClip.NeedleThrowBurst },
        { "NeedleThrow Out", AnimationClip.NeedleThrowOut },
        { "NeedleThrow Return", AnimationClip.NeedleThrowReturn },
        { "NeedleThrow Return Short", AnimationClip.NeedleThrowReturnShort },
        { "NeedleThrow Thread", AnimationClip.NeedleThrowThread },
        { "NeedleThrow Thunk", AnimationClip.NeedleThrowThunk },

        { "Parry Stance", AnimationClip.ParryStance },
        { "Parry Stance Ground", AnimationClip.ParryStanceGround },
        { "Parry Recover", AnimationClip.ParryRecover },
        { "Parry Recover Ground", AnimationClip.ParryRecoverGround },
        { "Parry Clash", AnimationClip.ParryClash },
        { "Parry Clash Effect", AnimationClip.ParryClashEffect },
        { "Parry Dash", AnimationClip.ParryDash },
        { "Parry DashBurst", AnimationClip.ParryDashBurst },
        { "Parry Ready", AnimationClip.ParryReady },
        { "Parry Recovery Skid", AnimationClip.ParryRecoverySkid },
        { "Parry Stance Flash", AnimationClip.ParryStanceFlash },
        { "Parry Stance Flash Q", AnimationClip.ParryStanceFlashQ },
        { "Parry Thread", AnimationClip.ParryThread },
        { "Get Parry Dash", AnimationClip.GetParryDash },
        { "Get Parry End", AnimationClip.GetParryEnd },
        { "Get Parry Prepare", AnimationClip.GetParryPrepare },

        { "AirSphere Attack", AnimationClip.AirSphereAttack },
        { "AirSphere End", AnimationClip.AirSphereEnd },
        { "AirSphere Refresh", AnimationClip.AirSphereRefresh },
        
        { "Silk Charge Antic", AnimationClip.SilkChargeAntic },
        { "Silk Charge", AnimationClip.SilkCharge },
        { "Silk Charge Recover", AnimationClip.SilkChargeRecover },
        { "Silk Charge Zap", AnimationClip.SilkChargeZap },
        { "Silk Charge Recover Zap", AnimationClip.SilkChargeRecoverZap },
        { "Silk Charge Antic Zap", AnimationClip.SilkChargeAnticZap },


        { "Silk Bomb Antic", AnimationClip.SilkBombAntic },
        { "Silk Bomb Antic Q", AnimationClip.SilkBombAnticQ },
        { "Silk Bomb Loop", AnimationClip.SilkBombLoop },
        { "Silk Bomb Recover", AnimationClip.SilkBombRecover },
        { "Silk Bomb Locations", AnimationClip.SilkBombLocations },
        
        { "Sit Craft", AnimationClip.SitCraft },
        { "Sit Craft Silk", AnimationClip.SitCraftSilk },
        { "ToolThrow Up", AnimationClip.ToolThrowUp },
        { "ToolThrow Q", AnimationClip.ToolThrowQ },
        { "ToolThrowAlt Q", AnimationClip.ToolThrowAltQ },
        { "ToolThrow M", AnimationClip.ToolThrowM },
        { "ToolThrow Wall", AnimationClip.ToolThrowWall },

        { "Recoil Twirl", AnimationClip.RecoilTwirl },
        { "DownSpikeBounce 1", AnimationClip.DownSpikeBounce1 },
        { "DownSpikeBounce 2", AnimationClip.DownSpikeBounce2 },
        
        { "Charge Up", AnimationClip.ChargeUp },
        { "Charge Up Air", AnimationClip.ChargeUpAir },
        { "Charge Up Bench", AnimationClip.ChargeUpBench },
        { "Charge Up Bench Silk", AnimationClip.ChargeUpBenchSilk },
        { "Charge Up Burst", AnimationClip.ChargeUpBurst },

        { "TurnWalk", AnimationClip.TurnWalk },
        { "Turn Back Three Quarter", AnimationClip.TurnBackThreeQuarter },
        { "Turn Back Three Quarter EndToIdle", AnimationClip.TurnBackThreeQuarterEndToIdle },
        { "Turn Head Backward", AnimationClip.TurnHeadBackward },
        { "Turn Head Forward", AnimationClip.TurnHeadForward },
        { "Turn Quick", AnimationClip.TurnQuick },
        { "TurnFromBG", AnimationClip.TurnFromBG },
        { "TurnFromBG Loop", AnimationClip.TurnFromBGLoop },
        { "TurnToBG", AnimationClip.TurnToBG },
        { "TurnToBG Loop", AnimationClip.TurnToBGLoop },
        { "TurnToChallengeIdle", AnimationClip.TurnToChallengeIdle },
        { "TurnToChallengeStrong", AnimationClip.TurnToChallengeStrong },
        { "TurnToChallengeTalk", AnimationClip.TurnToChallengeTalk },
        { "TurnToFG", AnimationClip.TurnToFG },
        { "TurnToIdle", AnimationClip.TurnToIdle },
        
        { "Walk", AnimationClip.Walk },
        { "Walk Q", AnimationClip.WalkQ },
        { "Look Up Half", AnimationClip.LookUpHalf },
        { "Look Up Half End", AnimationClip.LookUpHalfEnd },
        { "LookDown Slight", AnimationClip.LookDownSlight },
        { "LookDown Slight End", AnimationClip.LookDownSlightEnd },
        { "Look Down Talk", AnimationClip.LookDownTalk },
        { "Look Up Half Flinch", AnimationClip.LookUpHalfFlinch },
        { "Look Up Half Talk", AnimationClip.LookUpHalfTalk },
        { "Look Up Talk", AnimationClip.LookUpTalk },
        { "LookDown Updraft", AnimationClip.LookDownUpdraft },
        { "LookDown Windy", AnimationClip.LookDownWindy },
        { "LookDownEnd Updraft", AnimationClip.LookDownEndUpdraft },
        { "LookDownEnd Windy", AnimationClip.LookDownEndWindy },
        { "LookDownToIdle", AnimationClip.LookDownToIdle },
        { "LookingUp", AnimationClip.LookingUp },
        { "LookUp Updraft", AnimationClip.LookUpUpdraft },
        { "LookUp Windy", AnimationClip.LookUpWindy },
        { "LookUpEnd Updraft", AnimationClip.LookUpEndUpdraft },
        { "LookUpEnd Windy", AnimationClip.LookUpEndWindy },
        { "LookUpToIdle", AnimationClip.LookUpToIdle },

        { "Scuttle Start", AnimationClip.ScuttleStart },
        { "Scuttle Loop", AnimationClip.ScuttleLoop },
        { "Scuttle TurnToLoop", AnimationClip.ScuttleTurnToLoop },
        { "Scuttle End", AnimationClip.ScuttleEnd },
        { "Scuttle Fall", AnimationClip.ScuttleFall },
        { "Scuttle Vault", AnimationClip.ScuttleVault },
        { "Scuttle Jump", AnimationClip.ScuttleJump },
        { "Scuttle Climb", AnimationClip.ScuttleClimb },
        { "Scuttle Climb End", AnimationClip.ScuttleClimbEnd },
        
        { "Stun", AnimationClip.Stun },
        { "Recoil", AnimationClip.Recoil },
        
        { "Grab Escape", AnimationClip.GrabEscape },
        
        { "Idle Updraft", AnimationClip.IdleUpdraft },
        { "Updraft Antic", AnimationClip.UpdraftAntic },
        { "Updraft Antic DJ", AnimationClip.UpdraftAnticDJ },
        { "Updraft Rise", AnimationClip.UpdraftRise },
        { "Updraft Rise Turn", AnimationClip.UpdraftRiseTurn },
        { "Updraft End", AnimationClip.UpdraftEnd },
        { "Updraft Idle", AnimationClip.UpdraftIdle },
        { "Updraft Shoot", AnimationClip.UpdraftShoot },

        { "Prostrate Rise NoNeedle", AnimationClip.ProstrateRiseNoNeedle },
        { "Bind First", AnimationClip.BindFirst },
        { "BindBurst First", AnimationClip.BindBurstFirst },
        { "Thwip To Idle", AnimationClip.ThwipToIdle },
        { "GetUpToIdle", AnimationClip.GetUpToIdle },

        { "Weakened Stun", AnimationClip.WeakenedStun },
        { "Weak Rise To Idle", AnimationClip.WeakRiseToIdle },
        { "Weak Walk", AnimationClip.WeakWalk },
        { "Weak TryJumpAntic", AnimationClip.WeakTryJumpAntic },
        { "Weak TryAttack", AnimationClip.WeakTryAttack },
        { "Weakened StunEnd", AnimationClip.WeakenedStunEnd },
        { "Weak Fall", AnimationClip.WeakFall },
        { "Weak Flinch To Idle", AnimationClip.WeakFlinchToIdle },
        { "Weak Idle Look Up", AnimationClip.WeakIdleLookUp },
        { "Weak Walk Faster", AnimationClip.WeakWalkFaster },
        { "Weak Walk To Idle", AnimationClip.WeakWalkToIdle },


        { "Taunt Collapse1", AnimationClip.TauntCollapse1 },
        { "Taunt Collapse2", AnimationClip.TauntCollapse2 },
        { "Taunt CollapseHit", AnimationClip.TauntCollapseHit },
        { "Prostrate", AnimationClip.Prostrate },
        { "Prostrate Rise", AnimationClip.ProstrateRise },
        { "Prostrate NoNeedle", AnimationClip.ProstrateNoNeedle },
        { "Prostrate Rise Slow", AnimationClip.ProstrateRiseSlow },
        { "ProstrateRiseToKneel", AnimationClip.ProstrateRiseToKneel },
        { "ProstrateRiseToKneel NoLoop", AnimationClip.ProstrateRiseToKneelNoLoop },
        { "ProstrateRiseToWound", AnimationClip.ProstrateRiseToWound },

        { "Talking Standard", AnimationClip.TalkingStandard },
        { "Talking Backward", AnimationClip.TalkingBackward },

        { "Abyss Kneel", AnimationClip.AbyssKneel },
        { "Abyss Kneel Back Idle", AnimationClip.AbyssKneelBackIdle },
        { "Abyss Kneel Back Talk", AnimationClip.AbyssKneelBackTalk },
        { "Abyss Kneel Idle", AnimationClip.AbyssKneelIdle },
        { "Abyss Kneel to Stand", AnimationClip.AbyssKneeltoStand },
        { "Abyss Kneel Turn Back", AnimationClip.AbyssKneelTurnBack },
        { "Kneel To Prostrate", AnimationClip.KneelToProstrate },
        { "Kneeling", AnimationClip.Kneeling },

        { "Acid Death", AnimationClip.AcidDeath },
        { "Death", AnimationClip.Death },
        { "Death Final", AnimationClip.DeathFinal },
        { "Spike Death", AnimationClip.SpikeDeath },
        { "Spike Death Antic", AnimationClip.SpikeDeathAntic },

        { "Beastling Call Fail", AnimationClip.BeastlingCallFail },
        { "Beastling Call Fail Windy", AnimationClip.BeastlingCallFailWindy },
        { "Bellway Call", AnimationClip.BellwayCall },
        { "Fast Travel Call", AnimationClip.FastTravelCall },
        { "Fast Travel Child Arrive", AnimationClip.FastTravelChildArrive },
        { "Fast Travel Fail", AnimationClip.FastTravelFail },
        { "Fast Travel Leap", AnimationClip.FastTravelLeap },

        { "Collect Heart Piece", AnimationClip.CollectHeartPiece },
        { "Collect Heart Piece End", AnimationClip.CollectHeartPieceEnd },
        { "Collect Memory Orb", AnimationClip.CollectMemoryOrb },
        { "Collect Normal 1", AnimationClip.CollectNormal1 },
        { "Collect Normal 1 Q", AnimationClip.CollectNormal1Q },
        { "Collect Normal 2", AnimationClip.CollectNormal2 },
        { "Collect Normal 3", AnimationClip.CollectNormal3 },
        { "Collect Normal 3 Q", AnimationClip.CollectNormal3Q },
        { "Collect Silk Heart", AnimationClip.CollectSilkHeart },
        { "Collect Stand 1", AnimationClip.CollectStand1 },
        { "Collect Stand 2", AnimationClip.CollectStand2 },
        { "Collect Stand 3", AnimationClip.CollectStand3 },
        { "CollectToWound", AnimationClip.CollectToWound },
        { "DropToWounded", AnimationClip.DropToWounded },
        { "Blue Health Over Burst", AnimationClip.BlueHealthOverBurst },
        { "Crest Shrine Powerup Loop", AnimationClip.CrestShrinePowerupLoop },
        { "Hazard Respawn", AnimationClip.HazardRespawn },
        { "Respawn Wake", AnimationClip.RespawnWake },
        { "Pod Bounce", AnimationClip.PodBounce },
        { "Quick Charged", AnimationClip.QuickCharged },
        { "Quick Craft Silk", AnimationClip.QuickCraftSilk },
        { "Sprintmaster Low", AnimationClip.SprintmasterLow },
        { "Sprintmaster Start", AnimationClip.SprintmasterStart },

        { "Dress Flourish", AnimationClip.DressFlourish },
        { "Give Dress", AnimationClip.GiveDress },
        { "Give Dress Idle", AnimationClip.GiveDressIdle },
        { "Enter", AnimationClip.Enter },
        { "Exit", AnimationClip.Exit },
        { "Exit Door To Idle", AnimationClip.ExitDoorToIdle },
        { "Exit To Idle", AnimationClip.ExitToIdle },
        { "Wake", AnimationClip.Wake },
        { "Wake Up Ground", AnimationClip.WakeUpGround },

        { "Hurt Listen Down", AnimationClip.HurtListenDown },
        { "Hurt Listen Up", AnimationClip.HurtListenUp },
        { "Hurt Look Down", AnimationClip.HurtLookDown },
        { "Hurt Look Down Windy", AnimationClip.HurtLookDownWindy },
        { "Hurt Look Down Windy End", AnimationClip.HurtLookDownWindyEnd },
        { "Hurt Look Up", AnimationClip.HurtLookUp },
        { "Hurt Look Up End", AnimationClip.HurtLookUpEnd },
        { "Hurt Look Up Windy", AnimationClip.HurtLookUpWindy },
        { "Hurt Look Up Windy End", AnimationClip.HurtLookUpWindyEnd },
        { "Hurt Talk Down", AnimationClip.HurtTalkDown },
        { "Hurt Talk Up", AnimationClip.HurtTalkUp },
        { "Hurt Talk Up Windy", AnimationClip.HurtTalkUpWindy },
        { "Hurt To Idle", AnimationClip.HurtToIdle },

        { "Needle Fall", AnimationClip.NeedleFall },
        { "Needle Land", AnimationClip.NeedleLand },

        { "Ring Drop Impact", AnimationClip.RingDropImpact },
        { "Ring Eject", AnimationClip.RingEject },
        { "Ring Grab Hornet", AnimationClip.RingGrabHornet },
        { "Ring Grab Rail", AnimationClip.RingGrabRail },
        { "Ring Harpoon Connect", AnimationClip.RingHarpoonConnect },
        { "Ring Look Down", AnimationClip.RingLookDown },
        { "Ring Look Down End", AnimationClip.RingLookDownEnd },
        { "Ring Look Up", AnimationClip.RingLookUp },
        { "Ring Look Up End", AnimationClip.RingLookUpEnd },
        { "Ring Turn", AnimationClip.RingTurn },

        { "Roar Lock", AnimationClip.RoarLock },
        { "Roar To LookUp", AnimationClip.RoarToLookUp },

        { "Spa Surface Idle", AnimationClip.SpaSurfaceIdle },
        { "Spa Surface IdleToSwim", AnimationClip.SpaSurfaceIdleToSwim },
        { "Spa Surface In", AnimationClip.SpaSurfaceIn },
        { "Spa Surface InToIdle", AnimationClip.SpaSurfaceInToIdle },
        { "Spa Surface TurnToSwim", AnimationClip.SpaSurfaceTurnToSwim },

        { "Weaver Pray", AnimationClip.WeaverPray },
        { "Weaver Pray End", AnimationClip.WeaverPrayEnd },
        { "Weaver Pray Prepare", AnimationClip.WeaverPrayPrepare },
        { "Weaver Pray Prepare Front", AnimationClip.WeaverPrayPrepareFront },

        { "Wound", AnimationClip.Wound },
        { "Wound Double Strike", AnimationClip.WoundDoubleStrike },
        { "Wound Zap", AnimationClip.WoundZap },

        { "Witch Tentacles!", AnimationClip.WitchTentacles },
        { "Shaman Cancel", AnimationClip.ShamanCancel },
        { "Bind Fail Burst", AnimationClip.BindInterrupt }
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
        { AnimationClip.DownSpikeCharged, new DownSpike() }, // Architect Crest charged down slash
        { AnimationClip.V3DownSlash, new Slash(SlashBase.SlashType.Down) },
        { AnimationClip.DownSlash, new Slash(SlashBase.SlashType.Down) },
        { AnimationClip.DownSlashAlt, new Slash(SlashBase.SlashType.DownAlt) },
        { AnimationClip.SpinBall, new Slash(SlashBase.SlashType.Down) },
        { AnimationClip.DashAttackAntic, DashSlashAntic.Instance },
        { AnimationClip.DashAttackAntic1, DashSlashAntic.Instance },
        { AnimationClip.DashAttackAntic2, DashSlashAntic.Instance },
        { AnimationClip.DashAttack, new DashSlash(DashSlash.DashSlashType.Shared) },
        { AnimationClip.DashAttack1, new DashSlash(DashSlash.DashSlashType.Witch1) },
        { AnimationClip.DashAttack2, new DashSlash(DashSlash.DashSlashType.Witch2) },
        { AnimationClip.DashUpperAntic, DashSlashAntic.Instance },
        { AnimationClip.DashUpper, new DashSlashReaper() },
        { AnimationClip.WandererDashAttack, new Slash(SlashBase.SlashType.Dash) },
        { AnimationClip.WandererDashAttackAlt, new Slash(SlashBase.SlashType.DashAlt) },
        { AnimationClip.DashAttackCharge, DashSlashAntic.Instance },
        { AnimationClip.DashAttackSlash, new Slash(SlashBase.SlashType.Dash) },
        { AnimationClip.SlashCharged, new NeedleStrike(false) },
        { AnimationClip.SlashChargedLoop, new NeedleStrike(true) },
        { AnimationClip.NeedleArtDash, new NeedleStrike(false) },
        { AnimationClip.BindChargeGround, new Bind() },
        { AnimationClip.BindChargeGroundLand, new Bind { BindState = Bind.State.ShamanDoneFalling } },
        { AnimationClip.BindBurstGround, BindBurst.Instance },
        { AnimationClip.BindChargeHealBurst, BindBurst.Instance },
        { AnimationClip.BindBurstAir, BindBurst.Instance },
        { AnimationClip.RageBindBurst, BindBurst.Instance },
        { AnimationClip.Death, new Death() },

        // Silk Skills
        { AnimationClip.NeedleThrowThrowing, new SilkSpear() },
        { AnimationClip.AirSphereAttack, new ThreadStorm() },
        { AnimationClip.SilkCharge, new SharpDart() },
        { AnimationClip.SilkChargeZap, new SharpDart { Volt = true } },
        { AnimationClip.ParryStance, CrossStitch.StartingInstance },
        { AnimationClip.ParryStanceGround, CrossStitch.StartingInstance },
        { AnimationClip.ParryClash, new CrossStitch() },
        { AnimationClip.SilkBombAntic, new RuneRage { IsAntic = true } },
        { AnimationClip.SilkBossNeedleCast, new PaleNails { IsAntic = true } },
    };

    private static readonly Dictionary<AnimationClip, IAnimationEffect> SubAnimationEffects = new() {
        { AnimationClip.WitchTentacles, BindBurst.Instance },
        { AnimationClip.ShamanCancel, new Bind { BindState = Bind.State.ShamanCancel } },
        { AnimationClip.BindInterrupt, BindInterrupt.Instance },
        { AnimationClip.AirSphereRefresh, new ThreadStorm() },
        { AnimationClip.SilkBombLocations, new RuneRage() },
        { AnimationClip.SilkBossNeedleFire, new PaleNails() }
    };

    /// <summary>
    /// The net client for sending animation updates.
    /// </summary>
    private readonly NetClient _netClient;

    /// <summary>
    /// The player manager to get player objects.
    /// </summary>
    private readonly PlayerManager _playerManager;

    /// <summary>
    /// Copied instance of the player data to get positions for targeting of Silk Skills.
    /// </summary>
    private readonly Dictionary<ushort, ClientPlayerData> _playerData;

    /// <summary>
    /// Copied instance of the server settings for checking whether PvP, Teams, etc. are enabled.
    /// </summary>
    private ServerSettings _serverSettings;

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

    /// <summary>
    /// List of encoded positions for Rune Rage blasts. Used to gather encoded positions before sending effect info to
    /// server.
    /// </summary>
    private readonly List<byte> _runeRagePositions = [];

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
        PlayerManager playerManager,
        ServerSettings serverSettings,
        Dictionary<ushort, ClientPlayerData> playerData
    ) {
        _netClient = netClient;
        _playerManager = playerManager;
        _playerData = playerData;
        _serverSettings = serverSettings;

        // _chargedEffectStopwatch = new Stopwatch();
        // _chargedEndEffectStopwatch = new Stopwatch();
    }

    /// <summary>
    /// Initialize the animation manager by registering packet handlers and initializing animation effects.
    /// </summary>
    public void Initialize(ServerSettings serverSettings) {
        _serverSettings = serverSettings;

        // Set the server settings for all animation effects
        foreach (var effect in AnimationEffects.Values) {
            effect.SetServerSettings(serverSettings);
        }

        foreach (var effect in SubAnimationEffects.Values) {
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

        EventHooks.HeroControllerDie += OnDeath;

        // Register FSM hooks for certain bind actions
        HeroController.OnHeroInstanceSet += CreateHeroHooks;
        if (HeroController.SilentInstance != null) {
            CreateHeroHooks(HeroController.instance);
        }


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
    }

    /// <summary>
    /// Deregister the game hooks for the animation manager.
    /// </summary>
    public void DeregisterHooks() {
        SceneManager.activeSceneChanged -= OnSceneChange;

        HeroController.OnHeroInstanceSet -= CreateHeroHooks;
        FsmStateActionInjector.UninjectAll();
        // On.HeroAnimationController.Play -= HeroAnimationControllerOnPlay;
        // On.HeroAnimationController.PlayFromFrame -= HeroAnimationControllerOnPlayFromFrame;

        // On.tk2dSpriteAnimator.WarpClipToLocalTime -= Tk2dSpriteAnimatorOnWarpClipToLocalTime;
        // On.tk2dSpriteAnimator.ProcessEvents -= Tk2dSpriteAnimatorOnProcessEvents;

        // On.HeroController.CancelDash -= HeroControllerOnCancelDash;

        // ModHooks.HeroUpdateHook -= OnHeroUpdateHook;

        // On.HeroController.DieFromHazard -= HeroControllerOnDieFromHazard;
        // On.GameManager.HazardRespawn -= GameManagerOnHazardRespawn;

        // On.HeroController.RelinquishControl -= HeroControllerOnRelinquishControl;
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
        var crestType = _playerManager.GetPlayerCrestType(id);
        
        UpdatePlayerAnimation(id, clipId, frame, crestType);

        var animationClip = (AnimationClip) clipId;

        if (!AnimationEffects.TryGetValue(animationClip, out var animationEffect)) {
            SubAnimationEffects.TryGetValue(animationClip, out animationEffect);
        }

        if (animationEffect != null) {
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
            
            if (_debugLogAnimations) Logger.Info($"Playing animation effect for animation clip: {animationClip}, {animationEffect.GetType()}");

            animationEffect.Play(
                playerObject,
                crestType,
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
    /// <param name="crestType">The type of crest the player is using.</param>
    public void UpdatePlayerAnimation(ushort id, int clipId, int frame, CrestType crestType) {
        var playerObject = _playerManager.GetPlayerObject(id);
        if (!playerObject) {
            // Logger.Get().Warn(this, $"Tried to update animation, but there was not matching player object for ID {id}");
            return;
        }

        var animationClip = (AnimationClip) clipId;

        if (_debugLogAnimations) Logger.Info($"Received PlayerAnimationUpdate: {animationClip}");

        if (SubAnimationEffects.ContainsKey(animationClip)) {
            if (_debugLogAnimations) Logger.Info($"PlayerAnimationUpdate was sub-effect: {animationClip}");
            return;
        }

        if (!ClipEnumNames.ContainsSecond(animationClip)) {
            // This happens when we send custom clips, that can't be played by the sprite animator, so for now we
            // don't log it. This warning might be useful if we seem to be missing animations from the Knights
            // sprite animator.

            if (_debugLogAnimations) Logger.Warn($"Tried to update animation, but there was no entry for clip ID: {clipId}, enum: {animationClip}");
            return;
        }

        var clipName = ClipEnumNames[animationClip];
        if (clipName == null) {
            Logger.Warn($"Clip name was null after lookup in {nameof(ClipEnumNames)}");
            return;
        }

        if (_debugLogAnimations) Logger.Info($"  clipName: {clipName}");

        // Get the sprite animator and check whether this clip can be played before playing it
        var spriteAnimator = playerObject.GetComponent<tk2dSpriteAnimator>();

        var clip = spriteAnimator.GetClipByName(clipName);

        // Get the config groups for the used crest type, so we can find animation clips that should override the
        // normal animation library
        if (!AnimationUtil.GetConfigsFromCrestType(
            crestType,
            out var configGroup,
            out var overrideGroup
        )) {
            Logger.Warn($"Could not get configs from crest type: {crestType}");
            return;
        }

        var clipInOverrideGroupLib = false;

        if (overrideGroup != null) {
            if (AnimationUtil.TryFindClipInOverrideGroup(overrideGroup, clipName, out var overrideGroupClip)) {
                clip = overrideGroupClip;
                clipInOverrideGroupLib = true;
                
                if (_debugLogAnimations) Logger.Info("Found clip in override group's override animation library");
            }
        }

        if (configGroup != null && !clipInOverrideGroupLib) {
            if (AnimationUtil.TryFindClipInOverrideGroup(configGroup, clipName, out var configGroupClip)) {
                clip = configGroupClip;
                
                if (_debugLogAnimations) Logger.Info("Found clip in config group's override animation library");
            }
        }

        if (clip == null) {
            Logger.Warn("Could not find clip in normal library, config group's override library, or override group's override library");
            return;
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
        } else if (SubAnimationEffects.TryGetValue(animationClip, out var subEffect)) {
            var effectInfo = subEffect.GetEffectInfo();

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

    /// <summary>
    /// Creates hooks for the Witch Tentacles and Shaman Cancel states in
    /// the Bind fsm once the HeroController is ready.
    /// </summary>
    private void CreateHeroHooks(HeroController hc) {
        // Initialize warding bell FSM if it isn't already.
        // This fills it in with the template
        var bellFsm = HeroController.instance.bellBindFSM;
        if (!bellFsm.fsm.initialized) {
            bellFsm.Init();
        }

        // Find bind FSM
        var heroFsms = hc.GetComponents<PlayMakerFSM>();

        var bindFsm = heroFsms.FirstOrDefault(fsm => fsm.FsmName == "Bind");
        if (bindFsm != null) {
            // Find FSM states to inject
            var tentacles = bindFsm.GetState("Witch Tentancles!"); // no that's not a typo... at least on my end
            FsmStateActionInjector.Inject(tentacles, OnWitchTentacles, 4);
        
            var shamanCancel = bindFsm.GetState("Shaman Air Cancel");
            FsmStateActionInjector.Inject(shamanCancel, OnShamanCancel);

            var bindInterrupt = bindFsm.GetState("Remove Silk?");
            FsmStateActionInjector.Inject(bindInterrupt, OnBindInterrupt, 2);
        } else {
            Logger.Warn("Unable to find Bind FSM to hook.");
        }

        // Silk skill injections
        var silkSkillFsm = hc.silkSpecialFSM;
        if (silkSkillFsm == null) {
            Logger.Warn("Unable to find Silk Skill FSM to hook.");
            return;
        }

        // Thread Storm
        var threadStormExtend = silkSkillFsm.GetState("Extend");
        FsmStateActionInjector.Inject(threadStormExtend, OnThreadStormExtend);

        // Rune Rage
        var sonarBuildArray = silkSkillFsm.GetState("Build Enemy Array");
        FsmStateActionInjector.Inject(sonarBuildArray, OnBuildRuneRageArray);

        var blastEnemy = silkSkillFsm.GetState("Blast Enemy");
        FsmStateActionInjector.Inject(blastEnemy, OnRuneBlastEnemy, 4);

        var blastRandom = silkSkillFsm.GetState("Random Blasts");
        FsmStateActionInjector.Inject(blastRandom, OnRuneBlastRandom, 3);

        var blastFinished = silkSkillFsm.GetState("Silk Bomb Recover");
        FsmStateActionInjector.Inject(blastFinished, OnRuneBlastFinished);

        // Pale Nails
        var nailObject = silkSkillFsm.GetAction<SpawnObjectFromGlobalPool>("BossNeedle Cast", 5)?.gameObject.Value;
        var nailFsm = nailObject?.LocateMyFSM("Control");
        if (nailObject == null || nailFsm == null) {
            Logger.Warn("Unable to find Pale Nail FSM to hook.");
            return;
        }

        // Find all existing pale nails
        List<GameObject> nails = [
            nailObject
        ];

        if (ObjectPool.instance.pooledObjects.TryGetValue(nailObject, out var globalPoolNails)) {
            nails.AddRange(globalPoolNails);
        }

        // Add injector components to all current instances of nails
        foreach (var nail in nails) {
            var injector = nail.AddComponent<FsmActionInjectorComponent>();
            List<FsmActionInjectorComponent.Injection> injections = [
                new() {
                    fsmName = nailFsm.FsmName,
                    fsmStateName = "Follow HeroFacingLeft",
                    actionIndex = 12,
                    Hook = OnPaleNailAttackCheck,
                    hookName = "Nail Target"
                },

                new() {
                    fsmName = nailFsm.FsmName,
                    fsmStateName = "Follow HeroFacingRight",
                    actionIndex = 12,
                    Hook = OnPaleNailAttackCheck,
                    hookName = "Nail Target"
                },

                new() {
                    fsmName = nailFsm.FsmName,
                    fsmStateName = "Fire Antic",
                    actionIndex = 0,
                    Hook = OnPaleNailFire,
                    hookName = "Nail Fire"
                }
            ];

            injector.SetInjections(injections);
        }
    }

    /// <summary>
    /// Animation subanimation hook for the Witch Tentacles FSM state.
    /// </summary>
    private void OnWitchTentacles(PlayMakerFSM fsm) {
        var dummyClip = new tk2dSpriteAnimationClip {
            name = "Witch Tentacles!",
            wrapMode = tk2dSpriteAnimationClip.WrapMode.Once
        };
        OnAnimationEvent(dummyClip);
    }

    /// <summary>
    /// Animation subanimation hook for the Shaman Air Cancel FSM state.
    /// </summary>
    private void OnShamanCancel(PlayMakerFSM fsm) {
        var dummyClip = new tk2dSpriteAnimationClip {
            name = "Shaman Cancel",
            wrapMode = tk2dSpriteAnimationClip.WrapMode.Once
        };
        OnAnimationEvent(dummyClip);
    }

    /// <summary>
    /// Animation subanimation hook for interrupted binds.
    /// </summary>
    private void OnBindInterrupt(PlayMakerFSM fsm) {
        var dummyClip = new tk2dSpriteAnimationClip {
            name = "Bind Fail Burst",
            wrapMode = tk2dSpriteAnimationClip.WrapMode.Once
        };
        OnAnimationEvent(dummyClip);
    }

    /// <summary>
    /// Animation subanimation hook for extending a thread storm.
    /// </summary>
    private void OnThreadStormExtend(PlayMakerFSM fsm) {
        var effectInfo = BaseSilkSkill.GetEffectFlags();
        _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.AirSphereRefresh, 0, effectInfo);
    }

    /// <summary>
    /// Influences Rune Rage to be able to target players that can be attacked.
    /// </summary>
    private void OnBuildRuneRageArray(PlayMakerFSM fsm) {
        // If we are not connected, there is nothing to send to
        if (!_netClient.IsConnected) {
            return;
        }

        _runeRagePositions.Clear();

        // Find tracker for Rune Rage
        var sonarObject = HeroController.instance.gameObject
            .FindGameObjectInChildren("Special Attacks")?
            .FindGameObjectInChildren("Sonar Enemy Tracker");

        if (sonarObject == null) return;

        // Get needed components
        var sonar = sonarObject.GetComponent<TrackTriggerObjects>();
        if (sonar == null) return;

        var sonarCollider = sonarObject.GetComponent<CircleCollider2D>();
        if (sonarCollider == null) return;

        // Remove any old player objects
        sonar.Refresh();

        // No need to target if pvp is off
        if (!_serverSettings.IsPvpEnabled) {
            return;
        }

        var radius = sonarCollider.radius * sonar.transform.GetScaleX();

        var inSonar = sonar.insideObjectsList;

        // Find all players within sonar
        foreach (var player in _playerData.Values) {
            if (!player.IsInLocalScene || !player.PlayerObject) {
                continue;
            }

            // Don't bother to target players on same team
            if (_serverSettings.TeamsEnabled && player.Team == _playerManager.LocalPlayerTeam && player.Team != Team.None) {
                inSonar.Remove(player.PlayerObject);
                continue;
            }

            var collider = player.PlayerObject.GetComponent<BoxCollider2D>();
            if (!collider) continue;

            // Determine if the player is within the sonar circle
            var closestPlayerPoint = collider.ClosestPoint(sonarCollider.transform.position);
            var distanceFromCenter = Vector2.Distance(closestPlayerPoint, sonarCollider.transform.position);

            if (distanceFromCenter <= radius) {
                inSonar.AddIfNotPresent(player.PlayerObject);
            } else {
                inSonar.Remove(player.PlayerObject);
            }
        }
    }

    /// <summary>
    /// Intercepts the spawn locations for targeted Rune Rages.
    /// </summary>
    private void OnRuneBlastEnemy(PlayMakerFSM fsm) {
        // At this point the rune cluster has been created with a position and a given offset from the object.
        // This position is relative to the player.
        var spawnPosition = fsm.FsmVariables.FindFsmVector3("Shift Pos");
        if (spawnPosition != null) {
            var position = RuneRage.EncodeRunePosition(spawnPosition.Value + HeroController.instance.transform.position);
            _runeRagePositions.AddRange(position);
        }
    }

    /// <summary>
    /// Intercepts the spawn locations for random Rune Rages.
    /// </summary>
    private void OnRuneBlastRandom(PlayMakerFSM fsm) {
        // If there aren't enough targets, rune rage will spawn up to 3 other blasts
        var spawnRadial = fsm.GetFirstAction<SpawnRandomObjectsRadialRandom>("Random Blasts");
        // Get the positions of spawned blasts
        var positions = spawnRadial.tempPosStore;

        // Add to collection of positions
        foreach (var position in positions) {
            var encodedPosition = RuneRage.EncodeRunePosition(position);
            _runeRagePositions.AddRange(encodedPosition);
        }
    }

    /// <summary>
    /// Animation hook to send Rune Rage positions.
    /// </summary>
    private void OnRuneBlastFinished(PlayMakerFSM fsm) {
        var effectInfo = BaseSilkSkill.GetEffectFlags().ToList();
        effectInfo.AddRange(_runeRagePositions);

        _runeRagePositions.Clear();
        _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.SilkBombLocations, 0, effectInfo.ToArray());
    }

    /// <summary>
    /// Influences Pail Nails to be able to target players that can be attacked.
    /// </summary>
    private void OnPaleNailAttackCheck(PlayMakerFSM fsm) {
        // If we are not connected, there is nothing to send to
        if (!_netClient.IsConnected) {
            return;
        }

        var sonarObject = fsm.gameObject;

        // Get needed components
        var sonar = sonarObject.GetComponentInChildren<TrackTriggerObjectsLineOfSight>();
        if (sonar == null) return;

        var sonarCollider = sonarObject.GetComponentInChildren<CircleCollider2D>();
        if (sonarCollider == null) return;

        // Remove any old player objects
        sonar.Refresh();

        // Don't bother targeting if PvP is off
        if (!_serverSettings.IsPvpEnabled) {
            return;
        }

        var radius = sonarCollider.radius * sonar.transform.GetScaleX();
        var inSonar = sonar.insideObjectsList;

        // Find all players within sonar
        foreach (var player in _playerData.Values) {
            if (!player.IsInLocalScene || !player.PlayerObject) {
                continue;
            }

            // Don't bother to target players on same team
            if (_serverSettings.TeamsEnabled && player.Team == _playerManager.LocalPlayerTeam && player.Team != Team.None) {
                continue;
            }

            var collider = player.PlayerObject.GetComponent<BoxCollider2D>();
            if (!collider) continue;

            // Determine if the player is within the sonar circle and is not obstructed
            var closestPlayerPoint = collider.ClosestPoint(sonarCollider.transform.position);
            var distanceFromCenter = Vector2.Distance(closestPlayerPoint, sonarCollider.transform.position);

            if (distanceFromCenter <= radius && sonar.IsCounted(collider.gameObject)) {
                inSonar.AddIfNotPresent(player.PlayerObject);
            }
        }
    }

    /// <summary>
    /// Sends an update to fire a set of nails at a specific target.
    /// </summary>
    private void OnPaleNailFire(PlayMakerFSM fsm) {
        // Only get info from one needle position (the first one)
        var needleOffset = fsm.FsmVariables.GetFsmVector3("Offset");
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (needleOffset.Value.x != 3) return;

        // Don't send if no target, it'll go off on its own.
        var target = fsm.FsmVariables.FindFsmGameObject("Target").Value;
        if (target == null) {
            return;
        }

        // Send nail target info
        var effectInfo = PaleNails.EncodeTargetInfo(target);
        _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.SilkBossNeedleFire, 0, effectInfo);
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

        var allowedClipNames = new[] { "" };

        for (var i = start + direction; i != num; i += direction) {
            if (i >= frames.Length || i < 0) {
                Logger.Warn("tk2dSpriteAnimator ProcessEvents index out of bounds!");
                continue;
            }

            if (i != 0 && !frames[i].triggerEvent) {
                continue;
            }

            // if (!allowedClipNames.Contains(self.CurrentClip.name)) {
            //     continue;
            // }
        
            if (_debugLogAnimations) Logger.Info($"OnAnimationEvent from tk2dSpriteAnimatorOnProcessEvents: {self.CurrentClip.name}, conditions: {i}, {frames[i].triggerEvent}");
            // OnAnimationEvent(self.CurrentClip);
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

    // /// <summary>
    // /// Callback method for when the local player dies.
    // /// </summary>
    private void OnDeath(bool nonLethal, bool frostDeath) {
        // If we are not connected, there is nothing to send to
        if (!_netClient.IsConnected) {
            return;
        }

        Logger.Debug("Client has died, sending PlayerDeath data");

        // Let the server know that we have died
        byte[] effectInfo = [
            (byte)(frostDeath ? 1 : 0)
        ];
        _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.Death, 0, effectInfo);
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

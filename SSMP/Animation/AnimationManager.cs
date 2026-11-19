using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker.Actions;
using SSMP.Animation.Effects;
using SSMP.Animation.Effects.Movement;
using SSMP.Animation.Effects.SilkSkills;
using SSMP.Animation.Effects.Tools;
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
using ClipWrapMode = tk2dSpriteAnimationClip.WrapMode;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Animation;

/// <summary>
/// Synchronizes player animations between clients: sends the local player's animation clips and effects to the
/// server, and plays received clips and effects on remote player objects.
/// </summary>
internal class AnimationManager {
    /// <summary>
    /// Define this compilation symbol to log every animation event. Calls to <see cref="LogTrace"/> are removed
    /// entirely by the compiler otherwise, including evaluation of their interpolated strings.
    /// </summary>
    private const string TraceSymbol = "SSMP_TRACE_ANIMATIONS";

    /// <summary>
    /// The PlayMaker event the hero receives when resting on a bench.
    /// </summary>
    private const string BenchRestEvent = "BENCHREST START";

    /// <summary>
    /// Looping animations that are re-sent on every loop, because their effect must be transmitted each time.
    /// </summary>
    private static readonly HashSet<string> AllowedLoopAnimations = ["Airborne", "Wall Slide"];


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
        { "Idle Backward", AnimationClip.IdleBackward },
        { "Idle BG", AnimationClip.IdleBG },
        { "Idle Hurt", AnimationClip.IdleHurt },
        { "Idle Hurt Listen", AnimationClip.IdleHurtListen },
        { "Idle Hurt Listen Backward", AnimationClip.IdleHurtListenBackward },
        { "Idle Hurt Listen Windy", AnimationClip.IdleHurtListenWindy },
        { "Idle Hurt NoNeedle", AnimationClip.IdleHurtNoNeedle },
        { "Idle Hurt Talk", AnimationClip.IdleHurtTalk },
        { "Idle Hurt Talk Backward", AnimationClip.IdleHurtTalkBackward },
        { "Idle Hurt Talk Turn Backward", AnimationClip.IdleHurtTalkTurnBackward },
        { "Idle Hurt Talk Turn Forward", AnimationClip.IdleHurtTalkTurnForward },
        { "Idle Hurt Talk Windy", AnimationClip.IdleHurtTalkWindy },
        { "Idle Hurt Windy", AnimationClip.IdleHurtWindy },
        { "Idle Slap", AnimationClip.IdleSlap },
        { "Idle To Run Short", AnimationClip.IdleToRunShort },
        { "Idle To Run Weak", AnimationClip.IdleToRunWeak },
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

        { "Bench", AnimationClip.Bench },

        { "Witch Tentacles!", AnimationClip.WitchTentacles },
        { "Shaman Cancel", AnimationClip.ShamanCancel },
        { "Bind Fail Burst", AnimationClip.BindInterrupt },
        { "Tool Magnetite Dice", AnimationClip.ToolMagnetiteDice },
        { "Tool Flea Brew", AnimationClip.ToolFleaBrew },
        { "Tool Fractured Mask", AnimationClip.ToolFracturedMask },
        { "Tool Magma Bell", AnimationClip.ToolMagmaBell },
        { "Tool Straight Pin ", AnimationClip.ToolStraightPin },
        { "Tool Threefold Pin", AnimationClip.ToolThreefoldPin },
        { "Tool Long Pin", AnimationClip.ToolLongpin },
        { "Tool Tacks", AnimationClip.ToolTacks }
    };

    #region Effect Tables

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
        { AnimationClip.DoubleJump, new FaydownCloak() },
        { AnimationClip.UmbrellaInflate, new DriftersCloak() },

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

    /// <summary>
    /// Effects for clips that have no sprite animation of their own and are raised by FSM hooks or tool events.
    /// </summary>
    private static readonly Dictionary<AnimationClip, IAnimationEffect> SubAnimationEffects = new() {
        { AnimationClip.WitchTentacles, BindBurst.Instance },
        { AnimationClip.ShamanCancel, new Bind { BindState = Bind.State.ShamanCancel } },
        { AnimationClip.BindInterrupt, BindInterrupt.Instance },
        { AnimationClip.Bench, new Bench() },

        // Silk Skills
        { AnimationClip.AirSphereRefresh, new ThreadStorm() },
        { AnimationClip.SilkBombLocations, new RuneRage() },
        { AnimationClip.SilkBossNeedleFire, new PaleNails() },

        // Tools
        { AnimationClip.ToolMagnetiteDice, new MagnetiteDice() },
        { AnimationClip.ToolFleaBrew, FleaBrew.Instance },
        { AnimationClip.ToolFracturedMask, new FracturedMask() },
        { AnimationClip.ToolMagmaBell, new MagmaBell() },
        { AnimationClip.ToolStraightPin, new StraightPin() },
        { AnimationClip.ToolThreefoldPin, new ThreefoldPin() },
        { AnimationClip.ToolLongpin, new Longpin() },
        { AnimationClip.ToolTacks, new Tacks() }
    };

    #endregion

    private readonly NetClient _netClient;
    private readonly PlayerManager _playerManager;

    /// <summary>
    /// Shared player data, used to find positions when targeting players with Silk Skills.
    /// </summary>
    private readonly Dictionary<ushort, ClientPlayerData> _playerData;

    /// <summary>
    /// Server settings, used to check whether PvP, teams, etc. are enabled.
    /// </summary>
    private ServerSettings _serverSettings;

    /// <summary>
    /// The local hero's sprite animator, cached because the sprite animator hooks fire for every animator in the
    /// scene and must reject non-hero animators cheaply.
    /// </summary>
    private tk2dSpriteAnimator? _heroAnimator;

    /// <summary>
    /// Name of the last clip sent, used to avoid re-sending a looping clip every loop.
    /// </summary>
    private string? _lastAnimationClip;

    /// <summary>
    /// Whether to stop sending animations until the scene has changed.
    /// </summary>
    private bool _stopSendingAnimationUntilSceneChange;

    /// <summary>
    /// Encoded Rune Rage blast positions, gathered across several FSM states before being sent in one update.
    /// </summary>
    private readonly List<byte> _runeRagePositions = [];

    public AnimationManager(
        NetClient netClient,
        PlayerManager playerManager,
        ServerSettings serverSettings,
        Dictionary<ushort, ClientPlayerData> playerData
    ) {
        _netClient = netClient;
        _playerManager = playerManager;
        _serverSettings = serverSettings;
        _playerData = playerData;
    }

    /// <summary>
    /// Pass the server settings to the manager and all animation effects.
    /// </summary>
    public void Initialize(ServerSettings serverSettings) {
        _serverSettings = serverSettings;

        foreach (var effect in AnimationEffects.Values.Concat(SubAnimationEffects.Values)) {
            effect.SetServerSettings(serverSettings);
        }
    }

    #region Hook Lifecycle

    /// <summary>
    /// Register the game hooks for the animation manager.
    /// </summary>
    public void RegisterHooks() {
        SceneManager.activeSceneChanged += OnSceneChange;
        EventHooks.SpriteAnimatorWarpClipToLocalTime += OnSpriteAnimatorWarpClipToLocalTime;
        EventHooks.HeroControllerDie += OnDeath;
        EventHooks.UseLavaBell += OnMagmaBell;

        HeroController.OnHeroInstanceSet += CreateHeroHooks;
        var hero = HeroController.SilentInstance;
        if (hero != null) {
            CreateHeroHooks(hero);
        }
    }

    /// <summary>
    /// Deregister the game hooks for the animation manager.
    /// </summary>
    public void DeregisterHooks() {
        SceneManager.activeSceneChanged -= OnSceneChange;
        EventHooks.SpriteAnimatorWarpClipToLocalTime -= OnSpriteAnimatorWarpClipToLocalTime;
        EventHooks.HeroControllerDie -= OnDeath;
        EventHooks.UseLavaBell -= OnMagmaBell;

        HeroController.OnHeroInstanceSet -= CreateHeroHooks;
        FsmStateActionInjector.UninjectAll();
        MagnetiteDice.Unhook();
        ToolItemManager.BoundAttackToolUsed -= AttackToolUsed;

        var benchRegister = FindBenchEventRegister(HeroController.SilentInstance);
        if (benchRegister) {
            benchRegister.ReceivedEvent -= OnBench;
        }

        _heroAnimator = null;
    }

    /// <summary>
    /// Hook the FSMs and events of a hero instance. Called whenever the hero instance is (re)created.
    /// </summary>
    private void CreateHeroHooks(HeroController hero) {
        _heroAnimator = hero.GetComponent<tk2dSpriteAnimator>();

        // The warding bell FSM is filled in from its template on init, so make sure that has happened
        var bellFsm = hero.bellBindFSM;
        if (!bellFsm.fsm.initialized) {
            bellFsm.Init();
        }

        CreateSkillHooks(hero);
        CreateToolHooks(hero);
        CreateBindHooks(hero);

        var benchRegister = FindBenchEventRegister(hero);
        if (benchRegister) {
            benchRegister.ReceivedEvent += OnBench;
        }
    }

    /// <summary>
    /// Find the event register on the hero that receives the bench rest event.
    /// </summary>
    /// <param name="hero">The hero to search, or null.</param>
    /// <returns>The event register, or null if the hero is null or has none.</returns>
    private static EventRegister? FindBenchEventRegister(HeroController? hero) =>
        hero == null
            ? null
            : hero.GetComponents<EventRegister>().FirstOrDefault(r => r.SubscribedEvent == BenchRestEvent);

    /// <summary>
    /// Callback for when the scene changes. Resumes sending animations that entering a building paused.
    /// </summary>
    /// <param name="oldScene">The scene that was active.</param>
    /// <param name="newScene">The scene that is now active.</param>
    private void OnSceneChange(Scene oldScene, Scene newScene) => _stopSendingAnimationUntilSceneChange = false;

    #endregion

    #region Receiving

    /// <summary>
    /// Callback for a received player animation update. Plays the clip and its effect, if any, on the player object.
    /// </summary>
    /// <param name="id">The ID of the player.</param>
    /// <param name="clipId">The ID of the animation clip.</param>
    /// <param name="frame">The frame that the animation should play from.</param>
    /// <param name="effectInfo">A byte array containing effect info for the animation.</param>
    public void OnPlayerAnimationUpdate(ushort id, int clipId, int frame, byte[]? effectInfo) {
        var playerObject = _playerManager.GetPlayerObject(id);
        if (!playerObject) {
            return;
        }

        var crestType = _playerManager.GetPlayerCrestType(id);
        var animationClip = (AnimationClip) clipId;

        PlayClip(playerObject, animationClip, frame, crestType);

        if (FindEffect(animationClip) is not { } effect) {
            return;
        }

        if (effect is DamageAnimationEffect damageEffect) {
            damageEffect.SetShouldDoDamage(CanDamageLocalPlayer(id));
        }

        LogTrace($"Playing animation effect for animation clip: {animationClip}, {effect.GetType()}");
        effect.Play(playerObject, crestType, effectInfo);
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
        if (playerObject) {
            PlayClip(playerObject, (AnimationClip) clipId, frame, crestType);
        }
    }

    /// <summary>
    /// Play a clip on a player's sprite animator, preferring the crest's override libraries over the base library.
    /// </summary>
    private static void PlayClip(GameObject playerObject, AnimationClip animationClip, int frame, CrestType crestType) {
        LogTrace($"Received PlayerAnimationUpdate: {animationClip}");

        // Sub-animations only carry an effect; there is no sprite clip to play
        if (SubAnimationEffects.ContainsKey(animationClip)) {
            return;
        }

        // Custom clips are expected here: they exist for their effect and the sprite animator can't play them
        if (ClipEnumNames[animationClip] is not { } clipName) {
            LogTrace($"No clip name for clip: {animationClip}");
            return;
        }

        if (!AnimationUtil.GetConfigsFromCrestType(crestType, out var configGroup, out var overrideGroup)) {
            Logger.Warn($"Could not get configs from crest type: {crestType}");
            return;
        }

        var spriteAnimator = playerObject.GetComponent<tk2dSpriteAnimator>();

        // Precedence: override group's library, then config group's library, then the base library
        tk2dSpriteAnimationClip? clip = null;
        if (overrideGroup != null &&
            AnimationUtil.TryFindClipInOverrideGroup(overrideGroup, clipName, out var overrideClip)) {
            clip = overrideClip;
        } else if (configGroup != null &&
                   AnimationUtil.TryFindClipInOverrideGroup(configGroup, clipName, out var configClip)) {
            clip = configClip;
        }

        clip ??= spriteAnimator.GetClipByName(clipName);

        if (clip == null) {
            Logger.Warn($"Could not find clip '{clipName}' in the base library or the crest's override libraries");
            return;
        }

        LogTrace($"  playing clip: {clipName}");
        spriteAnimator.PlayFromFrame(clip, frame);
    }

    /// <summary>
    /// Teammates can't damage each other; a player without a team can damage and be damaged by anyone.
    /// </summary>
    private bool CanDamageLocalPlayer(ushort id) {
        var localTeam = _playerManager.LocalPlayerTeam;
        var otherTeam = _playerManager.GetPlayerTeam(id);

        return otherTeam != localTeam || otherTeam == Team.None || localTeam == Team.None;
    }

    /// <summary>
    /// Callback for when a player leaves the scene. Destroys leftover effect objects.
    /// </summary>
    public void OnPlayerLeaveScene(ClientPlayerData player) => Tacks.DestroyPlayerTacks(player.PlayerObject);

    #endregion

    #region Sending

    /// <summary>
    /// Get the AnimationClip enum value for the currently playing animation of the local player.
    /// </summary>
    /// <returns>The clip, or <c>default</c> if the current clip is unknown.</returns>
    public static AnimationClip GetCurrentAnimationClip() {
        var clipName = HeroController.instance.GetComponent<tk2dSpriteAnimator>().CurrentClip.name;

        return ClipEnumNames.ContainsFirst(clipName) ? ClipEnumNames[clipName] : default;
    }

    /// <summary>
    /// Hook on tk2dSpriteAnimator#WarpClipToLocalTime, which fires the animation events of clips. Used to detect
    /// when the local hero starts playing a clip.
    /// </summary>
    private void OnSpriteAnimatorWarpClipToLocalTime(
        tk2dSpriteAnimator animator,
        tk2dSpriteAnimationClip clip,
        float time
    ) {
        // Fires for every sprite animator in the scene, so reject non-hero animators first and as cheaply as possible
        if (!ReferenceEquals(animator, _heroAnimator)) {
            return;
        }

        var frames = clip.frames;
        if (frames.Length == 0) {
            return;
        }

        var index = (int) animator.clipTime % frames.Length;
        if (index == 0 || frames[index].triggerEvent || AllowedLoopAnimations.Contains(clip.name)) {
            LogTrace($"Animation event from WarpClipToLocalTime: {clip.name}, frame index: {index}");
            OnAnimationEvent(clip);
        }
    }

    /// <summary>
    /// Send a clip played by the local hero (or raised synthetically by an FSM hook) to the server.
    /// </summary>
    private void OnAnimationEvent(tk2dSpriteAnimationClip clip) {
        var clipName = clip.name;
        LogTrace($"Animation event with name: {clipName}");

        if (!_netClient.IsConnected || _stopSendingAnimationUntilSceneChange) {
            return;
        }

        // A looping clip is sent once when it starts, unless its effect must be re-sent each loop
        var isRepeatedLoop = clipName == _lastAnimationClip
                             && clip.wrapMode != ClipWrapMode.Once
                             && !AllowedLoopAnimations.Contains(clipName);
        if (isRepeatedLoop) {
            return;
        }

        if (clip.wrapMode is not (ClipWrapMode.Loop or ClipWrapMode.LoopSection or ClipWrapMode.Once)) {
            return;
        }

        // Entering a building starts a scene transition, so stop sending until the new scene is active
        // TODO: exiting a building has the same issue, but that is less trivial to solve
        if (clipName == "Enter") {
            _stopSendingAnimationUntilSceneChange = true;
        }

        if (!ClipEnumNames.ContainsFirst(clipName)) {
            Logger.Warn($"Player sprite animator played unknown clip, name: {clipName}");
            return;
        }

        var animationClip = ClipEnumNames[clipName];

        LogTrace($"  Sending animation: {animationClip}");
        _netClient.UpdateManager.UpdatePlayerAnimation(animationClip, 0, FindEffect(animationClip)?.GetEffectInfo());

        _lastAnimationClip = clipName;
    }

    /// <summary>
    /// Raise an animation event for a clip that has no sprite animation, so it goes through the normal send path.
    /// </summary>
    private void RaiseSyntheticClip(string clipName) => OnAnimationEvent(
        new tk2dSpriteAnimationClip {
            name = clipName,
            wrapMode = ClipWrapMode.Once
        }
    );

    /// <summary>
    /// Send the given animation clip to the server if the client is connected.
    /// </summary>
    /// <param name="clip">The animation clip to send.</param>
    /// <param name="effectInfo">Additional info for the effect of the clip, or null if there is none.</param>
    private void SendIfConnected(AnimationClip clip, byte[]? effectInfo = null) {
        if (_netClient.IsConnected) {
            _netClient.UpdateManager.UpdatePlayerAnimation(clip, 0, effectInfo);
        }
    }

    /// <summary>
    /// Find the effect of the given animation clip in both the effects and the sub-animation effects.
    /// </summary>
    /// <param name="clip">The animation clip to find the effect for.</param>
    /// <returns>The effect, or null if the clip has none.</returns>
    private static IAnimationEffect? FindEffect(AnimationClip clip) =>
        AnimationEffects.TryGetValue(clip, out var effect) || SubAnimationEffects.TryGetValue(clip, out effect)
            ? effect
            : null;

    /// <summary>
    /// Callback for when the local player dies.
    /// </summary>
    private void OnDeath(bool nonLethal, bool frostDeath) {
        if (!_netClient.IsConnected) {
            return;
        }

        Logger.Debug("Client has died, sending PlayerDeath data");
        _netClient.UpdateManager.UpdatePlayerAnimation(AnimationClip.Death, 0, [(byte) (frostDeath ? 1 : 0)]);
    }

    /// <summary>
    /// Callback for when the local player rests on a bench.
    /// </summary>
    private void OnBench() => SendIfConnected(AnimationClip.Bench);

    #endregion

    #region Bind Hooks

    /// <summary>
    /// Inject the actions into the Bind FSM of the hero that send the bind animations without a sprite clip.
    /// </summary>
    /// <param name="hero">The hero whose Bind FSM is hooked.</param>
    private void CreateBindHooks(HeroController hero) {
        var bindFsm = hero.GetComponents<PlayMakerFSM>().FirstOrDefault(fsm => fsm.FsmName == "Bind");
        if (bindFsm == null) {
            Logger.Warn("Unable to find Bind FSM to hook.");
            return;
        }

        // "Tentancles" is the game's own spelling of the state name
        FsmStateActionInjector.Inject(bindFsm.GetState("Witch Tentancles!"), OnWitchTentacles, 4);
        FsmStateActionInjector.Inject(bindFsm.GetState("Shaman Air Cancel"), OnShamanCancel);
        FsmStateActionInjector.Inject(bindFsm.GetState("Remove Silk?"), OnBindInterrupt, 2);
    }

    /// <summary>
    /// Callback for when the Witch crest bind spawns its tentacles.
    /// </summary>
    /// <param name="fsm">The FSM that ran the injected action.</param>
    private void OnWitchTentacles(PlayMakerFSM fsm) => RaiseSyntheticClip("Witch Tentacles!");

    /// <summary>
    /// Callback for when the Shaman crest bind is cancelled in the air.
    /// </summary>
    /// <param name="fsm">The FSM that ran the injected action.</param>
    private void OnShamanCancel(PlayMakerFSM fsm) => RaiseSyntheticClip("Shaman Cancel");

    /// <summary>
    /// Callback for when a bind is interrupted before it finishes.
    /// </summary>
    /// <param name="fsm">The FSM that ran the injected action.</param>
    private void OnBindInterrupt(PlayMakerFSM fsm) => RaiseSyntheticClip("Bind Fail Burst");

    #endregion

    #region Silk Skill Hooks

    /// <summary>
    /// Inject the actions into the silk skill FSMs of the hero that send the silk skill animations.
    /// </summary>
    /// <param name="hero">The hero whose silk skill FSMs are hooked.</param>
    private void CreateSkillHooks(HeroController hero) {
        var silkSkillFsm = hero.silkSpecialFSM;
        if (silkSkillFsm == null) {
            Logger.Warn("Unable to find Silk Skill FSM to hook.");
            return;
        }

        // Thread Storm
        FsmStateActionInjector.Inject(silkSkillFsm.GetState("Extend"), OnThreadStormExtend);

        // Rune Rage
        FsmStateActionInjector.Inject(silkSkillFsm.GetState("Build Enemy Array"), OnBuildRuneRageArray);
        FsmStateActionInjector.Inject(silkSkillFsm.GetState("Blast Enemy"), OnRuneBlastEnemy, 4);
        FsmStateActionInjector.Inject(silkSkillFsm.GetState("Random Blasts"), OnRuneBlastRandom, 3);
        FsmStateActionInjector.Inject(silkSkillFsm.GetState("Silk Bomb Recover"), OnRuneBlastFinished);

        // Pale Nails: each nail is a pooled object with its own FSM, so inject into every existing instance
        var nailObject = silkSkillFsm.GetAction<SpawnObjectFromGlobalPool>("BossNeedle Cast", 5)?.gameObject.Value;
        var nailFsm = nailObject?.LocateMyFSM("Control");
        if (nailObject == null || nailFsm == null) {
            Logger.Warn("Unable to find Pale Nail FSM to hook.");
            return;
        }

        List<GameObject> nails = [nailObject];
        if (ObjectPool.instance.pooledObjects.TryGetValue(nailObject, out var pooledNails)) {
            nails.AddRange(pooledNails);
        }

        var nailFsmName = nailFsm.FsmName;
        foreach (var nail in nails) {
            List<FsmActionInjectorComponent.Injection> injections = [
                new() {
                    fsmName = nailFsmName,
                    fsmStateName = "Follow HeroFacingLeft",
                    actionIndex = 12,
                    Hook = OnPaleNailAttackCheck,
                    hookName = "Nail Target"
                },
                new() {
                    fsmName = nailFsmName,
                    fsmStateName = "Follow HeroFacingRight",
                    actionIndex = 12,
                    Hook = OnPaleNailAttackCheck,
                    hookName = "Nail Target"
                },
                new() {
                    fsmName = nailFsmName,
                    fsmStateName = "Fire Antic",
                    actionIndex = 0,
                    Hook = OnPaleNailFire,
                    hookName = "Nail Fire"
                }
            ];

            nail.AddComponent<FsmActionInjectorComponent>().SetInjections(injections);
        }
    }

    /// <summary>
    /// Callback for when Thread Storm is extended while it is active.
    /// </summary>
    /// <param name="fsm">The FSM that ran the injected action.</param>
    private void OnThreadStormExtend(PlayMakerFSM fsm) =>
        SendIfConnected(AnimationClip.AirSphereRefresh, BaseSilkSkill.GetEffectFlags());

    /// <summary>
    /// Adds hostile players inside the Rune Rage sonar to its target list, so they can be targeted.
    /// </summary>
    private void OnBuildRuneRageArray(PlayMakerFSM fsm) {
        if (!_netClient.IsConnected) {
            return;
        }

        _runeRagePositions.Clear();

        var sonarObject = HeroController.instance.gameObject
                                        .FindGameObjectInChildren("Special Attacks")?
                                        .FindGameObjectInChildren("Sonar Enemy Tracker");
        if (sonarObject == null) {
            return;
        }

        var sonar = sonarObject.GetComponent<TrackTriggerObjects>();
        var sonarCollider = sonarObject.GetComponent<CircleCollider2D>();
        if (sonar == null || sonarCollider == null) {
            return;
        }

        // Remove stale player objects
        sonar.Refresh();

        if (!_serverSettings.IsPvpEnabled) {
            return;
        }

        Vector2 center = sonarCollider.transform.position;
        var radius = sonarCollider.radius * sonar.transform.GetScaleX();
        var inSonar = sonar.insideObjectsList;

        foreach (var player in _playerData.Values) {
            if (!player.IsInLocalScene || !player.PlayerObject) {
                continue;
            }

            if (IsTeammate(player)) {
                inSonar.Remove(player.PlayerObject);
                continue;
            }

            var collider = player.PlayerObject.GetComponent<BoxCollider2D>();
            if (!collider) {
                continue;
            }

            if (IsWithinRadius(collider, center, radius)) {
                inSonar.AddIfNotPresent(player.PlayerObject);
            } else {
                inSonar.Remove(player.PlayerObject);
            }
        }
    }

    /// <summary>
    /// Records the spawn position of a targeted Rune Rage blast.
    /// </summary>
    private void OnRuneBlastEnemy(PlayMakerFSM fsm) {
        // The blast position is relative to the hero at this point
        var shiftPosition = fsm.FsmVariables.FindFsmVector3("Shift Pos");
        if (shiftPosition != null) {
            var worldPosition = shiftPosition.Value + HeroController.instance.transform.position;
            _runeRagePositions.AddRange(RuneRage.EncodeRunePosition(worldPosition));
        }
    }

    /// <summary>
    /// Records the spawn positions of random Rune Rage blasts, which fill in when there aren't enough targets.
    /// </summary>
    private void OnRuneBlastRandom(PlayMakerFSM fsm) {
        var spawner = fsm.GetFirstAction<SpawnRandomObjectsRadialRandom>("Random Blasts");
        foreach (var position in spawner.tempPosStore) {
            _runeRagePositions.AddRange(RuneRage.EncodeRunePosition(position));
        }
    }

    /// <summary>
    /// Sends all recorded Rune Rage blast positions once the skill has finished.
    /// </summary>
    private void OnRuneBlastFinished(PlayMakerFSM fsm) {
        byte[] effectInfo = [.. BaseSilkSkill.GetEffectFlags(), .. _runeRagePositions];
        _runeRagePositions.Clear();

        SendIfConnected(AnimationClip.SilkBombLocations, effectInfo);
    }

    /// <summary>
    /// Adds hostile players that a Pale Nail can see to its target list, so they can be targeted.
    /// </summary>
    private void OnPaleNailAttackCheck(PlayMakerFSM fsm) {
        if (!_netClient.IsConnected) {
            return;
        }

        var sonar = fsm.gameObject.GetComponentInChildren<TrackTriggerObjectsLineOfSight>();
        var sonarCollider = fsm.gameObject.GetComponentInChildren<CircleCollider2D>();
        if (sonar == null || sonarCollider == null) {
            return;
        }

        // Remove stale player objects
        sonar.Refresh();

        if (!_serverSettings.IsPvpEnabled) {
            return;
        }

        Vector2 center = sonarCollider.transform.position;
        var radius = sonarCollider.radius * sonar.transform.GetScaleX();
        var inSonar = sonar.insideObjectsList;

        foreach (var player in _playerData.Values) {
            if (!player.IsInLocalScene || !player.PlayerObject || IsTeammate(player)) {
                continue;
            }

            var collider = player.PlayerObject.GetComponent<BoxCollider2D>();
            if (!collider) {
                continue;
            }

            if (IsWithinRadius(collider, center, radius) && sonar.IsCounted(collider.gameObject)) {
                inSonar.AddIfNotPresent(player.PlayerObject);
            }
        }
    }

    /// <summary>
    /// Sends the target of a Pale Nails volley.
    /// </summary>
    private void OnPaleNailFire(PlayMakerFSM fsm) {
        // Every nail runs this state; only the first nail (x offset 3) reports, so the target is sent once
        var offset = fsm.FsmVariables.GetFsmVector3("Offset").Value;
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (offset.x != 3) {
            return;
        }

        // Without a target the nails fire on their own, so there is nothing to sync
        var target = fsm.FsmVariables.FindFsmGameObject("Target").Value;
        if (target == null) {
            return;
        }

        SendIfConnected(AnimationClip.SilkBossNeedleFire, PaleNails.EncodeTargetInfo(target));
    }

    /// <summary>
    /// Whether the given player is on the same team as the local player, which is only the case when teams are
    /// enabled.
    /// </summary>
    /// <param name="player">The player to check.</param>
    /// <returns>True if the player is a teammate, otherwise false.</returns>
    private bool IsTeammate(ClientPlayerData player) =>
        _serverSettings.TeamsEnabled
        && player.Team != Team.None
        && player.Team == _playerManager.LocalPlayerTeam;

    /// <summary>
    /// Whether the closest point of the given collider is within the radius around the center.
    /// </summary>
    /// <param name="collider">The collider to check.</param>
    /// <param name="center">The center of the radius.</param>
    /// <param name="radius">The radius around the center.</param>
    /// <returns>True if the collider is within the radius, otherwise false.</returns>
    private static bool IsWithinRadius(Collider2D collider, Vector2 center, float radius) =>
        Vector2.Distance(collider.ClosestPoint(center), center) <= radius;

    #endregion

    #region Tool Hooks

    /// <summary>
    /// Register the hooks that send the animations of tools.
    /// </summary>
    /// <param name="hero">The hero whose tool FSMs are hooked.</param>
    private void CreateToolHooks(HeroController hero) {
        MagnetiteDice.Hook(OnDiceEnable);
        MagmaBell.HookRecharge(OnMagmaBellRecharge);

        FsmStateActionInjector.Inject(hero.toolsFSM.GetState("Flea Brew Burst"), OnFleaBrew, 0, "Flea Brew");

        var maskFsm = hero.gameObject
                          .FindGameObjectInChildren("Charm Effects")?
                          .FindGameObjectInChildren("Fractured Mask Break")?
                          .LocateMyFSM("Spawn Effect");
        if (maskFsm) {
            FsmStateActionInjector.Inject(
                maskFsm.GetState("Instantiate Effect"), OnFracturedMaskBreak, 0, "Fractured Mask Break"
            );
        }

        // Static event that outlives the hero: unsubscribe first so a new hero instance doesn't add a duplicate
        ToolItemManager.BoundAttackToolUsed -= AttackToolUsed;
        ToolItemManager.BoundAttackToolUsed += AttackToolUsed;
    }

    /// <summary>
    /// Callback for when the local player uses an attack tool.
    /// </summary>
    private void AttackToolUsed(AttackToolBinding binding) {
        if (!_netClient.IsConnected) {
            return;
        }

        var tool = ToolItemManager.GetBoundAttackTool(binding, ToolEquippedReadSource.Active);
        if (tool == null || !BaseAttackTool.ToolNameMap.TryGetValue(tool.name, out var toolClip)) {
            return;
        }

        _netClient.UpdateManager.UpdatePlayerAnimation(toolClip, 0, BaseAttackTool.GetToolInfo());
    }

    /// <summary>
    /// Callback for when the shield effect of the Magnetite Dice is enabled.
    /// </summary>
    private void OnDiceEnable() => SendIfConnected(AnimationClip.ToolMagnetiteDice);

    /// <summary>
    /// Callback for when the local player drinks a Flea Brew.
    /// </summary>
    /// <param name="fsm">The FSM that ran the injected action.</param>
    private void OnFleaBrew(PlayMakerFSM fsm) =>
        SendIfConnected(AnimationClip.ToolFleaBrew, FleaBrew.Instance.GetEffectInfo());

    /// <summary>
    /// Callback for when the Fractured Mask of the local player breaks.
    /// </summary>
    /// <param name="fsm">The FSM that ran the injected action.</param>
    private void OnFracturedMaskBreak(PlayMakerFSM fsm) => SendIfConnected(AnimationClip.ToolFracturedMask);

    /// <summary>
    /// Callback for when the Magma Bell is used.
    /// </summary>
    private void OnMagmaBell() => SendIfConnected(AnimationClip.ToolMagmaBell, [0]);

    /// <summary>
    /// Callback for when the Magma Bell has recharged.
    /// </summary>
    private void OnMagmaBellRecharge() => SendIfConnected(AnimationClip.ToolMagmaBell, [1]);

    #endregion

    /// <summary>
    /// Log an animation trace message. Calls are removed by the compiler unless <see cref="TraceSymbol"/> is
    /// defined.
    /// </summary>
    /// <param name="message">The message to log.</param>
    [System.Diagnostics.Conditional(TraceSymbol)]
    private static void LogTrace(string message) => Logger.Info(message);
}

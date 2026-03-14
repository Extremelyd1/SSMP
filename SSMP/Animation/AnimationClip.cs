namespace SSMP.Animation;

/// <summary>
/// Enumeration of all possible animation clip names.
/// Some have doc comments explaining when they are played. The ones missing are either unknown or self-explanatory.
/// </summary>
internal enum AnimationClip {
    // Sprite animator clip names
    WakeToSit = 1,
    SitIdle,
    SitLook1,
    SitLook2,
    SitLook3,
    SitLook4,
    SitLean,
    SitLookLeft,
    SittingAsleep,
    GetOff,
    
    Land,
    HardLand,
    HardLandGreymoor,
    HardLandQuick,
    SuperHardLand,
    LandToSprint,

    Hop,
    HopLand,
    HopToSomersault,

    Run,
    RunQ,
    RunToIdle,
    RunToIdleC,

    Idle,
    IdleBackward,
    IdleBG,
    IdleHurtListen,
    IdleHurtListenBackward,
    IdleHurtListenWindy,
    IdleHurtNoNeedle,
    IdleHurtTalk,
    IdleHurtTalkBackward,
    IdleHurtTalkTurnBackward,
    IdleHurtTalkTurnForward,
    IdleHurtTalkWindy,
    IdleHurtWindy,
    IdleSlap,
    IdleToRunShort,
    IdleToRunWeak,
    IdleWindy,
    IdleToRun,

    WalkToIdle,
    RunToWalk,
    RunToWalkWeak,
    LandToWalk,
    Turn,
    Dash,
    DashDown,
    DashDownLand,
    DashDownEnd,
    //DashStabEffect,
    //DashStabEffectGlow,
    SprintLv2,
    SprintLv3,
    SprintRecoil,
    SprintSkidToRun,
    SprintToRun,
    MantleCling,
    MantleLand,
    MantleLandToIdle,
    MantleVault,
    SprintAirShort,
    SprintTurn,
    ShuttlecockAntic,
    Shuttlecock,
    SprintAir,
    SprintAirLoop,
    DoubleJump,
    DoubleJumpWings2,
    LandToRun,
    DashAttackAntic,
    DashAttack,
    DashAttackRecover,
    DashAttackAnticLong,
    DashAttackAnticShort,
    SkidEnd1,
    SkidEnd2,
    DashToIdle,
    DashToRun,
    Slash,
    SlashAlt,
    SlashToRun,
    AirDash,
    SlashLandRunAlt,
    DownSpikeAntic,
    DownSpike,
    DownSpikeBurst,
    DownSpikeRecovery,
    DownSpikeRecoveryLand,
    DownSpikeFollowup,
    UpSlash,
    SlashLand,
    SlashCharged,
    /// <summary>
    /// The Needle Strike with Witch crest when repressing the button up to 2 times.
    /// </summary>
    SlashChargedLoop,
    UmbrellaInflateAntic,
    WallJump,
    WallJumpAntic,
    WallJumpPuff,
    WallJumpSomersault,
    Wallrun,
    WallrunAntic,
    WallSlide,
    SlideBrake,
    SlideBrakeEnd,
    SlideFast,
    SlideFastEnd,
    SlideNormal,
    SlideStart,

    WallCling,
    SitFallAsleep,
    
    UmbrellaInflate,
    UmbrellaFloat,
    UmbrellaDeflate,
    UmbrellaTurn,

    IdleRest,
    MantleLandToRun,
    Sit,
    Sprint,
    SprintBackflip,

    AirSphere,
    AirSphereAntic,
    AirSphereDissipate,
    AirSphereRepeatAntic,

    SilkBossNeedleCast,

    Taunt,
    TauntBack,
    TauntBackEnd,
    TauntBackUp,
    TauntEndtoIdle,
    TauntIdle,
    TauntRings,
    TauntRingsFlash,
    TauntStraightBack,
    TauntStraightBackQ,
    TauntThread,
    ChallengeStrong,
    ChallengeTalk,
    ToChallengeTalk,
    ChallengeTalkEnd,
    ChallengeTalkEndToIdle,
    ChallengeTalkEndToTalk,
    ChallengeTalkIdle,
    ChallengeTalkIdleStart,
    ChallengeTalkStart,
    ChallengeStrongToIdle,

    NeedolinStart,
    NeedolinStartCancelable,
    NeedolinEnd,
    NeedolinPlay,
    NeedolinSitStart,
    NeedolinSitPlay,
    NeedolinSitEnd,
    NeedolinPlayLowTransition,
    NeedolinPlayLow,
    NeedolinPlayHighTransition,
    NeedolinPlayHigh,
    NeedolinDeepEnd,
    NeedolinPromptedIdle,
    NeedolinPromptedPlayEnd,
    NeedolinPromptedPlayStart,
    NeedolinPromptedStart,
    NeedolinThread,
    NeedolinTurn,
    NeedolinSitTurn,

    HarpoonAntic,
    HarpoonThrow,
    HarpoonCatch,
    HarpoonCatchBack,
    HarpoonDash,
    HarpoonNeedle,
    HarpoonNeedleReturn,
    HarpoonNeedleWallHit,
    HarpoonThread,
    
    MapOpen,
    MapIdle,
    MapAway,
    MapTurn,
    MapUpdate,
    MapWalk,
    SitMapOpen,
    SitMapClose,
    
    BindChargeGround,
    BindChargeAir,
    BindBurstAir,
    BindBurstGround,
    BindCursedEnd,
    BindCursedMid,
    BindCursedStart,
    BindFirstEnd,
    BindFirstStand,
    BindFlash,
    BindSilk,
    BindSilkFirstWeaver,
    BindSilkLoop,
    BindSilkQuick,
    BindCancelAir,
    BindCancelGround,
    BindChargeGrabNeedle,
    ReserveBindBurstAir,
    ReserveBindBurstGround,
    ReserveBindChargeAir,
    ReserveBindChargeGround,

    LookDown,
    LookDownEnd,

    SuperJumpAntic,
    SuperJumpThrow,
    SuperJumpThrowWait,
    SuperJumpJumpAntic,
    SuperJumpLoop,
    SuperJumpHitRoof,
    SuperJumpFall,
    SuperJumpAnticCancel,
    SuperJumpAnticEffect,
    SuperJumpAnticEffectEnd,
    SuperJumpCatchCancel,
    SuperJumpHitRoofQ,
    SuperJumpLoopCancel,
    SuperJumpThread,
    SuperJumpThreadLoop,

    Fall,
    FallToProstrate,

    LookUp,
    LookUpEnd,

    SurfaceIn,
    SurfaceInToIdle,
    SurfaceIdle,
    SurfaceIdleToSwim,
    SurfaceTurnToSwim,
    SurfaceCurrentInRecover,
    SurfaceCurrentInTumble,
    SwimDash,
    SwimDashTurn,
    SwimDashBonk,
    MantleCancelToJump,
    MantleCancelToJumpBackwards,
    WallScrambleAntic,
    WallScramble,
    WallScrambleMantle,
    WallScrambleQuickened,
    WallScrambleRepeat,
    WallScrambleEnd,
    WallSlash,
    SomersaultPinDrop,
    Airborne,
    /// <summary>
    /// Sprinting into a wall
    /// </summary>
    Bonked,
    SprintBonk,
    BonkedFast,
    BonkedLand,

    SlashLandRun,
    /// <summary>
    /// Down slash with Reaper crest
    /// </summary>
    V3DownSlashAntic,
    /// <summary>
    /// Down slash with Reaper crest
    /// </summary>
    V3DownSlash,
    /// <summary>
    /// Slash while sprinting/dashing with Reaper crest
    /// </summary>
    DashUpperAntic,
    /// <summary>
    /// Slash while sprinting/dashing with Reaper crest
    /// </summary>
    DashUpper,
    /// <summary>
    /// Slash while sprinting/dashing with Reaper crest
    /// </summary>
    DashUpperRecovery,
    /// <summary>
    /// Slash while sprinting/dashing with Wanderer crest
    /// </summary>
    WandererDashAttack,
    /// <summary>
    /// Alt Slash while sprinting/dashing with Wanderer crest
    /// </summary>
    WandererDashAttackAlt,
    /// <summary>
    /// Down slash with Wanderer/Shaman crest
    /// </summary>
    DownSlash,
    /// <summary>
    /// Down slash with Wanderer crest
    /// </summary>
    DownSlashAlt,
    /// <summary>
    /// Down slash with Beast crest
    /// </summary>
    SpinBallAntic,
    /// <summary>
    /// Down slash with Beast crest
    /// </summary>
    SpinBallLaunch,
    /// <summary>
    /// Down slash with Beast crest
    /// </summary>
    SpinBall,
    /// <summary>
    /// Charged slash with Beast crest
    /// </summary>
    NeedleArtDash,
    /// <summary>
    /// Bind with Beast crest in air
    /// </summary>
    RageBind,
    /// <summary>
    /// Bind with Beast crest in air with Injector Band
    /// </summary>
    RageBindQuick,
    /// <summary>
    /// Bind with Beast crest in air with Multibinder
    /// </summary>
    RageBindLong,
    /// <summary>
    /// Bind with Beast crest on ground
    /// </summary>
    RageBindGrounded,
    /// <summary>
    /// Bind with Beast crest on ground with Injector Band
    /// </summary>
    RageBindQuickGrounded,
    /// <summary>
    /// Bind with Beast crest on ground with Multibinder
    /// </summary>
    RageBindLongGrounded,
    /// <summary>
    /// Bind with Beast crest
    /// </summary>
    RageBindBurst,
    /// <summary>
    /// Bind with Beast crest with Injector Band
    /// </summary>
    RageBindBurstQuick,
    /// <summary>
    /// Bind with Beast crest with Multibinder
    /// </summary>
    RageBindBurstLong,
    /// <summary>
    /// Landing after bind with Beast crest
    /// </summary>
    LandQ,
    /// <summary>
    /// Rage from bind with Beast crest
    /// </summary>
    RageIdle,
    /// <summary>
    /// Rage from bind with Beast crest
    /// </summary>
    RageIdleEnd,
    /// <summary>
    /// Slash while sprinting/dashing with Witch crest
    /// </summary>
    DashAttackAntic1,
    /// <summary>
    /// Slash while sprinting/dashing with Witch crest
    /// </summary>
    DashAttack1,
    /// <summary>
    /// Slash while sprinting/dashing with Witch crest
    /// </summary>
    DashAttackAntic2,
    /// <summary>
    /// Slash while sprinting/dashing with Witch crest
    /// </summary>
    DashAttack2,
    /// <summary>
    /// Bind with Witch crest
    /// </summary>
    BindChargeWitch,
    /// <summary>
    /// Bind with Witch crest and Injector Band
    /// </summary>
    BindChargeWitchQuick,
    /// <summary>
    /// Bind with Witch crest and Multibinder
    /// </summary>
    BindChargeWitchLong,
    /// <summary>
    /// Down slash with Architect crest
    /// </summary>
    DownSpikeCharge,
    /// <summary>
    /// Fully charged down slash with Architect crest
    /// </summary>
    DownSpikeCharged,
    /// <summary>
    /// Slash while sprinting/dashing with Architect crest
    /// </summary>
    DashAttackCharge,
    /// <summary>
    /// Craft-bind with Architect crest on ground
    /// </summary>
    QuickCraftGround,
    /// <summary>
    /// Craft-bind with Architect crest in air
    /// </summary>
    QuickCraftAir,
    /// <summary>
    /// Slash while sprinting/dashing with Beast or Shaman crest
    /// </summary>
    DashAttackLeap,
    /// <summary>
    /// Slash while sprinting/dashing with Beast or Shaman crest
    /// </summary>
    DashAttackSlash,
    /// <summary>
    /// Bind with Shaman crest
    /// </summary>
    BindChargeGroundLand,
    /// <summary>
    /// Bind with Shaman crest
    /// </summary>
    BindChargeHealBurst,
    /// <summary>
    /// Bind with Shaman crest
    /// </summary>
    BindChargeEnd,
    
    /// <summary>
    /// Bind with Claw Mirrors (and maybe also the individual mirrors respectively) in air.
    /// Does not play with Beast, Witch, or Shaman crest.
    /// </summary>
    BindChargeTwirlAir,
    /// <summary>
    /// Bind with Claw Mirrors (and maybe also the individual mirrors respectively) on ground.
    /// Does not play with Beast, Witch, or Shaman crest.
    /// </summary>
    BindChargeTwirlGround,
    
    /// <summary>
    /// Silkspear while on ground
    /// </summary>
    NeedleThrowAnticG,
    /// <summary>
    /// Silkspear while in air
    /// </summary>
    NeedleThrowAnticA,
    NeedleThrowThrowing,
    NeedleThrowCatch,
    NeedleThrowBurst,
    NeedleThrowOut,
    NeedleThrowReturn,
    NeedleThrowReturnShort,
    NeedleThrowThread,
    NeedleThrowThunk,
    /// <summary>
    /// Cross Stitch in air
    /// </summary>
    ParryStance,
    /// <summary>
    /// Cross Stitch on ground
    /// </summary>
    ParryStanceGround,
    ParryRecover,
    ParryRecoverGround,
    ParryClash,
    ParryClashEffect,
    ParryDash,
    ParryDashBurst,
    ParryReady,
    ParryRecoverySkid,
    ParryStanceFlash,
    ParryStanceFlashQ,
    ParryThread,
    GetParryDash,
    GetParryEnd,
    GetParryPrepare,
    /// <summary>
    /// Thread Storm
    /// </summary>
    AirSphereAttack,
    AirSphereEnd,
    /// <summary>
    /// Sharpdart
    /// </summary>
    SilkCharge,
    SilkChargeAntic,
    SilkChargeAnticZap,
    SilkChargeRecover,
    SilkChargeRecoverZap,
    SilkChargeZap,
    SilkChargeEnd,
    /// <summary>
    /// Rune Rage
    /// </summary>
    SilkBombAntic,
    SilkBombAnticQ,
    SilkBombLoop,
    SilkBombRecover,
    
    SitCraft,
    SitCraftSilk,
    ToolThrowUp,
    /// <summary>
    /// Tools thrown forwards such as Straight Pin
    /// </summary>
    ToolThrowQ,
    ToolThrowAltQ,
    ToolThrowM,
    ToolThrowWall,

    /// <summary>
    /// Moving when exiting Delver's Drill attack
    /// </summary>
    RecoilTwirl,
    /// <summary>
    /// Not moving when exiting Delver's Drill attack
    /// </summary>
    DownSpikeBounce1,
    DownSpikeBounce2,
    /// <summary>
    /// Using Flea Brew
    /// </summary>
    ChargeUp,
    ChargeUpAir,
    ChargeUpBench,
    ChargeUpBenchSilk,
    ChargeUpBurst,

    IdleHurt,
    TurnWalk,
    TurnBackThreeQuarter,
    TurnBackThreeQuarterEndToIdle,
    TurnHeadBackward,
    TurnHeadForward,
    TurnQuick,
    TurnFromBG,
    TurnFromBGLoop,
    TurnToBG,
    TurnToBGLoop,
    TurnToChallengeIdle,
    TurnToChallengeStrong,
    TurnToChallengeTalk,
    TurnToFG,
    TurnToIdle,


    Walk,
    WalkQ,
    LookUpHalf,
    LookUpHalfEnd,
    LookDownSlight,
    LookDownSlightEnd,
    LookDownTalk,
    LookUpHalfFlinch,
    LookUpHalfTalk,
    LookUpTalk,
    LookDownUpdraft,
    LookDownWindy,
    LookDownEndUpdraft,
    LookDownEndWindy,
    LookDownToIdle,
    LookingUp,
    LookUpUpdraft,
    LookUpWindy,
    LookUpEndUpdraft,
    LookUpEndWindy,
    LookUpToIdle,

    ScuttleStart,
    ScuttleLoop,
    ScuttleTurnToLoop,
    ScuttleEnd,
    ScuttleFall,
    ScuttleVault,
    ScuttleJump,
    ScuttleClimb,
    ScuttleClimbEnd,

    Stun,
    Recoil,
    
    GrabEscape,
    
    IdleUpdraft,
    UpdraftAntic,
    UpdraftAnticDJ,
    UpdraftRise,
    UpdraftRiseTurn,
    UpdraftEnd,
    UpdraftIdle,
    UpdraftShoot,

    ProstrateRiseNoNeedle,
    BindFirst,
    BindBurstFirst,
    ThwipToIdle,
    GetUpToIdle,

    WeakenedStun,
    WeakRiseToIdle,
    WeakWalk,
    WeakTryJumpAntic,
    WeakTryAttack,
    WeakenedStunEnd,
    WeakFall,
    WeakFlinchToIdle,
    WeakIdleLookUp,
    WeakWalkFaster,
    WeakWalkToIdle,

    TauntCollapse1,
    TauntCollapse2,
    TauntCollapseHit,
    Prostrate,
    ProstrateRise,
    ProstrateNoNeedle,
    ProstrateRiseSlow,
    ProstrateRiseToKneel,
    ProstrateRiseToKneelNoLoop,
    ProstrateRiseToWound,

    TalkingStandard,
    TalkingBackward,

    AbyssKneel,
    AbyssKneelBackIdle,
    AbyssKneelBackTalk,
    AbyssKneelIdle,
    AbyssKneeltoStand,
    AbyssKneelTurnBack,
    KneelToProstrate,
    Kneeling,

    AcidDeath,
    Death,
    DeathFinal,
    SpikeDeath,
    SpikeDeathAntic,

    BeastlingCallFail,
    BeastlingCallFailWindy,
    BellwayCall,
    FastTravelCall,
    FastTravelChildArrive,
    FastTravelFail,
    FastTravelLeap,

    CollectHeartPiece,
    CollectHeartPieceEnd,
    CollectMemoryOrb,
    CollectNormal1,
    CollectNormal1Q,
    CollectNormal2,
    CollectNormal3,
    CollectNormal3Q,
    CollectSilkHeart,
    CollectStand1,
    CollectStand2,
    CollectStand3,
    CollectToWound,

    DropToWounded,
    BlueHealthOverBurst,
    CrestShrinePowerupLoop,
    HazardRespawn,
    RespawnWake,
    PodBounce,
    QuickCharged,
    QuickCraftSilk,
    SprintmasterLow,
    SprintmasterStart,

    DressFlourish,
    GiveDress,
    GiveDressIdle,

    Enter,
    Exit,
    ExitDoorToIdle,
    ExitToIdle,
    Wake,
    WakeUpGround,

    HurtListenDown,
    HurtListenUp,
    HurtLookDown,
    HurtLookDownWindy,
    HurtLookDownWindyEnd,
    HurtLookUp,
    HurtLookUpEnd,
    HurtLookUpWindy,
    HurtLookUpWindyEnd,
    HurtTalkDown,
    HurtTalkUp,
    HurtTalkUpWindy,
    HurtToIdle,

    NeedleFall,
    NeedleLand,

    RingDropImpact,
    RingEject,
    RingGrabHornet,
    RingGrabRail,
    RingHarpoonConnect,
    RingLookDown,
    RingLookDownEnd,
    RingLookUp,
    RingLookUpEnd,
    RingTurn,

    RoarLock,
    RoarToLookUp,

    SpaSurfaceIdle,
    SpaSurfaceIdleToSwim,
    SpaSurfaceIn,
    SpaSurfaceInToIdle,
    SpaSurfaceTurnToSwim,

    WeaverPray,
    WeaverPrayEnd,
    WeaverPrayPrepare,
    WeaverPrayPrepareFront,

    Wound,
    WoundDoubleStrike,
    WoundZap,

    // Custom clip names
    DashEnd,
    NailArtCharge,
    NailArtCharged,
    NailArtChargeEnd,
    WallSlideEnd,
    HazardDeath,

    // Sub-animation names
    WitchTentacles,
    ShamanCancel,
    BindInterupt,
}

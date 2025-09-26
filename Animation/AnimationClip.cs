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
    GetOff,
    Land,
    Run,
    RunQ,
    RunToIdle,
    Idle,
    IdleToRun,
    Turn,
    Dash,
    SprintLv2,
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
    LandToRun,
    DashAttackAntic,
    DashAttack,
    DashAttackRecover,
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
    DownSpikeRecovery,
    DownSpikeRecoveryLand,
    UpSlash,
    SlashLand,
    SlashCharged,
    UmbrellaInflateAntic,
    WallJump,
    WallSlide,
    WallCling,
    SitFallAsleep,
    UmbrellaInflate,
    UmbrellaFloat,
    UmbrellaDeflate,
    IdleRest,
    MantleLandToRun,
    Sit,
    Sprint,
    SprintBackflip,
    AirSphereAntic,
    SilkBossNeedleCast,
    Taunt,
    NeedolinStart,
    NeedolinStartCancelable,
    NeedolinEnd,
    NeedolinPlay,
    NeedolinSitStart,
    NeedolinSitPlay,
    NeedolinSitEnd,
    HarpoonAntic,
    HarpoonThrow,
    HarpoonCatch,
    SilkChargeEnd,
    MapOpen,
    MapIdle,
    MapAway,
    SitMapOpen,
    SitMapClose,
    BindChargeGround,
    BindChargeAir,
    BindBurstAir,
    BindBurstGround,
    LookDown,
    LookDownEnd,
    NeedolinPlayLowTransition,
    NeedolinPlayLow,
    NeedolinPlayHighTransition,
    NeedolinPlayHigh,
    SuperJumpAntic,
    SuperJumpThrow,
    SuperJumpThrowWait,
    SuperJumpJumpAntic,
    SuperJumpLoop,
    SuperJumpHitRoof,
    SuperJumpFall,
    LookUp,
    LookUpEnd,
    SurfaceIn,
    SurfaceInToIdle,
    SurfaceIdle,
    SurfaceIdleToSwim,
    SurfaceTurnToSwim,
    SwimDash,
    SwimDashTurn,
    SwimDashBonk,
    MantleCancelToJump,
    MantleCancelToJumpBackwards,
    WallScrambleAntic,
    WallScramble,
    WallScrambleEnd,
    WallSlash,
    WallJumpSomersault,
    Airborne,
    /// <summary>
    /// Sprinting into a wall
    /// </summary>
    SprintBonk,
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
    /// Slash while sprinting/dashing with Shaman crest
    /// </summary>
    DashAttackLeap,
    /// <summary>
    /// Slash while sprinting/dashing with Shaman crest
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
    /// <summary>
    /// Thread Storm
    /// </summary>
    AirSphereAttack,
    AirSphereEnd,
    /// <summary>
    /// Sharpdart
    /// </summary>
    SilkChargeAntic,
    SilkCharge,
    SilkChargeRecover,
    /// <summary>
    /// Rune Rage
    /// </summary>
    SilkBombAntic,
    SilkBombLoop,
    SilkBombRecover,
    
    SitCraft,
    ToolThrowUp,
    /// <summary>
    /// Tools thrown forwards such as Straight Pin
    /// </summary>
    ToolThrowQ,
    ToolThrowAltQ,
    
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
    
    IdleHurt,
    TurnWalk,
    Walk,
    LookUpHalf,
    LookUpHalfEnd,
    LookDownSlight,
    LookDownSlightEnd,
    
    ScuttleStart,
    ScuttleLoop,
    ScuttleEnd,
    ScuttleFall,
    ScuttleVault,
    
    Stun,
    Recoil,
    
    GrabEscape,

    // Custom clip names
    DashEnd,
    NailArtCharge,
    NailArtCharged,
    NailArtChargeEnd,
    WallSlideEnd,
    HazardDeath,
}

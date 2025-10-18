using System;
using SSMP.Internals;
using SSMP.Networking.Packet;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the needle strike (charged slash).
/// </summary>
internal class NeedleStrike : DamageAnimationEffect {
    /// <summary>
    /// Cached game object for the local player's attacks object.
    /// </summary>
    private static GameObject? _localAttacks;

    /// <summary>
    /// Cached game object for the charge slash for Hunter crest.
    /// </summary>
    private static GameObject? _chargeSlashBasic;

    /// <summary>
    /// Cached game object for the charge slash for Reaper crest.
    /// </summary>
    private static GameObject? _chargeSlashScythe;

    /// <summary>
    /// Cached game object for the charge slash for Witch crest.
    /// </summary>
    private static GameObject? _chargeSlashWitch;

    /// <summary>
    /// Cached game object for the charge slash for Wanderer crest.
    /// </summary>
    private static GameObject? _chargeSlashWanderer;

    /// <summary>
    /// Cached game object for the charge slash for Architect crest.
    /// </summary>
    private static GameObject? _chargeSlashArchitect;

    /// <summary>
    /// Cached game object for the charge slash for Shaman crest.
    /// </summary>
    private static GameObject? _chargeSlashShaman;

    /// <summary>
    /// Cached game object for the charge slash for Beast crest.
    /// </summary>
    private static GameObject? _chargeSlashBeast;

    /// <summary>
    /// Cached game object for the charge slash for Beast crest when in rage (bind) mode.
    /// </summary>
    private static GameObject? _chargeSlashBeastRage;

    /// <summary>
    /// Cached FSM for Nail Arts.
    /// </summary>
    private static PlayMakerFSM? _nailArtsFsm;

    /// <summary>
    /// Cached PlayAudioEvent for Reaper crest.
    /// </summary>
    private static PlayAudioEvent? _reaperAudioEvent;

    /// <summary>
    /// Cached PlayAudioEvent for Witch crest.
    /// </summary>
    private static PlayAudioEvent? _witchAudioEvent;

    /// <summary>
    /// Cached PlayAudioEvent for Wanderer crest.
    /// </summary>
    private static PlayAudioEvent? _wandererAudioEvent;

    /// <summary>
    /// Cached PlayAudioEvent for Architect crest.
    /// </summary>
    private static PlayAudioEvent? _architectAudioEvent;

    /// <summary>
    /// Cached PlayAudioEvent for Beast crest when doing the leap.
    /// </summary>
    private static PlayAudioEvent? _beastLeapAudioEvent;

    /// <summary>
    /// Cached PlayAudioEvent for Beast crest when doing the slash.
    /// </summary>
    private static PlayAudioEvent? _beastSlashAudioEvent;

    /// <summary>
    /// Whether this Needle Strike is for a loop of the Witch attack (where you press the button again, up to 2 times).
    /// </summary>
    private readonly bool _witchLoop;

    public NeedleStrike(bool witchLoop) {
        _witchLoop = witchLoop;
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        if (effectInfo == null || effectInfo.Length < 1) {
            Logger.Error("Could not get null or empty effect info for SlashBase");
            return;
        }

        var packet = new Packet(effectInfo);
        var slashEffects = packet.ReadBitFlag<SlashBase.SlashEffect>();

        if (!AnimationUtil.GetConfigsFromCrestType(
                crestType,
                out var configGroup,
                out _
            ) || configGroup == null) {
            return;
        }

        _localAttacks ??= HeroController.instance.gameObject.FindGameObjectInChildren("Attacks");
        if (_localAttacks == null) {
            Logger.Warn("Could not find local Attacks object in hero object");
            return;
        }

        var playerAttacks = playerObject.FindGameObjectInChildren("Attacks");
        if (playerAttacks == null) {
            Logger.Warn("Player object does not have player attacks child, cannot play needle strike");
            return;
        }

        int? damage = ServerSettings.IsPvpEnabled && ShouldDoDamage ? 1 : null;

        Logger.Info($"NeedleStrike animation effect for crest: {crestType}");

        // Each of the below if statement blocks contains the logic for spawning the effect for an individual crest

        if (crestType.IsHunter()) {
            _chargeSlashBasic ??= _localAttacks.FindGameObjectInChildren("Charge Slash Basic");
            if (_chargeSlashBasic == null) {
                Logger.Warn("Could not find Charge Slash Basic in local attacks");
                return;
            }

            var strikeObj = Object.Instantiate(_chargeSlashBasic, playerAttacks.transform);
            strikeObj.layer = 17;
            strikeObj.name = "Needle Strike (Hunter)";

            ModifyDamagingSlashObject(strikeObj.FindGameObjectInChildren("slash 01"), damage);
            ModifyDamagingSlashObject(strikeObj.FindGameObjectInChildren("slash 02"), damage);

            strikeObj.ActivateAfterTime(0.16f);

            strikeObj.DestroyAfterTime(5f);
            return;
        }

        if (crestType is CrestType.Reaper) {
            _chargeSlashScythe ??= _localAttacks.FindGameObjectInChildren("Charge Slash Scythe");
            if (_chargeSlashScythe == null) {
                Logger.Warn("Could not find Charge Slash Scythe in local attacks");
                return;
            }

            _reaperAudioEvent ??= GetOrFindNailArtsFsm().GetFirstAction<PlayAudioEvent>("Antic Rpr");
            AudioUtil.PlayAudioEventAtPlayerObject(_reaperAudioEvent, playerObject);

            var strikeObj = Object.Instantiate(_chargeSlashScythe, playerAttacks.transform);
            strikeObj.layer = 17;
            strikeObj.name = "Needle Strike (Reaper)";

            ModifyDamagingSlashObject(strikeObj.FindGameObjectInChildren("slash 01"), damage);
            ModifyDamagingSlashObject(strikeObj.FindGameObjectInChildren("slash 02"), damage);
            ModifyDamagingSlashObject(strikeObj.FindGameObjectInChildren("slash behind"), damage);

            strikeObj.ActivateAfterTime(0.3333333f);

            strikeObj.DestroyAfterTime(5f);
            return;
        }

        if (crestType is CrestType.Wanderer) {
            _chargeSlashWanderer ??= _localAttacks.FindGameObjectInChildren("Charge Slash Wanderer");
            if (_chargeSlashWanderer == null) {
                Logger.Warn("Prefab for Needle Strike effect with Wanderer crest is null");
                return;
            }

            var strikeObj = Object.Instantiate(_chargeSlashWanderer, playerAttacks.transform);
            strikeObj.layer = 17;
            strikeObj.name = "Needle Strike (Wanderer)";
            
            ModifyDamagingSlashObject(strikeObj, damage);

            AnimationUtil.ExecuteActionAfterDelay(() => {
                strikeObj.SetActive(true);

                _wandererAudioEvent ??= GetOrFindNailArtsFsm().GetFirstAction<PlayAudioEvent>("Wanderer Attack");
                AudioUtil.PlayAudioEventAtPlayerObject(_wandererAudioEvent, playerObject);
            }, 0.1666667f);

            strikeObj.DestroyAfterTime(5f);

            return;
        }

        if (crestType is CrestType.Beast) {
            // Beast crest "NeedleArt Dash" animation clip triggers
            // Frame 1 has triggerEvent, seconds until frame: 0.1666667
            // Frame 4 has triggerEvent, seconds until frame: 0.4166667
            // Frame 6 has triggerEvent, seconds until frame: 0.5833333

            AnimationUtil.ExecuteActionAfterDelay(() => {
                _beastLeapAudioEvent ??= GetOrFindNailArtsFsm().GetFirstAction<PlayAudioEvent>("Warrior2 Leap");
                AudioUtil.PlayAudioEventAtPlayerObject(_beastLeapAudioEvent, playerObject);
            }, 0.1666667f);

            AnimationUtil.ExecuteActionAfterDelay(() => {
                var isInRageMode = slashEffects.Contains(SlashBase.SlashEffect.BeastRageMode);
                GameObject? prefab;

                if (isInRageMode) {
                    _chargeSlashBeastRage ??= _localAttacks.FindGameObjectInChildren("Charge Slash Warrior Rage");
                    prefab = _chargeSlashBeastRage;
                } else {
                    _chargeSlashBeast ??= _localAttacks.FindGameObjectInChildren("Charge Slash Warrior");
                    prefab = _chargeSlashBeast;
                }

                if (prefab == null) {
                    Logger.Warn("Could not find Needle Strike game object for Beast crest");
                    return;
                }

                var strikeObj = Object.Instantiate(prefab, playerAttacks.transform);
                strikeObj.layer = 17;
                strikeObj.name = "Needle Strike (Beast)";
                
                ModifyDamagingSlashObject(
                    strikeObj
                        .FindGameObjectInChildren("Charge Slash Warrior Left")?
                        .FindGameObjectInChildren("enemy damager"), 
                    damage
                );
                ModifyDamagingSlashObject(
                    strikeObj
                        .FindGameObjectInChildren("Charge Slash Warrior Right")?
                        .FindGameObjectInChildren("enemy damager"), 
                    damage
                );

                strikeObj.SetActive(true);

                _beastSlashAudioEvent ??= GetOrFindNailArtsFsm().GetFirstAction<PlayAudioEvent>("Warrior2 Slash");
                AudioUtil.PlayAudioEventAtPlayerObject(_beastSlashAudioEvent, playerObject);
            }, 0.4166667f);

            return;
        }

        if (crestType is CrestType.Witch or CrestType.Cursed) {
            if (_witchLoop) {
                var existingStrikeObj = playerAttacks.FindGameObjectInChildren("Needle Strike");
                if (existingStrikeObj == null) {
                    return;
                }

                existingStrikeObj.SetActive(false);
                existingStrikeObj.SetActive(true);

                return;
            }

            _chargeSlashWitch ??= _localAttacks.FindGameObjectInChildren("Charge Slash Witch");
            if (_chargeSlashWitch == null) {
                Logger.Warn("Could not find Charge Slash Witch in local attacks");
                return;
            }

            _witchAudioEvent ??= GetOrFindNailArtsFsm().GetFirstAction<PlayAudioEvent>("Begin Spin");

            var strikeObj = Object.Instantiate(_chargeSlashWitch, playerAttacks.transform);
            strikeObj.layer = 17;
            strikeObj.name = "Needle Strike (Witch)";
            
            ModifyDamagingSlashObject(strikeObj.FindGameObjectInChildren("damager 01"), damage);
            ModifyDamagingSlashObject(strikeObj.FindGameObjectInChildren("damager 02"), damage);

            AnimationUtil.ExecuteActionAfterDelay(() => {
                AudioUtil.PlayAudioEventAtPlayerObject(_witchAudioEvent, playerObject);
                strikeObj.SetActive(true);
            }, 0.08f);

            strikeObj.DestroyAfterTime(5f);
            return;
        }

        if (crestType is CrestType.Architect) {
            // Architect crest "Slash_Charged" animation clip triggers
            // Frame 4 has triggerEvent, seconds until frame: 0.2777778
            // Frame 10 has triggerEvent, seconds until frame: 0.6111111

            _chargeSlashArchitect ??= _localAttacks.FindGameObjectInChildren("Charge Slash Toolmaster");
            if (_chargeSlashArchitect == null) {
                Logger.Warn("Could not find Charge Slash Toolmaster in local attacks");
                return;
            }

            _architectAudioEvent ??= GetOrFindNailArtsFsm().GetFirstAction<PlayAudioEvent>("Drill Sfx");

            var strikeObj = Object.Instantiate(_chargeSlashArchitect, playerAttacks.transform);
            strikeObj.layer = 17;
            strikeObj.name = "Needle Strike (Architect)";

            Object.Destroy(strikeObj.GetComponent<KeepWorldPosition>());
            
            ModifyDamagingSlashObject(
                strikeObj
                    .FindGameObjectInChildren("damager")?
                    .FindGameObjectInChildren("damager front"), 
                damage
            );

            AnimationUtil.ExecuteActionAfterDelay(() => {
                AudioUtil.PlayAudioEventAtPlayerObject(_architectAudioEvent, playerObject);
                strikeObj.SetActive(true);
            }, 0.2777778f);

            strikeObj.DestroyAfterTime(5f);
            return;
        }

        if (crestType is CrestType.Shaman) {
            _chargeSlashShaman ??= _localAttacks.FindGameObjectInChildren("Charge Slash Shaman");
            if (_chargeSlashShaman == null) {
                Logger.Warn("Could not find Charge Slash Shaman in local attacks");
                return;
            }

            var strikeObj = Object.Instantiate(_chargeSlashShaman, playerAttacks.transform);
            strikeObj.layer = 17;
            strikeObj.name = "Needle Strike (Shaman)";

            ModifyDamagingSlashObject(strikeObj.FindGameObjectInChildren("damager"), damage);

            AnimationUtil.ExecuteActionAfterDelay(() => {
                strikeObj.SetActive(true);

                SlashBase.PlayNailSlashTravel(strikeObj, slashEffects.Contains(SlashBase.SlashEffect.Longclaw));
            }, 0.2f);

            strikeObj.DestroyAfterTime(5f);
        }
    }

    /// <inheritdoc/>
    public override byte[] GetEffectInfo() {
        return SlashBase.GetSlashEffectInfo();
    }

    /// <summary>
    /// Get or find the Nail Arts FSM on the hero object. Will be cached to <see cref="_nailArtsFsm"/>.
    /// </summary>
    /// <returns>The FSM for Nail Arts.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the FSM cannot be found, which shouldn't happen.
    /// </exception>
    private PlayMakerFSM GetOrFindNailArtsFsm() {
        if (_nailArtsFsm != null) {
            return _nailArtsFsm;
        }

        var heroFsms = HeroController.instance.GetComponents<PlayMakerFSM>();
        foreach (var heroFsm in heroFsms) {
            if (heroFsm.FsmName == "Nail Arts") {
                _nailArtsFsm = heroFsm;
                return _nailArtsFsm;
            }
        }

        throw new InvalidOperationException("Could not find Nail Arts FSM on hero");
    }

    /// <summary>
    /// Modify the given game object by removing the <see cref="NailSlashTerrainThunk"/> and optionally adding
    /// a <see cref="DamageHero"/> component with the given damage.
    /// </summary>
    /// <param name="gameObject">The nullable game object. If null, no operation will be performed.</param>
    /// <param name="damage">The nullable damage as an integer. If non-null, will define the amount of damage of the
    /// added <see cref="DamageHero"/> component</param>
    private void ModifyDamagingSlashObject(GameObject? gameObject, int? damage) {
        if (gameObject == null) {
            return;
        }

        Object.DestroyImmediate(gameObject.GetComponent<NailSlashTerrainThunk>());

        if (damage.HasValue) {
            AddDamageHeroComponent(gameObject, damage.Value);
        }
    }
}

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
        
        Logger.Info($"NeedleStrike animation effect for crest: {crestType}");

        if (crestType.IsHunter() || 
            crestType is CrestType.Reaper or CrestType.Witch or CrestType.Architect or CrestType.Shaman
        ) {
            Logger.Info("  Shared crests effect");
            GameObject? prefab;
            float activateTime;

            if (crestType.IsHunter()) {
                _chargeSlashBasic ??= _localAttacks.FindGameObjectInChildren("Charge Slash Basic");
                prefab = _chargeSlashBasic;

                activateTime = 0.16f;
            } else if (crestType is CrestType.Reaper) {
                _chargeSlashScythe ??= _localAttacks.FindGameObjectInChildren("Charge Slash Scythe");
                prefab = _chargeSlashScythe;

                activateTime = 0.3333333f;

                _reaperAudioEvent ??= GetOrFindNailArtsFsm().GetFirstAction<PlayAudioEvent>("Antic Rpr");
                AudioUtil.PlayAudioEventAtPlayerObject(_reaperAudioEvent, playerObject);
            } else if (crestType is CrestType.Witch) {
                _chargeSlashWitch ??= _localAttacks.FindGameObjectInChildren("Charge Slash Witch");
                prefab = _chargeSlashWitch;

                // Remove activation time if this is a loop of the Witch attack, those activate instantly
                activateTime = _witchLoop ? 0.0f : 0.08f;

                _witchAudioEvent ??= GetOrFindNailArtsFsm().GetFirstAction<PlayAudioEvent>("Begin Spin");

                if (!_witchLoop) {
                    AnimationUtil.ExecuteActionAfterDelay(
                        () => AudioUtil.PlayAudioEventAtPlayerObject(_witchAudioEvent, playerObject),
                        0.08f
                    );
                }
            } else if (crestType is CrestType.Architect) {
                // Architect crest "Slash_Charged" animation clip triggers
                // Frame 4 has triggerEvent, seconds until frame: 0.2777778
                // Frame 10 has triggerEvent, seconds until frame: 0.6111111

                _chargeSlashArchitect ??= _localAttacks.FindGameObjectInChildren("Charge Slash Toolmaster");
                prefab = _chargeSlashArchitect;

                activateTime = 0.2777778f;

                _architectAudioEvent ??= GetOrFindNailArtsFsm().GetFirstAction<PlayAudioEvent>("Drill Sfx");
                AnimationUtil.ExecuteActionAfterDelay(
                    () => AudioUtil.PlayAudioEventAtPlayerObject(_architectAudioEvent, playerObject),
                    0.2777778f
                );
            } else {
                _chargeSlashShaman ??= _localAttacks.FindGameObjectInChildren("Charge Slash Shaman");
                prefab = _chargeSlashShaman;

                activateTime = 0.2f;
            }

            if (prefab == null) {
                Logger.Warn("Prefab for Needle Strike effect is null in local attacks");
                return;
            }

            if (_witchLoop) {
                var strikeObj = playerAttacks.FindGameObjectInChildren("Needle Strike");
                if (strikeObj != null) {
                    strikeObj.SetActive(false);
                    strikeObj.SetActive(true);
                }
            } else {
                var strikeObj = Object.Instantiate(prefab, playerAttacks.transform);
                strikeObj.name = "Needle Strike";
                
                Object.Destroy(strikeObj.GetComponent<NailSlashTerrainThunk>());
                foreach (var comp in strikeObj.GetComponentsInChildren<NailSlashTerrainThunk>()) {
                    Object.DestroyImmediate(comp);
                }

                if (crestType is CrestType.Architect) {
                    Object.Destroy(strikeObj.GetComponent<KeepWorldPosition>());
                }

                AnimationUtil.ExecuteActionAfterDelay(() => {
                    strikeObj.SetActive(true);
                    
                    if (crestType is CrestType.Shaman) {
                        SlashBase.PlayNailSlashTravel(strikeObj, slashEffects.Contains(SlashBase.SlashEffect.Longclaw));
                    }
                }, activateTime);

                strikeObj.DestroyAfterTime(5f);
            }

            return;
        }

        if (crestType is CrestType.Wanderer) {
            _chargeSlashWanderer ??= _localAttacks.FindGameObjectInChildren("Charge Slash Wanderer");
            if (_chargeSlashWanderer == null) {
                Logger.Warn("Prefab for Needle Strike effect with Wanderer crest is null");
                return;
            }

            var strikeObj = Object.Instantiate(_chargeSlashWanderer, playerAttacks.transform);
            strikeObj.name = "Needle Strike";

            AnimationUtil.ExecuteActionAfterDelay(() => {
                strikeObj.SetActive(true);

                _wandererAudioEvent ??= GetOrFindNailArtsFsm().GetFirstAction<PlayAudioEvent>("Wanderer Attack");
                AudioUtil.PlayAudioEventAtPlayerObject(_wandererAudioEvent, playerObject);
            }, 0.1666667f);

            // TODO: check after how much time to destroy
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
                strikeObj.name = "Needle Strike";

                strikeObj.SetActive(true);

                _beastSlashAudioEvent ??= GetOrFindNailArtsFsm().GetFirstAction<PlayAudioEvent>("Warrior2 Slash");
                AudioUtil.PlayAudioEventAtPlayerObject(_beastSlashAudioEvent, playerObject);
            }, 0.4166667f);
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
}

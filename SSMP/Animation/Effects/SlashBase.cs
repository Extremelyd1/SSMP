using System;
using System.Collections;
using System.Collections.Generic;
using GlobalSettings;
using SSMP.Util;
using SSMP.Internals;
using SSMP.Networking.Packet;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects;

/// <summary>
/// Abstract base class for the animation effect of nail slashes.
/// </summary>
internal abstract class SlashBase : ParryableEffect {
    /// <inheritdoc/>
    public abstract override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo);

    /// <inheritdoc/>
    public override byte[] GetEffectInfo() {
        return GetSlashEffectInfo();
    }

    /// <summary>
    /// Get animation effect info for slashes. Also used by <see cref="NeedleStrike"/>.
    /// </summary>
    /// <returns>A byte array containing effect info.</returns>
    public static byte[] GetSlashEffectInfo() {
        var crestType = CrestTypeExt.FromInternal(PlayerData.instance.CurrentCrestID);
        var slashEffects = new HashSet<SlashEffect>();
        if (crestType == CrestType.Beast && HeroController.instance.warriorState.IsInRageMode) {
            slashEffects.Add(SlashEffect.BeastRageMode);
        }

        var currentElement = HeroController.instance.NailImbuement.CurrentElement;
        if (currentElement == NailElements.Fire) {
            slashEffects.Add(SlashEffect.Flintslate);
        } else if (currentElement == NailElements.Poison) {
            slashEffects.Add(SlashEffect.FlintslatePollip);
        }

        if (Gameplay.LongNeedleTool.IsEquipped) {
            slashEffects.Add(SlashEffect.Longclaw);
        }

        var packet = new Packet();
        packet.WriteBitFlag(slashEffects);

        return packet.ToArray();
    }

    /// <summary>
    /// Plays the slash animation for the given player.
    /// </summary>
    /// <param name="playerObject">The GameObject representing the player.</param>
    /// <param name="crestType">The type of crest the player is using.</param>
    /// <param name="effectInfo">A byte array containing effect info.</param>
    /// <param name="type">The type of nail slash.</param>
    protected void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo, SlashType type) {
        if (effectInfo == null || effectInfo.Length < 1) {
            Logger.Error("Could not get null or empty effect info for SlashBase");
            return;
        }
        
        var packet = new Packet(effectInfo);
        var slashEffects = packet.ReadBitFlag<SlashEffect>();

        Play(playerObject, type, crestType, slashEffects);
    }

    /// <summary>
    /// Plays the slash animation for the given player.
    /// </summary>
    /// <param name="playerObject">The GameObject representing the player.</param>
    /// <param name="type">The type of nail slash.</param>
    /// <param name="crestType">The type of crest used by the player.</param>
    /// <param name="slashEffects">Effects for the slash, such as the fire effect from Flintslate or being in bind
    /// mode from the Beast crest.</param>
    protected void Play(GameObject playerObject, SlashType type, CrestType crestType, ISet<SlashEffect> slashEffects) {
        var isInBeastRageMode = slashEffects.Contains(SlashEffect.BeastRageMode);

        if (!AnimationUtil.GetConfigsFromCrestType(
                crestType, 
                out var configGroup,
                out var overrideGroup,
                isInBeastRageMode
        ) || configGroup == null) {
            return;
        }

        var nailAttackBase = GetNailAttackBase(
            type, 
            crestType, 
            isInBeastRageMode, 
            configGroup, 
            overrideGroup
        );

        if (nailAttackBase == null) {
            Logger.Error("Cannot play animation with null NailSlash");
            return;
        }

        // Get the attacks gameObject from the player object
        var playerAttacks = playerObject.FindGameObjectInChildren("Attacks");
        if (playerAttacks == null) {
            Logger.Warn("Player object does not have player attacks child, cannot play slash");
            return;
        }

        var slashParent = new GameObject("Slash Parent");
        slashParent.transform.SetParent(playerAttacks.transform);
        slashParent.SetActive(false);
        slashParent.transform.localPosition = Vector3.zero;
        slashParent.transform.localScale = Vector3.one;

        // Instantiate the slash gameObject from the given prefab
        // and use the attack gameObject as transform reference
        var slashObj = Object.Instantiate(nailAttackBase.gameObject, slashParent.transform);

        var nailSlash = slashObj.GetComponent<NailSlash>();
        var downSpike = slashObj.GetComponent<Downspike>();
        var heroDownAttack = slashObj.GetComponent<HeroDownAttack>();
        var audio = slashObj.GetComponent<AudioSource>();
        var poly = slashObj.GetComponent<PolygonCollider2D>();
        var mesh = slashObj.GetComponent<MeshRenderer>();
        var anim = slashObj.GetComponent<tk2dSpriteAnimator>();

        string animName;
        Vector3 scale;
        if (nailSlash != null) {
            animName = nailSlash.animName;
            scale = nailSlash.scale;

            Object.DestroyImmediate(nailSlash);
        } else if (downSpike != null) {
            animName = downSpike.animName;
            scale = downSpike.scale;

            Object.DestroyImmediate(downSpike);
        } else {
            throw new InvalidOperationException("Both NailSlash and Downspike are null components on slash object");
        }

        // For some attacks in crests, down slashes and down spikes, this component exists which will interfere
        // So we destroy it immediately
        if (heroDownAttack) {
            Object.DestroyImmediate(heroDownAttack);
        }

        slashParent.SetActive(true);

        var relAudioSource = AudioUtil.GetAudioSourceObject(playerObject);
        relAudioSource.transform.parent = slashParent.transform;
        relAudioSource.clip = audio.clip;
        relAudioSource.Play();

        mesh.enabled = true;

        HandleSlashSpriteAnimation(anim, poly, mesh, crestType, slashParent, animName);

        var longclaw = slashEffects.Contains(SlashEffect.Longclaw);
        
        if (crestType == CrestType.Shaman) {
            var castEffectObjName = type switch {
                SlashType.Normal or SlashType.NormalAlt => "Shaman_blade_cast_effect Slash",
                SlashType.Down => "Shaman_blade_cast_effect DownSlash",
                SlashType.Up => "Shaman_blade_cast_effect UpSlash",
                SlashType.Wall => "Shaman_blade_cast_effect WallSlash",
                _ => null
            };
            
            if (castEffectObjName != null) {
                var castEffectObj = GetGameObjectInCrestObjectFromName(configGroup, castEffectObjName);
                if (castEffectObj != null) {
                    var newCastEffectObj = Object.Instantiate(castEffectObj, slashParent.transform);
                    newCastEffectObj.SetActive(true);
                }
            }
            
            PlayNailSlashTravel(slashObj, longclaw, slashParent);
        } else {
            // This method is in the else for the Shaman crest check, because Shaman crest handles Longclaw differently
            ApplyLongclawMultiplier(longclaw, type, slashObj, scale);
        }

        if (ServerSettings.IsPvpEnabled && ShouldDoDamage) {
            AddDamageHeroComponent(slashObj);
        }
        
        // TODO: nail imbued from NailAttackBase
    }

    /// <summary>
    /// Apply the Longclaw multiplier to the given slash object.
    /// </summary>
    /// <param name="longclaw">Whether Longclaw is equipped for this slash.</param>
    /// <param name="type">The type of the slash.</param>
    /// <param name="slashObj">The game object corresponding to the slash.</param>
    /// <param name="scale">The normal scale of the slash.</param>
    protected void ApplyLongclawMultiplier(bool longclaw, SlashType type, GameObject slashObj, Vector3 scale) {
        if (longclaw) {
            var multiplier = Gameplay.LongNeedleMultiplier;
            if (type == SlashType.Up) {
                multiplier = new Vector2(multiplier.y, multiplier.x);
            }

            slashObj.transform.localScale = new Vector3(scale.x * multiplier.x, scale.y * multiplier.y, scale.z);
        } else {
            slashObj.transform.localScale = scale;
        }
    }

    /// <summary>
    /// Gets the NailAttackBase component from the given parameters. This is then used as a template for playing
    /// the slash animation according to its behaviour. Can be overridden by extending classes to get a different
    /// base.
    /// </summary>
    /// <returns>A nullable NailAttackBase instance.</returns>
    protected virtual NailAttackBase? GetNailAttackBase(
        SlashType type,
        CrestType crestType,
        bool isInBeastRageMode,
        HeroController.ConfigGroup configGroup,
        HeroController.ConfigGroup? overrideGroup
    ) {
        // For some reason the animation for the normal slash is used with the alt slash NailSlash component and
        // vice versa. So for the first two slash types, the NailSlash component is switched.
        // This is also the case for the normal down slash and alt down slash for the Wanderer crest.
        switch (type) {
            case SlashType.Normal:
                return GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.AlternateSlash);
            case SlashType.NormalAlt:
                return GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.NormalSlash);
            case SlashType.Up:
                return GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.UpSlash);
            case SlashType.Wall:
                return GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.WallSlash);
            case SlashType.Down:
                switch (crestType) {
                    case CrestType.Wanderer:
                        return GetPropertyFromConfigGroup(
                            configGroup, overrideGroup, group => group.AltDownSlash);
                    case CrestType.Reaper:
                    case CrestType.Witch:
                    case CrestType.Architect:
                        return GetNailSlashInCrestObjectFromName(configGroup, "DownSlash New");
                    case CrestType.Beast:
                        return GetNailSlashInCrestObjectFromName(
                            configGroup,
                            isInBeastRageMode ? "SpinSlash Rage" : "SpinSlash"
                        );
                    case CrestType.Shaman:
                        return GetNailSlashInCrestObjectFromName(configGroup, "DownSlash");
                }

                break;
            case SlashType.DownAlt:
                return GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.DownSlash);
            case SlashType.DownSpike:
                return GetPropertyFromConfigGroup(configGroup, overrideGroup, group => group.Downspike);
            case SlashType.Dash:
                if (crestType == CrestType.Reaper) {
                    return GetNailSlashInCrestObjectFromName(
                        configGroup,
                        "DashUpper Slash"
                    );
                }

                if (crestType == CrestType.Beast) {
                    return GetNailSlashInCrestObjectFromName(
                        configGroup,
                        isInBeastRageMode ? "DashSlash Rage" : "DashSlash"
                    );
                }

                if (crestType == CrestType.Shaman) {
                    return GetNailSlashInCrestObjectFromName(
                        configGroup,
                        "DashSlash"
                    );
                }

                return GetPropertyFromConfigGroup<NailAttackBase>(
                    configGroup, 
                    overrideGroup, 
                    group => GetNailAttackBaseComponentFromObject(group.DashStab)
                );
            case SlashType.DashAlt:
                return GetPropertyFromConfigGroup<NailAttackBase>(
                    configGroup, 
                    overrideGroup, 
                    group => GetNailAttackBaseComponentFromObject(group.DashStabAlt)
                );
            default:
                Logger.Error($"Cannot play animation for unknown nail slash: {type}");
                break;
        }

        return null;
    }

    /// <summary>
    /// Separate method that handles only the sprite animation of the nail slash, which is overridden in
    /// <see cref="DownSpike"/>, which uses a slightly different way of playing and handling the sprite animation.
    /// </summary>
    protected virtual void HandleSlashSpriteAnimation(
        tk2dSpriteAnimator anim,
        PolygonCollider2D poly,
        MeshRenderer mesh,
        CrestType crestType,
        GameObject slashParent,
        string animName
    ) {
        var animTriggerCounter = 0;
        anim.AnimationEventTriggered = (_, _, _) => {
            ++animTriggerCounter;
            if (animTriggerCounter == 1) {
                poly.enabled = true;
            }

            if (animTriggerCounter == 2) {
                poly.enabled = false;
            }
        };
        anim.AnimationCompleted = (_, _) => {
            poly.enabled = false;
            mesh.enabled = false;
            anim.AnimationEventTriggered = null;

            if (crestType != CrestType.Shaman) {
                Object.Destroy(slashParent);
            }
        };

        var clipByName = anim.GetClipByName(animName);
        // TODO: FPS increase by Quickening from NailSlash
        anim.Play(clipByName, Mathf.Epsilon, clipByName.fps);
    }

    /// <summary>
    /// Get the <see cref="NailAttackBase"/> component from the given game object. Can be either a
    /// <see cref="NailSlash"/> or <see cref="DashStabNailAttack"/>.
    /// </summary>
    /// <param name="gameObj"></param>
    /// <returns></returns>
    protected NailAttackBase? GetNailAttackBaseComponentFromObject(GameObject gameObj) {
        if (gameObj == null) {
            return null;
        }

        var dashStabNailAttack = gameObj.GetComponent<DashStabNailAttack>();
        if (dashStabNailAttack != null) {
            return dashStabNailAttack;
        }

        return gameObj.GetComponent<NailSlash>();
    }

    /// <summary>
    /// Play the nail slash travel animation that is used for the movement of the nail slash when having the Shaman
    /// crest equipped. This is a coroutine that mimics a lot of the functionality of the NailSlashTravel class, but
    /// leaves out recoil and other player impacting behaviour.
    /// </summary>
    /// <param name="slashObj">The base slash object that should have the NailSlashTravel component.</param>
    /// <param name="longclaw">Whether Longclaw is equipped and thus the Nail Slash should travel further.</param>
    /// <param name="slashParent">The slash parent, so we can destroy it later. Or null, if we should destroy the
    /// <paramref name="slashObj"/> instead.</param>
    public static void PlayNailSlashTravel(GameObject slashObj, bool longclaw, GameObject? slashParent = null) {
        MonoBehaviourUtil.Instance.StartCoroutine(Play());
        return;

        IEnumerator Play() {
            var travelComp = slashObj.GetComponent<NailSlashTravel>();

            travelComp.hasStarted = true;

            if (travelComp.particles) {
                travelComp.particles.Play(true);
            }

            yield return null;

            var transform = travelComp.transform;
            var worldPos = (Vector2) transform.position;

            if (travelComp.distanceFiller) {
                travelComp.distanceFiller.gameObject.SetActive(true);
            }

            travelComp.SetThunkerActive(true);

            for (var elapsed = 0f; elapsed < travelComp.travelDuration; elapsed += Time.deltaTime) {
                travelComp.elapsedT = elapsed / travelComp.travelDuration;

                // setPosition action in NailSlashTravel.cs
                var self = travelComp.travelDistance.MultiplyElements(
                    new Vector2(Mathf.Sign(transform.lossyScale.x), 1f)
                );
                if (longclaw) {
                    self = self.MultiplyElements(Gameplay.LongNeedleMultiplier);
                }

                var num = travelComp.travelCurve.Evaluate(travelComp.elapsedT);

                transform.SetPosition2D(worldPos + self * num);

                if (travelComp.distanceFiller) {
                    var newXScale = Mathf.Abs(self.x) * num;
                    travelComp.distanceFiller.SetScaleX(newXScale);
                    travelComp.distanceFiller.SetLocalPositionX(newXScale * 0.5f);
                }

                yield return null;
            }

            // TODO: this is a bit of a hack, but we only want to destroy the slash parent once the particles are done
            while (travelComp.particles && travelComp.particles.isPlaying) {
                yield return new WaitForSeconds(0.5f);
            }

            Object.Destroy(slashParent == null ? slashObj : slashParent);
        }
    }

    /// <summary>
    /// Get a property from either the given config group or its override group based on whether the override group
    /// has it defined. This is similar to behaviour in HeroController.
    /// </summary>
    /// <param name="configGroup">The normal config group to get the property from.</param>
    /// <param name="overrideGroup">The override config group to get the property from.</param>
    /// <param name="getPropertyFunc">The function to get the property given either config group.</param>
    /// <typeparam name="T">The type of the property to get.</typeparam>
    /// <returns>A nullable instance of the property.</returns>
    protected static T? GetPropertyFromConfigGroup<T>(
        HeroController.ConfigGroup configGroup,
        HeroController.ConfigGroup? overrideGroup,
        Func<HeroController.ConfigGroup, T?> getPropertyFunc
    ) {
        // If the override group is null, we get the value from the property from the normal group
        if (overrideGroup == null) {
            return getPropertyFunc.Invoke(configGroup);
        }

        // Get the value from the override group to check if it is null or not
        var overrideGroupValue = getPropertyFunc.Invoke(overrideGroup);
        if (overrideGroupValue == null) {
            // It is null, so we get the value from the normal group
            return getPropertyFunc.Invoke(configGroup);
        }

        // It is not null, so we return it
        return overrideGroupValue;
    }

    /// <summary>
    /// Get the NailSlash component in a crest based on the given config group and the name of the game object that has
    /// the NailSlash component.
    /// </summary>
    /// <param name="configGroup">The config group for the crest.</param>
    /// <param name="gameObjectName">The name of the game object that has the component.</param>
    /// <returns>A nullable NailSlash component.</returns>
    private NailSlash? GetNailSlashInCrestObjectFromName(HeroController.ConfigGroup configGroup, string gameObjectName) {
        var nailSlashObj = GetGameObjectInCrestObjectFromName(configGroup, gameObjectName);
        if (nailSlashObj == null) {
            return null;
        }

        return nailSlashObj.GetComponent<NailSlash>();
    }

    /// <summary>
    /// Get the game object in a crest based on the given config group and the name of the game object.
    /// </summary>
    /// <param name="configGroup">The config group for the crest.</param>
    /// <param name="gameObjectName">The name of the game object.</param>
    /// <returns>A nullable game object.</returns>
    private GameObject? GetGameObjectInCrestObjectFromName(HeroController.ConfigGroup configGroup, string gameObjectName) {
        var normalSlash = configGroup.NormalSlash;
        if (normalSlash == null) {
            Logger.Error("NormalSlash in crest config group is null");
            return null;
        }

        var normalSlashObj = normalSlash.gameObject;
        if (normalSlashObj == null) {
            Logger.Error("NormalSlash game object in crest config group is null");
            return null;
        }

        var crestObj = normalSlashObj.transform.parent.gameObject;
        if (crestObj == null) {
            Logger.Error("Crest game object as parent of slash is null");
            return null;
        }

        var gameObjToFind = crestObj.FindGameObjectInChildren(gameObjectName);
        if (gameObjToFind == null) {
            Logger.Error("Game object as child of crest object is null");
            return null;
        }

        return gameObjToFind;
    }

    /// <summary>
    /// Enumeration of slash types.
    /// </summary>
    public enum SlashType {
        Normal,
        NormalAlt,
        Down,
        DownAlt,
        DownSpike,
        Up,
        Wall,
        Dash,
        DashAlt,
    }

    /// <summary>
    /// Enumeration of slash effects.
    /// </summary>
    public enum SlashEffect {
        BeastRageMode,
        Longclaw,
        Flintslate,
        FlintslatePollip,
    }
}

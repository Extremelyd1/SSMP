using System.Collections.Generic;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Animation.Effects;

/// <summary>
/// Class for the animation effect of a bind (healing) finishing.
/// </summary>
internal class BindBurst : Bind {
    /// <summary>
    /// Static instance for access by multiple animation clips in <see cref="AnimationManager"/>.
    /// </summary>
    private static BindBurst? _instance;

    /// <inheritdoc cref="_instance" />
    public static BindBurst Instance => _instance ??= new BindBurst();

    /// <summary>
    /// A set of players who recently binded while maggoted
    /// </summary>
    public static readonly HashSet<int> MaggotedPlayers = new();

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Set maggot info
        var flags = new Flags(effectInfo);

        var playerObjectIdentifier = playerObject.GetInstanceID();
        if (MaggotedPlayers.Contains(playerObjectIdentifier)) {
            flags.Maggoted = true;
            MaggotedPlayers.Remove(playerObjectIdentifier);
        }

        if (!CreateObjects(playerObject, out var bindEffects)) {
            return;
        }

        if (flags.BaseMirror || flags.UpgradedMirror) PlayMirror(playerObject, flags.UpgradedMirror);
        if (flags.Maggoted) PlayMaggotCleanse(bindEffects, playerObject);

        // Stop regardless of if it's on or not
        StopBindBell(bindEffects);

        // Play crest-specific animations
        switch (crestType) {
            case CrestType.Beast:
                if (!flags.Maggoted) {
                    PlayBeastRage(bindEffects);
                }
                break;
            case CrestType.Witch:
                if (!flags.Maggoted) {
                    PlayWitchEnd(bindEffects);
                } else {
                    PlayWitchMaggoted(bindEffects);
                }
                return;
            case CrestType.Shaman:
                PlayShamanEnd(bindEffects);
                break;
        }

        PlayNormalEnd(bindEffects);
    }

    /// <summary>
    /// Plays the maggot cleanse animation
    /// </summary>
    private void PlayMaggotCleanse(GameObject bindEffects, GameObject playerObject) {
        Logger.Info("Playing Maggot Animation");
        var maggotBurst = GetOrFindBindFsm().GetAction<SpawnObjectFromGlobalPool>("Maggoted?", 2);
        var maggotFlash = GetOrFindBindFsm().GetAction<SpawnObjectFromGlobalPool>("Maggoted?", 4);
        var maggotAudio = GetOrFindBindFsm().GetFirstAction<PlayAudioEvent>("Maggoted?");

        EffectUtils.SpawnGlobalPoolObject(maggotBurst, bindEffects.transform, 5f);
        EffectUtils.SpawnGlobalPoolObject(maggotFlash, bindEffects.transform, 5f);

        if (maggotAudio != null) {
            PlaySound(playerObject, maggotAudio);
        }
    }

    /// <summary>
    /// Creates the appropriate Claw Mirror object.
    /// The object will be destroyed after it finishes.
    /// </summary>
    private GameObject? PrepareMirror(GameObject playerObject, SetGameObject mirrorSource) {

        var mirror = EffectUtils.SpawnGlobalPoolObject(mirrorSource.gameObject.Value, playerObject.transform, 3f);

        if (mirror == null) {
            return null;
        }

        var shaker = mirror.GetComponentInChildren<CameraControlAnimationEvents>();
        if (shaker != null) {
            Component.DestroyImmediate(shaker);
        }

        var haze = mirror.FindGameObjectInChildren("haze2");
        if (haze != null) {
            GameObject.Destroy(haze);
        }

        return mirror;
    }

    /// <summary>
    /// Plays the appropriate Claw Mirror animation.
    /// Adds a damage component if appropriate.
    /// </summary>
    private void PlayMirror(GameObject playerObject, bool upgraded) {
        // Get claw prefabs
        Logger.Debug("Playing Claw Mirror Animation");
        var regularClaw = GetOrFindBindFsm().GetAction<SetGameObject>("Dazzle?", 3)!;
        var upgradedClaw = GetOrFindBindFsm().GetAction<SetGameObject>("Dazzle?", 4)!;

        // Create the claw
        GameObject? claw;
        if (upgraded) claw = PrepareMirror(playerObject, upgradedClaw);
        else claw = PrepareMirror(playerObject, regularClaw);

        if (claw == null) {
            return;
        }

        claw.SetActive(true);
        
        // Add hitbox if appropriate
        if (ServerSettings.IsPvpEnabled && ShouldDoDamage) {
            var damagerParent = claw.FindGameObjectInChildren("Trobbio_dazzle_flash");
            var damager = damagerParent?.FindGameObjectInChildren("hero_dazzle_flash_damager");
            if (damager == null) {
                Logger.Warn("Couldn't find claw mirror damager");
                return;
            }

            damager.layer = (int) GlobalEnums.PhysLayers.HERO_ATTACK;

            var damageComponent = AddDamageHeroComponent(damager);
            damageComponent.hazardType = GlobalEnums.HazardType.EXPLOSION;
        }
    }

    /// <summary>
    /// Stops the bind bell animation
    /// </summary>
    public void StopBindBell(GameObject bindEffects) {
        var bindBell = bindEffects.FindGameObjectInChildren(BindBellName);
        bindBell?.SetActive(false);
    }

    /// <summary>
    /// Plays the Beast Crest specific rage animation
    /// </summary>
    private void PlayBeastRage(GameObject bindEffects) {
        var beastRage = CreateEffectIfNotExists(bindEffects, "crest rage_burst_effect(Clone)");
        beastRage?.SetActive(true);
    }

    /// <summary>
    /// Plays the witch maggot cleanse animation
    /// </summary>
    private void PlayWitchMaggoted(GameObject bindEffects) {
        var maggotCleanse = CreateEffectIfNotExists(bindEffects, "Witch Bind Maggot Cleanse");
        if (maggotCleanse != null) {
            maggotCleanse.SetActive(false);
            maggotCleanse.SetActive(true);
        }
    }

    /// <summary>
    /// Plays the Witch Crest tentancles animation.
    /// Yes it's called Tentancles internally. Thanks TC.
    /// </summary>
    private void PlayWitchEnd(GameObject bindEffects) {
        var witchBind = bindEffects.FindGameObjectInChildren("Witch Bind");
        if (witchBind == null) {
            var localWitchBind = LocalBindEffects?.FindGameObjectInChildren("Witch Bind");
            if (localWitchBind == null) {
                Logger.Warn("Unable to find local Witch Bind object");
                return;
            }

            witchBind = GameObject.Instantiate(localWitchBind, bindEffects.transform);

            var shaker = witchBind.GetComponent<CameraControlAnimationEvents>();
            if (shaker != null) {
                Component.DestroyImmediate(shaker);
            }

            SetWitchDamagers(witchBind);
        }

        var audio = GetOrFindBindFsm().GetFirstAction<PlayAudioEvent>("Witch Tentancles!");
        if (audio != null) {
            PlaySound(bindEffects.transform.parent.gameObject, audio);
        }

        witchBind.SetActive(false);
        witchBind.SetActive(true);
    }

    /// <summary>
    /// Adds or removes hero damage components from Witch Crest bind
    /// </summary>
    private void SetWitchDamagers(GameObject witchBind) {
        for (var i = 0; i < witchBind.transform.childCount; i++) {
            var child = witchBind.transform.GetChild(i);
            if (!child.name.StartsWith("Damager")) {
                continue;
            }

            SetDamageHeroState(child.gameObject);
        }
    }

    /// <summary>
    /// Stops the Shaman Crest specific silk animation
    /// </summary>
    private void PlayShamanEnd(GameObject bindEffects) {
        var shamanAntic = bindEffects.FindGameObjectInChildren("Shaman_Bind_antic_silk");
        if (shamanAntic == null) {
            return;
        }
        shamanAntic.SetActive(false);
    }

    /// <summary>
    /// Plays particles and shows a flash
    /// </summary>
    private void PlayNormalEnd(GameObject bindEffects) {
        Logger.Info("Playing Heal Particles and Anim");
        var healParticle = CreateEffectIfNotExists(bindEffects, "Pt Heal");
        if (healParticle != null) {
            healParticle.GetComponent<ParticleSystem>().Play();
        }
        
        var healAnim = CreateEffectIfNotExists(bindEffects, "Heal Anim");
        if (healAnim != null) {
            healAnim.SetActive(true);
        }

        var bindSilkObj = bindEffects.FindGameObjectInChildren("Bind Silk");
        if (bindSilkObj != null) {
            var bindSilkMeshRenderer = bindSilkObj.GetComponent<MeshRenderer>();
            bindSilkMeshRenderer.enabled = false;
        }

        var audio = GetOrFindBindFsm().GetFirstAction<AudioPlayerOneShotSingle>("Bind Burst");
        if (audio != null) {
            PlaySound(bindEffects.transform.parent.gameObject, audio);
        }


    }
}

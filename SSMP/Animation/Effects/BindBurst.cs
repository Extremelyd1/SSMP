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
    // Effect object names
    private const string BeastCrestRageObjectName = "crest rage_burst_effect(Clone)";
    private const string WitchBindCleanseObjectName = "Witch Bind Maggot Cleanse";
    private const string WitchBindObjectName = "Witch Bind";
    private const string HealAnimObjectName = "Heal Anim";
    private const string HealParticleObjectName = "Pt Heal";

    /// <summary>
    /// Static instance for access by multiple animation clips in <see cref="AnimationManager"/>.
    /// </summary>
    public static readonly BindBurst Instance = new();

    /// <summary>
    /// A set of players who recently bound while maggoted
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

        // Play flag-specific effects
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
        // Grab assets from Maggoted state
        var stateName = "Maggoted?";
        var maggotBurst = GetOrFindBindFsm().GetAction<SpawnObjectFromGlobalPool>(stateName, 2);
        var maggotFlash = GetOrFindBindFsm().GetAction<SpawnObjectFromGlobalPool>(stateName, 4);
        var maggotAudio = GetOrFindBindFsm().GetFirstAction<PlayAudioEvent>(stateName);

        // Spawn copies of those assets
        EffectUtils.SpawnGlobalPoolObject(maggotBurst, bindEffects.transform, 5f);
        EffectUtils.SpawnGlobalPoolObject(maggotFlash, bindEffects.transform, 5f);

        // Play audio
        if (maggotAudio != null) {
            PlaySound(playerObject, maggotAudio);
        }
    }

    /// <summary>
    /// Creates the appropriate Claw Mirror object.
    /// The object will be destroyed after it finishes.
    /// </summary>
    private static GameObject? PrepareMirror(GameObject playerObject, SetGameObject mirrorSource) {
        // Spawn mirror
        var mirror = EffectUtils.SpawnGlobalPoolObject(mirrorSource.gameObject.Value, playerObject.transform, 3f);

        if (mirror == null) {
            return null;
        }

        // Remove camera and haze components
        var shaker = mirror.GetComponentInChildren<CameraControlAnimationEvents>();
        if (shaker != null) {
            Object.DestroyImmediate(shaker);
        }

        mirror.DestroyGameObjectInChildren("haze2");

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
        if (upgraded) {
            claw = PrepareMirror(playerObject, upgradedClaw);
        } else {
            claw = PrepareMirror(playerObject, regularClaw);
        }

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
    public static void StopBindBell(GameObject bindEffects) {
        // Only bother turning it off if it's on
        var bindBell = bindEffects.FindGameObjectInChildren(BindBellObjectName);
        bindBell?.SetActive(false);
    }

    /// <summary>
    /// Plays the Beast Crest specific rage animation
    /// </summary>
    private void PlayBeastRage(GameObject bindEffects) {
        // Create and reactivate the rage effect
        var beastRage = CreateEffectIfNotExists(bindEffects, BeastCrestRageObjectName);

        if (beastRage != null) {
            beastRage.SetActive(false);
            beastRage.SetActive(true);
        }
    }

    /// <summary>
    /// Plays the witch maggot cleanse animation
    /// </summary>
    private void PlayWitchMaggoted(GameObject bindEffects) {
        // Spawn in the special tentacles
        var maggotCleanse = CreateEffectIfNotExists(bindEffects, WitchBindCleanseObjectName);

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
        // Get bind effect
        var effectWasCreated = CreateEffectIfNotExists(bindEffects, WitchBindObjectName, out var witchBind);
        if (witchBind == null) {
            return;
        }

        // Remove camera controls if object was created
        if (effectWasCreated) {
            var shaker = witchBind.GetComponent<CameraControlAnimationEvents>();
            if (shaker != null) {
                Object.DestroyImmediate(shaker);
            }
        }

        // Toggle damage depending on if PVP is on or not
        SetWitchDamagers(witchBind);

        // Play tentacles audio
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
        // Loop through all children, looking for all the ones called "Damager"
        for (var i = 0; i < witchBind.transform.childCount; i++) {
            var child = witchBind.transform.GetChild(i);
            if (!child.name.StartsWith("Damager")) {
                continue;
            }

            // Add or remove damage component from Damager object
            SetDamageHeroState(child.gameObject);
        }
    }

    /// <summary>
    /// Stops the Shaman Crest specific silk animation
    /// </summary>
    private static void PlayShamanEnd(GameObject bindEffects) {
        var shamanAntic = bindEffects.FindGameObjectInChildren(ShamanFallAnticObjectName);
        if (shamanAntic == null) {
            return;
        }
        shamanAntic.SetActive(false);
    }

    /// <summary>
    /// Plays particles and shows a flash. Happens at the end of a normal bind.
    /// </summary>
    private void PlayNormalEnd(GameObject bindEffects) {
        // Play particle effect
        CreateEffectIfNotExists(bindEffects, HealParticleObjectName, out var healParticle);
        if (healParticle != null) {
            healParticle.GetComponent<ParticleSystem>().Play();
        }
        
        // Play silk animation
        var healAnim = CreateEffectIfNotExists(bindEffects, HealAnimObjectName);
        if (healAnim != null) {
            healAnim.SetActive(true);
        }

        // Disable mesh renderer because the game does that. Is this needed?
        var bindSilkObj = bindEffects.FindGameObjectInChildren(BindSilkObjectName);
        if (bindSilkObj != null) {
            var bindSilkMeshRenderer = bindSilkObj.GetComponent<MeshRenderer>();
            bindSilkMeshRenderer.enabled = false;
        }

        // Play audio
        var audio = GetOrFindBindFsm().GetFirstAction<AudioPlayerOneShotSingle>("Bind Burst");
        if (audio != null) {
            PlaySound(bindEffects.transform.parent.gameObject, audio);
        }
    }
}

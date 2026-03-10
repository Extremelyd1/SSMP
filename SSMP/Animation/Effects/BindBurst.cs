using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects;

internal class BindBurst : Bind {
    /// <summary>
    /// Static instance for access by multiple animation clips in <see cref="AnimationManager"/>.
    /// </summary>
    private static BindBurst? _instance;
    /// <inheritdoc cref="_instance" />
    public static BindBurst Instance => _instance ??= new BindBurst();
    public static HashSet<ushort> MaggotedPlayers = new HashSet<ushort>();
    public override void Play(GameObject playerObject, CrestType crestType, ushort playerId, byte[]? effectInfo) {
        Flags flags = new Flags(effectInfo);
        if (MaggotedPlayers.Contains(playerId)) {
            flags.Maggoted = true;
            MaggotedPlayers.Remove(playerId);
        }
        MonoBehaviourUtil.Instance.StartCoroutine(PlayBindBurstEffect(playerObject, crestType, flags));
    }

    private IEnumerator PlayBindBurstEffect(GameObject playerObject, CrestType crestType, Flags flags) {
        if (!CreateObjects(playerObject, out var bindEffects)) {
            yield break;
        }

        if (flags.BaseMirror || flags.UpgradedMirror) PlayMirror(playerObject, flags.UpgradedMirror);
        if (flags.Maggoted) PlayMaggotCleanse(bindEffects, playerObject);
        
        // Stop regardless of if its on or not
        StopBindBell(bindEffects);

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
                break;
            case CrestType.Shaman:
                PlayShamanEnd(bindEffects);
                break;
            default:
                break;
        }

        PlayNormalEnd(bindEffects);
    }

    /// <summary>
    /// Plays the maggot cleanse animation
    /// </summary>
    protected void PlayMaggotCleanse(GameObject bindEffects, GameObject playerObject) {
        Logger.Info("Playing Maggot Animation");
        var maggotBurst = GetOrFindBindFsm().GetAction<SpawnObjectFromGlobalPool>("Maggoted?", 2);
        var maggotFlash = GetOrFindBindFsm().GetAction<SpawnObjectFromGlobalPool>("Maggoted?", 4);
        var maggotAudio = GetOrFindBindFsm().GetFirstAction<PlayAudioEvent>("Maggoted?");

        if (maggotBurst != null) {
            maggotBurst.gameObject.Value.Spawn(bindEffects.transform, Vector3.zero);
        }
        if (maggotFlash != null) {
            maggotFlash.gameObject.Value.Spawn(bindEffects.transform, Vector3.zero);
        }
        if (maggotAudio != null) {
            AudioUtil.PlayAudioEventAtPlayerObject(
                maggotAudio,
                playerObject
            );
        }
    }

    /// <summary>
    /// Creates the appropriate Claw Mirror object.
    /// The object will be destroyed after it finishes.
    /// </summary>
    private GameObject? PrepareMirror(GameObject playerObject, SetGameObject mirrorSource, string name) {

        if (mirrorSource == null) {
            Logger.Warn("Unable to find mirror source");
            return null;
        }

        // This is ugly and i hate it, but it works
        var mirror = GameObject.Instantiate(mirrorSource.gameObject.Value, playerObject.transform);
        mirror.transform.SetParent(null);
        mirror.transform.position = playerObject.transform.position;
        mirror.SetActive(true);

        if (mirror == null) {
            Logger.Warn("Unable to spawn mirror");
            return null;
        }
        mirror.name = name;

        var shaker = mirror.GetComponentInChildren<CameraControlAnimationEvents>();
        if (shaker != null) {
            Component.DestroyImmediate(shaker);
        }

        EffectUtils.SafelyRemoveAutoRecycle(mirror);
        mirror.DestroyAfterTime(2f);

        var haze = mirror.FindGameObjectInChildren("haze2");
        if (haze != null) {
            GameObject.Destroy(haze);
        }

        //mirror.SetActive(false);
        return mirror;
    }

    /// <summary>
    /// Plays the appropriate Claw Mirror animation.
    /// Adds a damage component if appropriate.
    /// </summary>
    private void PlayMirror(GameObject playerObject, bool upgraded) {
        Logger.Info("Playing Claw Mirror Animation");
        var regularClaw = GetOrFindBindFsm().GetAction<SetGameObject>("Dazzle?", 3)!;
        var upgradedClaw = GetOrFindBindFsm().GetAction<SetGameObject>("Dazzle?", 4)!;

        GameObject? claw;
        if (upgraded) claw = PrepareMirror(playerObject, upgradedClaw, "dazzle_upgraded");
        else claw = PrepareMirror(playerObject, regularClaw, "dazzle_regular");

        if (claw == null) {
            Logger.Warn("Unable to create claw mirror object.");
            return;
        }

        claw.SetActive(true);
        if (ServerSettings.IsPvpEnabled && ShouldDoDamage) {
            var damagerParent = claw.FindGameObjectInChildren("Trobbio_dazzle_flash");
            var damager = damagerParent?.FindGameObjectInChildren("hero_dazzle_flash_damager");
            if (damager != null) {
                damager.layer = (int) GlobalEnums.PhysLayers.HERO_ATTACK;
                AddDamageHeroComponent(damager);
            } else {
                Logger.Warn("Couldn't find claw mirror damager");
            }
        }
    }

    /// <summary>
    /// Stops the bind bell animation
    /// </summary>
    public void StopBindBell(GameObject bindEffects) {
        var bindBell = bindEffects.FindGameObjectInChildren(BIND_BELL_NAME);
        if (bindBell != null) {
            bindBell.SetActive(false);
        }
    }

    /// <summary>
    /// Plays the Beast Crest specific rage animation
    /// </summary>
    private void PlayBeastRage(GameObject bindEffects) {
        var beastRage = CreateEffectIfNotExists(bindEffects, "crest rage_burst_effect(Clone)");
        beastRage?.SetActive(true);
    }

    private void PlayWitchMaggoted(GameObject bindEffects) {
        var maggotCleanse = CreateEffectIfNotExists(bindEffects, "Witch Bind Maggot Cleanse");
        if (maggotCleanse != null) {
            maggotCleanse.SetActive(false);
            maggotCleanse.SetActive(true);
        }
    }

    private void PlayWitchEnd(GameObject bindEffects) {
        var witchBind = bindEffects.FindGameObjectInChildren("Witch Bind");
        if (witchBind == null) {
            var localWitchBind = _localBindEffects.FindGameObjectInChildren("Witch Bind");
            if (localWitchBind == null) {
                Logger.Warn("Unable to find local Witch Bind object");
                return;
            }

            witchBind = GameObject.Instantiate(localWitchBind, bindEffects.transform);

            var shaker = witchBind.GetComponent<CameraControlAnimationEvents>();
            if (shaker != null) {
                Component.DestroyImmediate(shaker);
            }

            if (ServerSettings.IsPvpEnabled && ShouldDoDamage) {
                SetWitchDamagers(witchBind);
            }
        }

        witchBind.SetActive(false);
        witchBind.SetActive(true);
    }

    private void SetWitchDamagers(GameObject witchBind) {
        for (int i = 0; i < witchBind.transform.childCount; i++) {
            var child = witchBind.transform.GetChild(i);
            if (!child.name.StartsWith("Damager")) {
                continue;
            }

            AddDamageHeroComponent(child.gameObject);
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
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects;

internal class BindBurst : Bind {
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        Flags flags = new Flags(effectInfo);
        MonoBehaviourUtil.Instance.StartCoroutine(PlayBindBurstEffect(playerObject, crestType, flags));
    }

    private IEnumerator PlayBindBurstEffect(GameObject playerObject, CrestType crestType, Flags flags) {
        if (!CreateObjects(playerObject, out var bindEffects)) {
            yield break;
        }

        if (flags.BaseMirror || flags.UpgradedMirror) PlayMirror(bindEffects, flags.UpgradedMirror);
        if (flags.BindBell) StopBindBell(bindEffects);
        if (flags.Maggoted) PlayMaggotCleanse(bindEffects, playerObject);

        switch (crestType) {
            case CrestType.Beast:
                PlayBeastRage(bindEffects);
                break;
            case CrestType.Shaman:
                PlayShamanEnd(bindEffects);
                break;
            default:
                break;
        }

        PlayNormalEnd(bindEffects);
    }

    private GameObject? PrepareMirror(GameObject bindEffects, SetGameObject mirrorSource, string name) {
        var mirror = bindEffects.FindGameObjectInChildren(name);
        if (mirror != null) {
            mirror.SetActive(false);
            return mirror;
        }

        if (mirrorSource == null) {
            Logger.Warn("Unable to find mirror source");
            return null;
        }

        mirror = mirrorSource.gameObject.Value.Spawn(bindEffects.transform, Vector3.zero);
        if (mirror == null) {
            Logger.Warn("Unable to spawn mirror");
            return null;
        }
        mirror.name = name;

        var shaker = mirror.GetComponentInChildren<CameraControlAnimationEvents>();
        if (shaker != null) {
            Component.DestroyImmediate(shaker);
        }
        var delay = mirror.AddComponentIfNotPresent<DeactivateAfterDelay>();
        delay.time = 2f;
        EffectUtils.SafelyRemoveAutoRecycle(mirror);
        var haze = mirror.FindGameObjectInChildren("haze2");
        if (haze != null) {
            GameObject.Destroy(haze);
        }

        mirror.SetActive(false);
        return mirror;
    }

    private void PlayMirror(GameObject bindEffects, bool upgraded) {
        Logger.Info("Playing Claw Mirror Animation");
        var regularClaw = GetOrFindBindFsm().GetAction<SetGameObject>("Dazzle?", 3)!;
        var upgradedClaw = GetOrFindBindFsm().GetAction<SetGameObject>("Dazzle?", 4)!;

        GameObject? claw;
        if (upgraded) claw = PrepareMirror(bindEffects, upgradedClaw, "dazzle_upgraded");
        else claw = PrepareMirror(bindEffects, regularClaw, "dazzle_regular");

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
        } else {
            var damager = claw.GetComponentInChildren<DamageHero>(true);
            if (damager != null) {
                Component.Destroy(damager);
            }
        }
    }

    private void StopBindBell(GameObject bindEffects) {
        var bindBell = bindEffects.FindGameObjectInChildren(BIND_BELL_NAME);
        if (bindBell != null) {
            bindBell.SetActive(false);
        }
    }

    private void PlayMaggotCleanse(GameObject bindEffects, GameObject playerObject) {
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

    private void PlayBeastRage(GameObject bindEffects) {
        var beastRage = CreateEffectIfNotExists(bindEffects, "crest rage_burst_effect(Clone)");
        beastRage?.SetActive(true);
    }

    private void PlayShamanEnd(GameObject bindEffects) {
        var shamanAntic = bindEffects.FindGameObjectInChildren("Shaman_Bind_antic_silk");
        if (shamanAntic == null) {
            return;
        }
        shamanAntic.SetActive(false);
    }

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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

internal class SharpDart : BaseSilkSkill {
    private const string DashBurstName = "Silk Charge DashBurst";
    private const string ZapParticlesName = "Silk Charge Particles Zap";
    private const string DashDamagerName = "Silk Charge Damager";

    public bool Volt = false;

    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var isShaman = crestType == CrestType.Shaman;
        MonoBehaviourUtil.Instance.StartCoroutine(PlayEffect(playerObject, isShaman));
    }

    private IEnumerator PlayEffect(GameObject playerObject, bool isShaman) {
        if (TryGetDamager(playerObject, out var damager)) {
            SetDamageHeroState(damager);
            damager.SetActive(true);

            var rune = damager.FindGameObjectInChildren("Shaman Rune");
            if (rune) {
                rune.SetActive(false);
                rune.SetActive(isShaman);
            }
        }

        if (TryGetDashBurst(playerObject, out var dashBurst)) {
            dashBurst.SetActive(false);
            dashBurst.SetActive(true);
        }

        if (Volt && TryGetParticles(playerObject, out var particles)) {
            particles.SetActive(false);
            particles.SetActive(true);
        }

        // Play sound effects
        PlayHornetAttackSound(playerObject);

        var fsm = GetSkillFSM();
        var chargeAntic = fsm.GetAction<PlayAudioEvent>("Silk Charge Begin", 5);
        if (chargeAntic != null) AudioUtil.PlayAudio(chargeAntic, playerObject);
        
        if (Volt) {
            var voltNoise = fsm.GetFirstAction<PlayAudioEvent>("Silk Charge Zap FX");
            if (voltNoise != null) AudioUtil.PlayAudio(voltNoise, playerObject);
        }

        // Brief pause for ending sound
        yield return new WaitForSeconds(0.2f);

        var chargeFull = fsm.GetAction<PlayAudioEvent>("Silk Charge Start", 12);
        if (chargeFull != null) AudioUtil.PlayAudio(chargeFull, playerObject);
    }

    private bool TryGetDashBurst(GameObject playerObject, [MaybeNullWhen(false)] out GameObject dashBurst) {
        // Find existing object
        var attacks = GetPlayerSilkAttacks(playerObject);
        dashBurst = attacks.FindGameObjectInChildren(DashBurstName);
        if (dashBurst) {
            return true;
        }

        // Copy from local attacks
        if (!TryGetLocalSilkAttacks(out var localSilkAttacks)) {
            return false;
        }

        var localDashBurst = localSilkAttacks.FindGameObjectInChildren(DashBurstName);
        if (!localDashBurst) {
            return false;
        }

        dashBurst = Object.Instantiate(localDashBurst, attacks.transform);
        dashBurst.name = DashBurstName;
        dashBurst.transform.localScale = new Vector3(0.75f, 0.75f, 0.75f);

        return true;
    }

    private bool TryGetDamager(GameObject playerObject, [MaybeNullWhen(false)] out GameObject damager) {
        // Find existing object
        var attacks = GetPlayerSilkAttacks(playerObject);
        damager = attacks.FindGameObjectInChildren(DashDamagerName);
        if (damager) {
            return true;
        }

        // Copy from local attacks
        if (!TryGetLocalSilkAttacks(out var localSilkAttacks)) {
            return false;
        }

        var localDamager = localSilkAttacks.FindGameObjectInChildren(DashDamagerName);
        if (!localDamager) {
            return false;
        }

        damager = Object.Instantiate(localDamager, attacks.transform);
        damager.name = DashDamagerName;

        var delay = damager.AddComponent<DeactivateAfterDelay>();
        delay.time = 0.3f;

        damager.DestroyGameObjectInChildren("Worm Worrier");

        damager.DestroyComponentsInChildren<HeroShamanRuneEffect>();

        var runeBloom = damager
            .FindGameObjectInChildren("Shaman Rune")?
            .FindGameObjectInChildren("Shaman Rune Camera Bloom");

        if (runeBloom) {
            Object.DestroyImmediate(runeBloom);
        }

        return true;
    }

    private bool TryGetParticles(GameObject playerObject, [MaybeNullWhen(false)] out GameObject particles) {
        var effects = playerObject.FindGameObjectInChildren("Effects");
        if (!effects) {
            particles = null;
            return false;
        }

        particles = effects.FindGameObjectInChildren(ZapParticlesName);

        if (particles) {
            return true;
        }

        var localParticles = HeroController.instance.gameObject
            .FindGameObjectInChildren("Effects")?
            .FindGameObjectInChildren(ZapParticlesName);

        if (!localParticles) {
            return false;
        }

        particles = Object.Instantiate(localParticles, effects.transform);
        particles.name = ZapParticlesName;

        if (particles.TryGetComponent<ParticleSystem>(out var system)) {
            var emission = system.emission;
            emission.enabled = true;
        }

        var delay = particles.AddComponent<DeactivateAfterDelay>();
        delay.time = 0.3f;

        return true;
    }
}

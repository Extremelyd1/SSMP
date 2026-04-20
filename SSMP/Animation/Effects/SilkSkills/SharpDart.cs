using System.Collections;
using System.Diagnostics.CodeAnalysis;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

internal class SharpDart : BaseSilkSkill {
    /// <summary>
    /// The name of the volt particles object
    /// </summary>
    private const string VoltParticlesName = "Silk Charge Particles Zap";

    /// <summary>
    /// If this instance of sharp dart is for the volt filament variant
    /// </summary>
    public bool Volt = false;

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var isShaman = crestType == CrestType.Shaman;
        MonoBehaviourUtil.Instance.StartCoroutine(PlayEffect(playerObject, isShaman));
    }

    /// <summary>
    /// Plays the sharp dart effect
    /// </summary>
    /// <param name="playerObject">The player using the skill</param>
    /// <param name="isShaman">If the shaman crest effects should be used</param>
    private IEnumerator PlayEffect(GameObject playerObject, bool isShaman) {
        // Set up damager
        if (TryGetDamager(playerObject, out var damager)) {
            SetDamageHeroState(damager);
            damager.SetActive(true);

            var rune = damager.FindGameObjectInChildren("Shaman Rune");
            if (rune) {
                rune.SetActive(false);
                rune.SetActive(isShaman);
            }
        }

        // Play dash burst
        if (TryGetDashBurst(playerObject, out var dashBurst)) {
            dashBurst.SetActive(false);
            dashBurst.SetActive(true);
        }

        var fsm = GetSkillFSM();

        // Play sound effects
        PlayHornetAttackSound(playerObject);

        var chargeAntic = fsm.GetAction<PlayAudioEvent>("Silk Charge Begin", 5);
        if (chargeAntic != null) AudioUtil.PlayAudio(chargeAntic, playerObject);
        
        // Play volt effects
        if (Volt) {
            var voltNoise = fsm.GetFirstAction<PlayAudioEvent>("Silk Charge Zap FX");
            if (voltNoise != null) AudioUtil.PlayAudio(voltNoise, playerObject);

            if (TryGetParticles(playerObject, out var particles)) {
                particles.SetActive(false);
                particles.SetActive(true);
            }
        }

        // Brief pause for ending sound
        yield return new WaitForSeconds(0.2f);

        var chargeFull = fsm.GetAction<PlayAudioEvent>("Silk Charge Start", 12);
        if (chargeFull != null) AudioUtil.PlayAudio(chargeFull, playerObject);
    }

    /// <summary>
    /// Attempts to get the dash burst effect
    /// </summary>
    /// <param name="playerObject">The player using the skill</param>
    /// <param name="dashBurst">The effect, if found</param>
    /// <returns>true if the effect was found</returns>
    private static bool TryGetDashBurst(GameObject playerObject, [MaybeNullWhen(false)] out GameObject dashBurst) {
        FindOrCreateSkill(playerObject, "Silk Charge DashBurst", out dashBurst);
        
        if (dashBurst) {
            dashBurst.transform.localScale = new Vector3(0.75f, 0.75f, 0.75f);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to get the damage object
    /// </summary>
    /// <param name="playerObject">The player using the skill</param>
    /// <param name="damager">The damager, if found</param>
    /// <returns>true if the damager was found</returns>
    private static bool TryGetDamager(GameObject playerObject, [MaybeNullWhen(false)] out GameObject damager) {
        // Find or create effect
        var created = FindOrCreateSkill(playerObject, "Silk Charge Damager", out damager);
        if (!damager) {
            return false;
        }

        if (!created) {
            return true;
        }

        // Set up effect if created
        var delay = damager.AddComponent<DeactivateAfterDelay>();
        delay.time = 0.3f;

        damager.DestroyGameObjectInChildren("Worm Worrier");

        damager.DestroyComponentsInChildren<HeroShamanRuneEffect>();

        damager.FindGameObjectInChildren("Shaman Rune")?
            .DestroyGameObjectInChildren("Shaman Rune Camera Bloom");

        return true;
    }

    /// <summary>
    /// Attempts to get the volt particles
    /// </summary>
    /// <param name="playerObject">The player using the skill</param>
    /// <param name="particles">The particles, if found</param>
    /// <returns>true if the particles were found</returns>
    private static bool TryGetParticles(GameObject playerObject, [MaybeNullWhen(false)] out GameObject particles) {
        // Find effects
        var effects = playerObject.FindGameObjectInChildren("Effects");
        if (!effects) {
            effects = new GameObject("Effects");
            effects.transform.SetParentReset(playerObject.transform);
        }

        // Find existing effect
        particles = effects.FindGameObjectInChildren(VoltParticlesName);

        if (particles) {
            return true;
        }

        // Create new effect
        var localParticles = HeroController.instance.gameObject
            .FindGameObjectInChildren("Effects")?
            .FindGameObjectInChildren(VoltParticlesName);

        if (!localParticles) {
            return false;
        }

        particles = Object.Instantiate(localParticles, effects.transform);
        particles.name = VoltParticlesName;

        // Set up components
        if (particles.TryGetComponent<ParticleSystem>(out var system)) {
            var emission = system.emission;
            emission.enabled = true;
        }

        var delay = particles.AddComponent<DeactivateAfterDelay>();
        delay.time = 0.3f;

        return true;
    }
}

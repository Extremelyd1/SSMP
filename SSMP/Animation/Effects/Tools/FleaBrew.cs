using System.Collections;
using System.Collections.Generic;
using GlobalSettings;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Tools;

/// <summary>
/// Class for the tool effect of Flea Brew (attack buff).
/// </summary>
internal class FleaBrew : BaseAttackTool {
    private const string ParticlesName = "Flea Brew Particles";

    /// <summary>
    /// Cached reference to a modified version of the poisoned flea brew trail.
    /// </summary>
    private static GameObject? _modifiedPoisonTrail;

    /// <summary>
    /// Cached values of sprite flashes for Flea Brews.
    /// </summary>
    private static readonly Dictionary<int, SpriteFlash.FlashHandle> BrewFlashes = [];

    /// <summary>
    /// Instance of the effect class.
    /// </summary>
    public static readonly FleaBrew Instance = new();

    /// <inheritdoc/>
    public override byte[] GetEffectInfo() {
        return [
            (byte) (HasPoison() ? 1 : 0)
        ];
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var hc = HeroController.instance;
        var isPoison = EffectIsPoisoned(effectInfo);

        // Play audio
        var fsm = hc.toolsFSM;
        if (fsm != null) {
            var audio = fsm.GetFirstAction<PlayAudioEvent>("Flea Brew Burst");
            AudioUtil.PlayAudio(audio, playerObject);
        }

        // Start particles
        var duration = hc.QUICKENING_DURATION;
        var localPrefab = isPoison ? hc.quickeningPoisonEffectPrefab : hc.quickeningEffectPrefab;

        var particles = EffectUtils.SpawnGlobalPoolObject(
            localPrefab.gameObject, 
            playerObject.transform, 
            duration,
            true
        );
        if (particles == null) return;

        particles.name = ParticlesName;

        // Set up poison clouds
        if (isPoison && ShouldDoDamage && ServerSettings.IsPvpEnabled) {
            SetPoisonTrail(particles);
        }

        // Play sprite flash
        if (!playerObject.TryGetComponent<SpriteFlash>(out var flash)) {
            return;
        }

        // See if previous effect is playing
        var id = playerObject.GetInstanceID();
        if (BrewFlashes.TryGetValue(id, out var prevHandle)) {
            if (flash.IsFlashing(true, prevHandle)) {
                flash.CancelRepeatingFlash(prevHandle);
            }
        }

        // Start new flash
        var color = isPoison ? Gameplay.PoisonPouchHeroTintColour : new Color(1f, 0.85f, 0.47f, 1f);
        var flashHandle = flash.Flash(
            color, 
            0.7f, 
            0.2f,
            0.01f,
            0.22f,
            0f,
            repeating: true,
            0,
            1,
            requireExplicitCancel: true
        );
        BrewFlashes[id] = flashHandle;

        MonoBehaviourUtil.Instance.StartCoroutine(StopBrewFlashAfterDelay(playerObject, flashHandle));

    }

    /// <summary>
    /// Stops the Flea Brew flashing and particles.
    /// </summary>
    /// <param name="playerObject">The player object with the Flea Brew animation.</param>
    public static void StopBrew(GameObject playerObject) {
        var id = playerObject.GetInstanceID();
        if (!BrewFlashes.TryGetValue(id, out var handle)) {
            return;
        }

        StopBrew(playerObject, handle);
    }

    /// <summary>
    /// Stops the Flea Brew flashing and particles.
    /// </summary>
    /// <param name="playerObject">The player object with the Flea Brew animation.</param>
    /// <param name="handle">The current sprite flash handle.</param>
    private static void StopBrew(GameObject playerObject, SpriteFlash.FlashHandle handle) {
        // Stop sprite flash
        if (playerObject.TryGetComponent<SpriteFlash>(out var flash)) {
            flash.CancelRepeatingFlash(handle);
        }

        // Stop particles
        var particles = playerObject.FindGameObjectInChildren(ParticlesName);
        if (particles) {
            Object.Destroy(particles);
        }
    }

    /// <summary>
    /// Stops the Flea Brew sprite flash after a delay.
    /// </summary>
    /// <param name="playerObject">The player that used the tool.</param>
    /// <param name="handle">The flash's handle.</param>
    private static IEnumerator StopBrewFlashAfterDelay(GameObject playerObject, SpriteFlash.FlashHandle handle) {
        // Wait for effect to end
        yield return new WaitForSeconds(HeroController.instance.QUICKENING_DURATION);

        // Cancel flash
        StopBrew(playerObject, handle);
    }

    /// <summary>
    /// Sets up a poison trail that deals damage.
    /// </summary>
    /// <param name="particles">The poisoned Flea Brew particle spawner.</param>
    private void SetPoisonTrail(GameObject particles) {
        // Find the prefab spawner
        var spawnerObj = particles.FindGameObjectInChildren("Trail Spawner");
        if (!spawnerObj) return;

        if (!spawnerObj.TryGetComponent<SpawnRepeatingSmart>(out var spawner)) {
            return;
        }

        // Set up a modified version of the prefab
        if (!_modifiedPoisonTrail) {
            var prefab = spawner.prefab;
            if (!prefab) return;

            _modifiedPoisonTrail = EffectUtils.SpawnGlobalPoolObject(prefab, particles.transform, 0);
            if (!_modifiedPoisonTrail) return;

            _modifiedPoisonTrail.SetActive(false);
            _modifiedPoisonTrail.name = "Hornet Poison Trail Modified";

            // Re-add recycler so that it de-spawns
            // Since this is a new object, it won't override the other pool
            var recycler = _modifiedPoisonTrail.AddComponent<AutoRecycleSelf>();
            recycler.afterEvent = GlobalEnums.AfterEvent.TIME;
            recycler.timeToWait = 1.1f;

            // Set the damager
            var damager = _modifiedPoisonTrail.FindGameObjectInChildren("damager");
            if (damager) {
                AddDamageHeroComponent(damager, ServerSettings.PoisonBrewDamage);
                damager.layer = 17;
            }
        } else {
            // Ensure the damage amount is correct every time the brew is used
            var damagerObj = _modifiedPoisonTrail.FindGameObjectInChildren("damager");
            if (damagerObj && damagerObj.TryGetComponent<DamageHero>(out var damager)) {
                damager.damageDealt = ServerSettings.PoisonBrewDamage;
            }
        }

        // Set the spawner's prefab
        spawner.prefab = _modifiedPoisonTrail;
    }
}

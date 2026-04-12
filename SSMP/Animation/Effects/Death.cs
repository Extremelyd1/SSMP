using System.Collections;
using System.Diagnostics.CodeAnalysis;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect for the death of the player.
/// </summary>
internal class Death : AnimationEffect {
    /// <summary>
    /// Name of the game object for the death particles.
    /// </summary>
    private const string DeathParticleObjectName = "Low Health Leak";

    /// <summary>
    /// Cached game object for the death particles.
    /// </summary>
    private static GameObject? _deathParticles;

    /// <inheritdoc/>
    public override byte[] GetEffectInfo() {
        var frosted = HeroController.instance.cState.isFrostDeath;

        byte[] info = [
            (byte)(frosted ? 1 : 0)
        ];

        return info;
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Play frost death if applicable (effect info contains a single byte with the value '1')
        if (effectInfo is [1]) {
            MonoBehaviourUtil.Instance.StartCoroutine(PlayFrostDeath(playerObject));
            return;
        } 

        // Otherwise just play the normal animation
        MonoBehaviourUtil.Instance.StartCoroutine(PlayDeath(playerObject));
    }

    /// <summary>
    /// Plays the frost death animation.
    /// </summary>
    /// <param name="playerObject">The GameObject representing the player.</param>
    private static IEnumerator PlayFrostDeath(GameObject playerObject) {
        // Find frost death prefab
        var prefab = HeroController.instance.heroDeathFrostPrefab;
        if (prefab == null) {
            yield break;
        }

        // Play freeze audio
        var fsm = prefab.LocateMyFSM("Hero Death Anim");
        var audio = fsm.GetFirstAction<AudioPlayerOneShotSingle>("Start");
        AudioUtil.PlayAudio(audio, playerObject);

        // Spawn the two crystal growth particle systems
        var localCrystalSlow = prefab.FindGameObjectInChildren("Particle_Crystal Slow");
        var localCrystalFinal = prefab.FindGameObjectInChildren("Particle_Crystal Final");

        const float growthTime = 3f;
        var crystalSlow = EffectUtils.SpawnGlobalPoolObject(
            localCrystalSlow, 
            playerObject.transform, 
            growthTime, 
            true
        );
        var crystalFinal = EffectUtils.SpawnGlobalPoolObject(
            localCrystalFinal, 
            playerObject.transform, 
            growthTime, 
            true
        );

        if (crystalSlow == null || crystalFinal == null) {
            yield break;
        }

        // Reset their positions
        crystalSlow.transform.localPosition = new Vector3(-0.32f, 0, -2.22f);
        crystalFinal.transform.localPosition = new Vector3(-0.22f, -0.04f, -2.2f);

        yield return new WaitForSeconds(growthTime);

        // Transition from growing the crystals to exploding them
        var localDestroyEffects = prefab.FindGameObjectInChildren("Destroy Effects");
        var destroyEffects = EffectUtils.SpawnGlobalPoolObject(
            localDestroyEffects, 
            playerObject.transform, 
            5
        );
        if (destroyEffects != null && destroyEffects.TryGetComponent<CameraShakeOnEnable>(out var shaker)) {
            Object.DestroyImmediate(shaker);
        }

        // "hide" the player (assign a very small texture)
        playerObject.GetComponent<tk2dSpriteAnimator>().Stop();
        playerObject.GetComponent<tk2dSprite>().SetSprite("wall_puff0004");

        // Spawn the frosted hornet object
        const int frostedDuration = 3;
        var localFrostDeath = prefab.FindGameObjectInChildren("Hornet_Frosted");
        var frostDeath = EffectUtils.SpawnGlobalPoolObject(
            localFrostDeath, 
            playerObject.transform, 
            frostedDuration + 1.1f
        );
        if (frostDeath == null) {
            yield break;
        }

        frostDeath.transform.localPosition = playerObject.transform.position + new Vector3(0.11f, 0, -0.18f);

        // Fade out and play particles
        yield return new WaitForSeconds(frostedDuration);
        frostDeath.AddComponent<SimpleFadeOut>();
        SpawnCocoonParticles(frostDeath);
    }

    /// <summary>
    /// Plays the normal death animation.
    /// </summary>
    /// <param name="playerObject">The GameObject representing the player.</param>
    private static IEnumerator PlayDeath(GameObject playerObject) {
        // Get/create a death particle system
        if (!CreateParticles(playerObject, out var particles)) {
            yield break;
        }

        // Enable the system by turning on emission
        var particleSystem = particles.GetComponent<ParticleSystem>();
        var emission = particleSystem.emission;
        emission.enabled = true;
        
        // Refresh particle system
        particles.SetActive(false);
        particles.SetActive(true);

        // Disable leak particle emission after time, then spawn black particles
        yield return new WaitForSeconds(4);
        emission.enabled = false;
        SpawnCocoonParticles(playerObject);
    }

    /// <summary>
    /// Spawns the particle system that is created when a player hits their cocoon.
    /// </summary>
    /// <param name="playerObject">The player object to spawn the particles on</param>
    private static void SpawnCocoonParticles(GameObject playerObject) {
        // Get the cocoon prefab
        var manager = GameManager.instance.GetSceneManager().GetComponent<CustomSceneManager>();
        var localCocoon = manager.heroCorpsePrefab;

        // Find the particle systems inside of it
        var localCocoonParticles = localCocoon
                                   .FindGameObjectInChildren("Core")?
                                   .FindGameObjectInChildren("Pt Spider Fall");
        if (localCocoonParticles == null) {
            Logger.Info("No particles");
            return;
        }

        // Spawn the particles
        EffectUtils.SpawnGlobalPoolObject(localCocoonParticles, playerObject.transform, 10f);
    }

    /// <summary>
    /// Attempts to locate and bind the 'Low Health Leak' GameObject to the specified player object.
    /// </summary>
    /// <param name="playerObject">The player's object.</param>
    /// <param name="deathParticles">The player's 'Low Health Leak' object, or null if not found.</param>
    /// <returns>true if the 'Low Health Leak' GameObject is successfully found and bound; otherwise, false.</returns>
    private static bool CreateParticles(
        GameObject playerObject, 
        [MaybeNullWhen(false)] out GameObject deathParticles
    ) {
        // Find the reference object
        _deathParticles ??= HeroController.instance.gameObject.FindGameObjectInChildren(DeathParticleObjectName);
        if (_deathParticles == null) {
            Logger.Warn("Could not find local Bind Effects object in hero object");
            deathParticles = null;
            return false;
        }

        // Find the existing particles for the player object
        deathParticles = playerObject.FindGameObjectInChildren(DeathParticleObjectName);

        // If not found, make it!
        if (deathParticles == null) {
            deathParticles = Object.Instantiate(_deathParticles);
            deathParticles.transform.SetParentReset(playerObject.transform);
            deathParticles.transform.SetLocalPositionZ(0.2f);
            deathParticles.name = DeathParticleObjectName;
            
            // Add timer to turn off
            var deactivator = deathParticles.AddComponent<DeactivateAfterDelay>();
            deactivator.time = 4f;

            // Ensure the particle system turns on automatically
            var particleSystem = deathParticles.GetComponent<ParticleSystem>();
            var main = particleSystem.main;
            main.playOnAwake = true;
        }

        // Particles were found or created!
        return true;
    }
}

using System.Collections;
using System.Linq;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

internal class SilkSpear : BaseSilkSkill {
    /// <summary>
    /// The object name of the silk spear
    /// </summary>
    private const string SpearObjectName = "Needle Throw";
    
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Get silk spear and the object that most child objects are in
        var spear = GetSilkSpear(playerObject);
        if (!spear) return;

        var parent = spear.FindGameObjectInChildren("needle_throw_simple");
        if (!parent) return;


        // Set volt settings
        var isVolt = IsVolt(effectInfo);

        var voltThread = parent
            .FindGameObjectInChildren("thread")?
            .FindGameObjectInChildren("zap thread");

        if (voltThread) voltThread.SetActive(isVolt);

        var needle = parent.FindGameObjectInChildren("needle");

        var voltNeedle = needle?.FindGameObjectInChildren("Zap Effect Activator");
        if (voltNeedle) {
            voltNeedle.SetActive(isVolt);
            voltNeedle.SetActiveChildren(isVolt);
        }

        // Set shaman settings
        var isShaman = crestType == CrestType.Shaman;

        var shamanParent = parent.FindGameObjectInChildren("Rune Effect Activator");
        if (shamanParent) {
            shamanParent.SetActive(isShaman);

            var shamanRune = shamanParent.FindGameObjectInChildren("Shaman Rune");
            if (shamanRune) {
                shamanRune.SetActive(isShaman);

                var voltRune = shamanRune.FindGameObjectInChildren("Zap Rune");
                if (voltRune) voltRune.SetActive(isVolt);
            }
        }

        // Set damager
        var damager = needle?.FindGameObjectInChildren("Needle Damage");
        if (damager) {
            SetDamageHeroStateCalculated(damager, ServerSettings.SilkSpearDamage, isVolt, isShaman);
            MonoBehaviourUtil.Instance.StartCoroutine(PlayPossibleThunk(playerObject, spear, damager));
        } else {
            Logger.Warn("Unable to set damager for Silk Spear");
        }

        // Enable spear
        spear.SetActive(false);
        spear.SetActive(true);

        // Play audio
        PlayHornetAttackSound(playerObject);
        var fsm = GetSkillFSM();

        var throwAudio = fsm.GetAction<PlayAudioEvent>("Start Throw", 1);
        if (throwAudio != null) AudioUtil.PlayAudio(throwAudio, playerObject);

        if (isVolt) {
            var voltAudio = fsm.GetAction<PlayAudioEvent>("Silkspear Zap FX", 1);
            if (voltAudio != null) AudioUtil.PlayAudio(voltAudio, playerObject);
        }

    }

    /// <summary>
    /// Waits for the spear to collide with terrain. If it does, it'll stop short.
    /// </summary>
    /// <param name="playerObject">The player who fired the spear</param>
    /// <param name="spear">The spear</param>
    /// <param name="damager">The spear's damager</param>
    /// <returns></returns>
    private static IEnumerator PlayPossibleThunk(GameObject playerObject, GameObject spear, GameObject damager) {
        var collider = damager.GetComponent<BoxCollider2D>();
        var animator = spear.GetComponentInChildren<Animator>();

        yield return null;

        // Try to thunk as long as the spear is doing damage
        while (collider.isActiveAndEnabled) {

            // Find a terrain collider within the bounds of the spear
            var y = spear.transform.position.y;
            var collisions = Physics2D.LinecastAll(new Vector2(collider.bounds.min.x, y), new Vector2(collider.bounds.max.x, y), LayerMask.GetMask("Terrain"));

            var found = collisions.FirstOrDefault(c => c.collider.gameObject.tag != "Piercable Terrain" && c.collider.gameObject.layer == 8);

            // A terrain collider was found, do the thunk
            if (found) {
                yield return null;

                // Don't stop short if < 6 units away
                if (damager.transform.position.x.IsWithinTolerance(6, playerObject.transform.position.x)) {
                    animator.Play("Thunk");
                }

                // Either way, do the thunk particles
                var thunk = spear.FindGameObjectInChildren("Needle Thunk");
                if (thunk) {
                    thunk.SetActive(false);
                    thunk.transform.position = found.point;
                    thunk.SetActive(true);
                }

                yield break;
            }

            yield return null;
        }
    }

    /// <summary>
    /// Attempts to find the silk spear for the player
    /// </summary>
    /// <param name="playerObject">The player using the spear</param>
    /// <returns>The spear, if found</returns>
    private static GameObject? GetSilkSpear(GameObject playerObject) {
        // Find existing silk spear
        var silkAttacks = GetPlayerSilkSkills(playerObject);
        var spear = silkAttacks.FindGameObjectInChildren(SpearObjectName);
        if (spear) return spear;

        // Find on own silk attacks
        if (!TryGetLocalSilkSkills(out var localSilkAttacks)) {
            return null;
        }

        var localSpear = localSilkAttacks.FindGameObjectInChildren(SpearObjectName);
        if (localSpear == null) {
            return null;
        }

        // Create new spear
        spear = Object.Instantiate(localSpear, silkAttacks.transform);
        spear.name = SpearObjectName;

        // Remove components
        spear.DestroyComponent<ToolEquipChecker>();
        spear.DestroyComponentsInChildren<HeroShamanRuneEffect>();
        spear.DestroyComponentsInChildren<FollowCamera>();

        // Remove specific children and their components
        var child = spear.FindGameObjectInChildren("needle_throw_simple");
        if (child) {
            child.DestroyComponent<CameraControlAnimationEvents>();
            child.DestroyComponent<CaptureAnimationEvent>();

            var bloom1 = child
                .FindGameObjectInChildren("Rune Effect Activator")?
                .FindGameObjectInChildren("Shaman Rune")?
                .FindGameObjectInChildren("Rune Effect")?
                .FindGameObjectInChildren("Shaman Rune")?
                .FindGameObjectInChildren("Shaman Rune Camera Bloom");

            var bloom2 = child
                .FindGameObjectInChildren("Rune Effect Activator")?
                .FindGameObjectInChildren("Shaman Rune")?
                .FindGameObjectInChildren("Zap Rune")?
                .FindGameObjectInChildren("Rune Effect")?
                .FindGameObjectInChildren("Shaman Rune")?
                .FindGameObjectInChildren("Shaman Rune Camera Bloom");

            if (bloom1) {
                Object.Destroy(bloom1);
            }
            if (bloom2) {
                Object.Destroy(bloom2);
            }
        }
         
        return spear;
    }
}

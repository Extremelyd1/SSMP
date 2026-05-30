using System.Diagnostics.CodeAnalysis;
using HutongGames.PlayMaker.Actions;
using SSMP.Game.Settings;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Tools;

/// <summary>
/// Class for the tool effect of Sawtooth Circlet (damage when double jumping or gliding).
/// This is a static class because it is only statically called from other effects (Faydown Cloak and Drifter's Cloak).
/// </summary>
internal static class SawtoothCirclet {
    /// <summary>
    /// The name of the Sawtooth Circlet object.
    /// </summary>
    private const string SpikedCircletName = "Tool_brolly_spike";

    /// <summary>
    /// Cached reference to the local Sawtooth Circlet object.
    /// </summary>
    private static GameObject? _localCirclet;

    /// <summary>
    /// Plays the Sawtooth Circlet animation.
    /// </summary>
    /// <param name="playerObject">The player using the circlet.</param>
    /// <param name="doDamage">If the circlet should do damage.</param>
    /// <param name="serverSettings">The server settings for retrieving the damage it should deal.</param>
    public static void PlayCirclet(GameObject playerObject, bool doDamage, ServerSettings serverSettings) {
        // Get the circlet
        if (!TryGetCirclet(playerObject, out var circlet)) {
            return;
        }

        // Set the damager
        var damagerParent = circlet
            .FindGameObjectInChildren("Brolly_spike_position")?
            .FindGameObjectInChildren("brolly_slash_enemy_damager");

        var damagerRight = damagerParent?.FindGameObjectInChildren("Damager R");
        var damagerLeft = damagerParent?.FindGameObjectInChildren("Damager L");

        var damage = serverSettings.SawtoothCircletDamage;
        if (damagerRight != null) DamageAnimationEffect.SetDamageHeroState(damagerRight, doDamage, damage);
        if (damagerLeft != null) DamageAnimationEffect.SetDamageHeroState(damagerLeft, doDamage, damage);

        // Refresh the circlet
        circlet.SetActive(false);
        circlet.SetActive(true);

        // Play audio
        var audioFsm = _localCirclet.LocateMyFSM("brolly_spike_cooldown_check").FsmTemplate.fsm;

        var audioAction = audioFsm.GetState("Check").Actions[4];
        if (audioAction is PlayAudioEvent audio) {
            AudioUtil.PlayAudio(audio, playerObject);
        }
    }

    /// <summary>
    /// Attempts to find or create the Sawtooth Circlet object.
    /// </summary>
    /// <param name="playerObject">The player using the circlet.</param>
    /// <param name="circlet">The circlet, if found.</param>
    /// <returns>True if the circlet was found, otherwise false.</returns>
    private static bool TryGetCirclet(GameObject playerObject, [MaybeNullWhen(false)] out GameObject circlet) {
        // Find or create effects
        var effects = playerObject.FindGameObjectInChildren("Effects");
        if (effects == null) {
            effects = new GameObject("Effects");
            effects.transform.SetParentReset(playerObject.transform);
        }

        // Find existing circlet
        circlet = effects.FindGameObjectInChildren(SpikedCircletName);
        if (circlet != null) {
            return true;
        }

        // Locate circlet
        if (_localCirclet == null) {
            var brollyFsm = HeroController.instance.fsm_brollyControl;
            var spawner = brollyFsm.GetFirstAction<SpawnObjectFromGlobalPool>("Damager?");
            _localCirclet = spawner.gameObject.Value;
        }

        // Spawn in a new circlet
        circlet = EffectUtils.SpawnGlobalPoolObject(_localCirclet, effects.transform, 0, true);
        if (circlet == null) {
            return false;
        }

        circlet.name = SpikedCircletName;
        circlet.transform.localPosition = new Vector3(0, 0, -0.02f);
        circlet.transform.localScale = new Vector3(-1, -1, 1);
        circlet.DestroyComponent<PlayMakerFSM>();
        circlet.tag = "Untagged";

        return true;
    }
}

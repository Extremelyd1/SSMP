using System.Diagnostics.CodeAnalysis;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Movement;

internal class UmbrellaInflate : DamageAnimationEffect {
    private const string UmbrellaInflateName = "umbrella_inflate_effect";

    private const string SpikedCircletName = "Tool_brolly_spike";

    private static GameObject? _localCirclet;

    public static UmbrellaInflate Instance = new();

    /// <inheritdoc/>
    public override byte[]? GetEffectInfo() {
        return [
            (byte)(ToolItemManager.IsToolEquipped("Brolly Spike") ? 1 : 0)
        ];
    }

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        // Get or create effects
        var effects = playerObject.FindGameObjectInChildren("Effects");
        if (effects == null) {
            effects = new GameObject();
            effects.transform.SetParentReset(playerObject.transform);
        }

        // Find or create inflate object
        var effect = effects.FindGameObjectInChildren(UmbrellaInflateName);
        if (effect == null) {
            var localEffect = HeroController.instance.umbrellaEffect;
            effect = Object.Instantiate(localEffect, effects.transform);

            effect.name = UmbrellaInflateName;
            effect.transform.localPosition = new Vector3(0, -0.24f, 0);
            effect.transform.localScale = Vector3.one;

            effect.DestroyGameObjectInChildren("umbrella_float_fx_burst0002");
        }

        // Refresh particles
        effect.SetActive(false);
        effect.SetActive(true);

        // Enable sawtooth circlet if appropriate
        if (effectInfo is [1]) {
            PlayCirclet(playerObject);
        }

    }

    /// <summary>
    /// Attempts to find or create the sawtooth circlet object
    /// </summary>
    /// <param name="playerObject">The player using the circlet</param>
    /// <param name="circlet">The circlet, if found</param>
    /// <returns>true if the circlet was found</returns>
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

    /// <summary>
    /// Plays the sawtooth circlet
    /// </summary>
    /// <param name="playerObject">The player using the circlet</param>
    public void PlayCirclet(GameObject playerObject) {
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

        if (damagerRight != null) SetDamageHeroState(damagerRight, 1);
        if (damagerLeft != null) SetDamageHeroState(damagerLeft, 1);

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
}

using System;
using GlobalSettings;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Util;

/// <summary>
/// Static class proving utilities regarding audio.
/// </summary>
internal static class AudioUtil {
    /// <summary>
    /// Get an audio source relative to the given GameObject.
    /// </summary>
    /// <param name="gameObject">The GameObject to get an audio source relative to.</param>
    /// <returns>The instantiated audio source component.</returns>
    public static AudioSource GetAudioSourceObject(GameObject gameObject) {
        var prefab = Audio.DefaultAudioSourcePrefab;
        if (prefab == null) {
            throw new Exception("DefaultAudioSourcePrefab is null, cannot spawn audio source");
        }

        return prefab.Spawn(gameObject.transform.position);
    }

    /// <summary>
    /// Play the given audio event positionally at the given player object's position. And destroy it after it is done
    /// playing.
    /// </summary>
    /// <param name="playAudioEvent">The PlayAudioEvent instance from an FSM.</param>
    /// <param name="playerObject">The player object to play the audio at.</param>
    public static void PlayAudioEventAtPlayerObject(PlayAudioEvent playAudioEvent, GameObject playerObject) {
        var audioClip = playAudioEvent.audioClip.value as AudioClip;
        if (audioClip == null) {
            Logger.Warn("Audio clip for PlayAudioEvent is null");
            return;
        }
        
        var audioEvent = new AudioEvent {
            Clip = audioClip,
            PitchMin = playAudioEvent.pitchMin.value,
            PitchMax = playAudioEvent.pitchMax.value,
            Volume = playAudioEvent.volume.value
        };

        var position = playerObject.transform.position;

        if (playAudioEvent.audioPlayerPrefab.IsNone) {
            audioEvent.SpawnAndPlayOneShot(position);
        } else {
            audioEvent.SpawnAndPlayOneShot(
                playAudioEvent.audioPlayerPrefab.value as AudioSource, 
                position
            );
        }
    }

    /// <summary>
    /// Play a random audio clip from the given FSM action positionally at the given player object's position.
    /// </summary>
    /// <param name="playAudioClip">The action instance from an FSM.</param>
    /// <param name="playerObject">The player object to play the audio at.</param>
    public static void PlayRandomAudioClipAtPlayObject(
        PlayRandomAudioClipTableV2 playAudioClip,
        GameObject playerObject
    ) {
        var audioClipTable = playAudioClip.Table.value as RandomAudioClipTable;
        if (audioClipTable == null) {
            Logger.Warn("Audio clip table for PlayRandomAudioClipTableV2 is null");
            return;
        }

        var position = playerObject.transform.position;

        if (playAudioClip.AudioPlayerPrefab.Value) {
            audioClipTable.SpawnAndPlayOneShot(
                playAudioClip.AudioPlayerPrefab.value as AudioSource, 
                position,
                playAudioClip.ForcePlay.value
            );
        } else {
            audioClipTable.SpawnAndPlayOneShot(position, playAudioClip.ForcePlay.value);
        }
    }
}

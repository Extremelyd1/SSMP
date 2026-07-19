using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using SSMP.Networking.Packet.Data;
using UnityEngine;
using Random = UnityEngine.Random;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
#pragma warning disable CS0618
#pragma warning disable CS8600
#pragma warning disable CS8618

namespace SSMP.Game.Client.Entity.Action;

internal static partial class EntityFsmActions {
    #region AudioPlay

    ///<summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, AudioPlay action) {
        return true;
    }

    ///<summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, AudioPlay action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null || !audioSource.enabled) {
            return;
        }

        var audioClip = action.oneShotClip.Value as AudioClip;
        if (audioClip == null) {
            audioSource.Play();

            if (action.volume.IsNone) {
                return;
            }

            audioSource.volume = action.volume.Value;
            return;
        }

        if (!action.volume.IsNone) {
            audioSource.PlayOneShot(audioClip, action.volume.Value);
            return;
        }

        audioSource.PlayOneShot(audioClip);
    }

    #endregion

    #region AudioPlaySimple

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, AudioPlaySimple action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, AudioPlaySimple action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null) {
            return;
        }

        var audioClip = action.oneShotClip.Value as AudioClip;
        if (audioClip == null) {
            if (!audioSource.isPlaying) {
                audioSource.Play();
            }

            if (!action.volume.IsNone) {
                audioSource.volume = action.volume.Value;
            }
        } else {
            if (!action.volume.IsNone) {
                audioSource.PlayOneShot(audioClip, action.volume.Value);
            } else {
                audioSource.PlayOneShot(audioClip);
            }
        }
    }

    #endregion

    #region AudioPlayerOneShot

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, AudioPlayerOneShot action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, AudioPlayerOneShot action) {
        // TODO: delay?

        if (action.audioClips.Length == 0) {
            return;
        }

        var audioPlayerPrefab = action.audioPlayer.Value;
        var audioPlayer = audioPlayerPrefab.Spawn(
            action.spawnPoint.Value.transform.position, Quaternion.Euler(Vector3.up)
        );

        var audioSource = audioPlayer.GetComponent<AudioSource>();

        action.storePlayer.Value = audioPlayer;

        var randomWeightedIndex = ActionHelpers.GetRandomWeightedIndex(action.weights);
        if (randomWeightedIndex != -1) {
            var audioClip = action.audioClips[randomWeightedIndex];
            if (audioClip != null) {
                audioSource.pitch = Random.Range(action.pitchMin.Value, action.pitchMax.Value);
                audioSource.PlayOneShot(audioClip);
            }
        }

        audioSource.volume = action.volume.Value;
    }

    #endregion

    #region AudioPlayerOneShotSingle

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, AudioPlayerOneShotSingle action) {
        return !action.audioPlayer.IsNone && !action.spawnPoint.IsNone && action.spawnPoint.Value != null;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, AudioPlayerOneShotSingle action) {
        // TODO: delay?

        if (action.audioPlayer.IsNone || action.spawnPoint.IsNone || action.spawnPoint.Value == null) {
            return;
        }

        var audioPlayer = action.audioPlayer.Value;
        var position = action.spawnPoint.Value.transform.position;
        var up = Vector3.up;

        if (audioPlayer == null) {
            return;
        }

        audioPlayer = audioPlayer.Spawn(position, Quaternion.Euler(up));
        var audioSource = audioPlayer.GetComponent<AudioSource>();
        action.storePlayer.Value = audioPlayer;

        var audioClip = action.audioClip.Value as AudioClip;
        audioSource.pitch = Random.Range(action.pitchMin.Value, action.pitchMax.Value);
        audioSource.volume = action.volume.Value;

        if (audioClip == null) {
            return;
        }

        audioSource.PlayOneShot(audioClip);
    }

    #endregion

    #region SetAudioClip

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetAudioClip action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetAudioClip action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null) {
            return;
        }

        audioSource.clip = action.audioClip.Value as AudioClip;
    }

    #endregion

    #region SetAudioPitch

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetAudioPitch action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetAudioPitch action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null) {
            return;
        }

        audioSource.pitch = action.pitch.Value;
    }

    #endregion

    #region AudioStop

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, AudioStop action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, AudioStop action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null) {
            return;
        }

        audioSource.Stop();
    }

    #endregion

    #region SetAudioVolume

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, SetAudioVolume action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, SetAudioVolume action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null) {
            return;
        }

        audioSource.volume = action.volume.Value;
    }

    #endregion

    #region AudioPlayRandom

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, AudioPlayRandom action) {
        return action.audioClips.Length != 0;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, AudioPlayRandom action) {
        if (action.audioClips.Length == 0) {
            return;
        }

        var audioSource = action.gameObject.Value.GetComponent<AudioSource>();

        var randomWeightedIndex = ActionHelpers.GetRandomWeightedIndex(action.weights);
        if (randomWeightedIndex == -1) {
            return;
        }

        var audioClip = action.audioClips[randomWeightedIndex];
        if (audioClip == null) {
            return;
        }

        audioSource.pitch = Random.Range(action.pitchMin.Value, action.pitchMax.Value);
        audioSource.PlayOneShot(audioClip);
    }

    #endregion

    #region AudioPlayInState

    /// <summary>Builds network data from the FSM action.</summary>
    private static bool GetNetworkDataFromAction(EntityNetworkData data, AudioPlayInState action) {
        return true;
    }

    /// <summary>Applies network data to the FSM action.</summary>
    private static void ApplyNetworkDataFromAction(EntityNetworkData data, AudioPlayInState action) {
        var gameObject = action.Fsm.GetOwnerDefaultTarget(action.gameObject);
        if (gameObject == null) {
            return;
        }

        var audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null) {
            return;
        }

        if (!audioSource.isPlaying) {
            audioSource.Play();
        }

        if (!action.volume.IsNone) {
            audioSource.volume = action.volume.Value;
        }

        void ExitAction() {
            audioSource.Stop();
        }

        new ActionInState {
            Fsm = action.Fsm,
            StateName = action.State.Name,
            ExitAction = ExitAction
        }.Register();
    }

    #endregion
}

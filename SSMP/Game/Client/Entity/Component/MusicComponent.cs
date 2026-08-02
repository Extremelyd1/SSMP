using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker.Actions;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using SSMP.Hooks;
using SSMP.Networking.Client;
using SSMP.Networking.Packet.Data;
using SSMP.Util;
using UnityEngine;
using UnityEngine.Audio;
using Logger = SSMP.Logging.Logger;

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable InconsistentNaming
#pragma warning disable CS8625
#pragma warning disable CS8618

namespace SSMP.Game.Client.Entity.Component;

/// <inheritdoc />
/// <summary>
/// Manages boss fight music synchronisation across the network.
/// Note: This component relies heavily on PlayMakerFSM internals (via reflection on OnEnable)
/// and is fragile; it may break if game updates change PlayMaker structures or method signatures.
/// </summary>
/// <remarks>Manages boss fight music synchronisation across the network.</remarks>
internal class MusicComponent : EntityComponent {
    #region Constants

    /// <summary>
    /// The file path of the embedded resource file for music data.
    /// </summary>
    private const string MusicDataFilePath = "SSMP.Resource.music-data.json";

    /// <summary>
    /// The file name for storing captured music references during gameplay.
    /// </summary>
    private const string CapturedMusicFileName = "captured-music-references.json";

    #endregion

    #region Static data

    /// <summary>
    /// Maps music cue names to their corresponding data structures for fast lookup.
    /// </summary>
    private static readonly Dictionary<string, MusicCueData> MusicCueDataByName;

    /// <summary>
    /// Maps audio mixer snapshot names to their corresponding data structures for fast lookup.
    /// </summary>
    private static readonly Dictionary<string, AudioMixerSnapshotData> SnapshotDataByName;

    /// <summary>
    /// Maps network indices to their corresponding music cue data structures.
    /// </summary>
    private static readonly Dictionary<byte, MusicCueData> MusicCueDataByIndex;

    /// <summary>
    /// Maps network indices to their corresponding audio mixer snapshot data structures.
    /// </summary>
    private static readonly Dictionary<byte, AudioMixerSnapshotData> SnapshotDataByIndex;

    /// <summary>
    /// Stores unique music cue names encountered during capture.
    /// </summary>
    private static readonly HashSet<string> SeenMusicCues = new(StringComparer.Ordinal);

    /// <summary>
    /// Stores unique audio mixer snapshot names encountered during capture.
    /// </summary>
    private static readonly HashSet<string> SeenSnapshots = new(StringComparer.Ordinal);

    #endregion

    #region Static lifecycle state

    /// <summary>
    /// The singleton instance of MusicComponent to ensure we only have one MusicComponent responsible for
    /// synchronising music in a scene.
    /// </summary>
    private static MusicComponent? _instance;

    /// <summary>
    /// Hook for intercepting PlayMakerFSM.OnEnable to resolve music cue and snapshot references.
    /// </summary>
    private static Hook? _fsmEnableHook;

    #endregion

    #region Instance state

    /// <summary>
    /// The index of the last played music cue, so we don't restart them unnecessarily.
    /// </summary>
    private byte _lastMusicCueIndex;

    /// <summary>
    /// The index of the last played audio snapshot, so we don't restart them unnecessarily.
    /// </summary>
    private byte _lastSnapshotIndex;

    #endregion

    #region Static construction

    /// <summary>
    /// Static constructor responsible for loading data from the JSON and registering static hooks.
    /// </summary>
    static MusicComponent() {
        var (cues, snapshots) = FileUtil.LoadObjectFromEmbeddedJson<
            (List<MusicCueData>, List<AudioMixerSnapshotData>)
        >(MusicDataFilePath);

        MusicCueDataByName = new Dictionary<string, MusicCueData>(StringComparer.Ordinal);
        SnapshotDataByName = new Dictionary<string, AudioMixerSnapshotData>(StringComparer.Ordinal);
        MusicCueDataByIndex = new Dictionary<byte, MusicCueData>();
        SnapshotDataByIndex = new Dictionary<byte, AudioMixerSnapshotData>();

        // Indices start at 1; 0 is reserved as "none".
        byte index = 1;

        foreach (var data in cues) {
            data.Index = index++;
            MusicCueDataByName[data.Name] = data;
            MusicCueDataByIndex[data.Index] = data;
        }

        foreach (var data in snapshots) {
            data.Index = index++;
            SnapshotDataByName[data.Name] = data;
            SnapshotDataByIndex[data.Index] = data;
        }
    }

    #endregion

    #region Instance construction

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicComponent"/> class.
    /// Registers event handlers for FSM audio actions.
    /// </summary>
    /// <param name="netClient">The net client used for synchronization.</param>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="gameObject">The GameObject associated with this component.</param>
    private MusicComponent(
        NetClient netClient,
        ushort entityId,
        HostClientPair<GameObject> gameObject
    ) : base(netClient, entityId, gameObject) {
        CustomHooks.ApplyMusicCueFromFsmAction += OnApplyMusicCue;
        CustomHooks.TransitionToAudioSnapshotFromFsmAction += OnTransitionToAudioSnapshot;
    }

    #endregion

    #region Singleton management

    /// <summary>
    /// Creates the singleton instance. Returns false and sets <paramref name="component" /> to null
    /// if one already exists.
    /// </summary>
    /// <param name="netClient">The NetClient instance for networking data.</param>
    /// <param name="entityId">The entity ID that this component is attached to.</param>
    /// <param name="gameObject">The host-client pair of game objects of the entity.</param>
    /// <param name="component">The created instance of MusicComponent if successful, otherwise null.</param>
    /// <returns>True if a new component could be created, false if a component already existed.</returns>
    public static bool TryCreateInstance(
        NetClient netClient,
        ushort entityId,
        HostClientPair<GameObject> gameObject,
        out MusicComponent? component
    ) {
        if (_instance != null) {
            component = null;
            return false;
        }

        _instance = new MusicComponent(netClient, entityId, gameObject);
        component = _instance;
        return true;
    }

    /// <summary>
    /// Clear the current singleton instance of the component.
    /// </summary>
    public static void ClearInstance() => _instance = null;

    #endregion

    #region Hook registration

    /// <summary>
    /// Register hooks for music-related operations.
    /// </summary>
    public static void RegisterHooks() {
        var method = typeof(PlayMakerFSM).GetMethod(
            "OnEnable",
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance
        );

        if (method != null) {
            _fsmEnableHook = new Hook(method, OnFsmEnable);
        } else {
            Logger.Error("Could not find PlayMakerFSM.OnEnable method info.");
        }
    }

    /// <summary>
    /// Deregister hooks for music-related operations.
    /// </summary>
    public static void DeregisterHooks() {
        _fsmEnableHook?.Dispose();
        _fsmEnableHook = null;
    }

    #endregion

    #region Component lifecycle

    /// <summary>
    /// Initializes the host-side logic for the component.
    /// </summary>
    public override void InitializeHost() {
    }

    /// <summary>
    /// Destroys the component and unregisters FSM action events.
    /// </summary>
    public override void Destroy() {
        CustomHooks.ApplyMusicCueFromFsmAction -= OnApplyMusicCue;
        CustomHooks.TransitionToAudioSnapshotFromFsmAction -= OnTransitionToAudioSnapshot;
    }

    #endregion

    #region Host-side music capture

    /// <summary>
    /// Hook that is called when the AudioManager.ApplyMusicCue is called from an ApplyMusicCue FSM action.
    /// Used to network the starting of a music cue for the scene host.
    /// </summary>
    /// <param name="action">The ApplyMusicCue FSM action responsible for the call.</param>
    private void OnApplyMusicCue(ApplyMusicCue action) {
        Logger.Debug($"OnApplyMusicCue: {action.Fsm.GameObject.gameObject.name}, {action.Fsm.Name}");

        if (IsControlled) return;

        if (action.musicCue.Value is not MusicCue musicCue) return;

        if (!MusicCueDataByName.TryGetValue(musicCue.name, out var cueData)) {
            Logger.Debug($"  Music cue '{musicCue.name}' not in music-data.json");
            return;
        }

        Logger.Debug($"  Sending index: {cueData.Index}");

        SendMusicPacket(cueData.Index, _lastSnapshotIndex);
        _lastMusicCueIndex = cueData.Index;
    }

    /// <summary>
    /// Hook that is called when the AudioMixerSnapshot.TransitionTo is called from a TransitionToAudioSnapshot FSM
    /// action. Used to network the starting of an audio snapshot for the scene host.
    /// </summary>
    /// <param name="action">The TransitionToAudioSnapshot FSM action responsible for the call.</param>
    private void OnTransitionToAudioSnapshot(TransitionToAudioSnapshot action) {
        Logger.Debug($"OnTransitionToAudioSnapshot: {action.Fsm.GameObject.gameObject.name}, {action.Fsm.Name}");

        // Door Control FSMs trigger snapshots independently of boss music; let them through.
        if (action.Fsm.Name.Equals("Door Control")) {
            Logger.Debug("  Door Control FSM, skipping");
            return;
        }

        if (IsControlled) return;

        if (action.snapshot.Value is not AudioMixerSnapshot snapshot) return;

        if (!SnapshotDataByName.TryGetValue(snapshot.name, out var snapshotData)) {
            Logger.Debug($"  Audio mixer snapshot '{snapshot.name}' not in music-data.json");
            return;
        }

        Logger.Debug($"  Sending index: {snapshotData.Index}");

        SendMusicPacket(_lastMusicCueIndex, snapshotData.Index);
        _lastSnapshotIndex = snapshotData.Index;
    }

    /// <summary>
    /// Sends a network packet containing the updated music cue and audio snapshot indices.
    /// </summary>
    /// <param name="cueIndex">The network index of the music cue.</param>
    /// <param name="snapshotIndex">The network index of the audio snapshot.</param>
    private void SendMusicPacket(byte cueIndex, byte snapshotIndex) {
        var networkData = new EntityNetworkData { Type = EntityComponentType.Music };
        networkData.Packet.Write(cueIndex);
        networkData.Packet.Write(snapshotIndex);

        SendData(networkData);
    }

    #endregion

    #region Controlled-client music application

    /// <summary>
    /// Updates the music component with received network data.
    /// </summary>
    /// <param name="data">The network data containing new audio indices.</param>
    /// <param name="alreadyInSceneUpdate">Indicates if this update is already within a scene update cycle.</param>
    public override void Update(EntityNetworkData data, bool alreadyInSceneUpdate) {
        Logger.Debug("Update MusicComponent");

        if (!IsControlled) {
            Logger.Debug("  Not controlled, skipping");
            return;
        }

        var cueIndex = data.Packet.ReadByte();
        var snapshotIndex = data.Packet.ReadByte();

        Logger.Debug($"  Received indices - cue: {cueIndex}, snapshot: {snapshotIndex}");

        if (cueIndex != _lastMusicCueIndex) {
            ApplyIndex(cueIndex);
            _lastMusicCueIndex = cueIndex;
        }

        if (snapshotIndex != _lastSnapshotIndex) {
            ApplyIndex(snapshotIndex);
            _lastSnapshotIndex = snapshotIndex;
        }
    }

    /// <summary>
    /// Resolves and applies either a music cue or an audio snapshot matching the given network index.
    /// </summary>
    /// <param name="index">The network index to apply.</param>
    private static void ApplyIndex(byte index) {
        if (MusicCueDataByIndex.TryGetValue(index, out var cueData)) {
            if (cueData.MusicCue == null) {
                Logger.Debug($"  Music cue '{cueData.Name}' indexed but not resolved in-scene");
                return;
            }

            Logger.Debug($"  Applying music cue ({cueData.Name}, {cueData.Type})");
            global::GameManager.instance.AudioManager.ApplyMusicCue(cueData.MusicCue, 0f, 0f, false);
            return;
        }

        if (SnapshotDataByIndex.TryGetValue(index, out var snapshotData)) {
            if (snapshotData.Snapshot == null) {
                Logger.Debug($"  Audio snapshot '{snapshotData.Name}' indexed but not resolved in-scene");
                return;
            }

            Logger.Debug($"  Transitioning to audio snapshot ({snapshotData.Name})");
            snapshotData.Snapshot.TransitionTo(0f);
            return;
        }

        Logger.Debug($"  No cue or snapshot found for index {index}");
    }

    #endregion

    #region FSM reference resolution

    /// <summary>
    /// Hook for when an FSM becomes enabled. Used to check for ApplyMusicCue or TransitionToAudioSnapshot actions
    /// such that their audio data can be resolved.
    /// </summary>
    /// <param name="orig">The original method delegate.</param>
    /// <param name="self">The PlayMakerFSM instance being enabled.</param>
    private static void OnFsmEnable(Action<PlayMakerFSM> orig, PlayMakerFSM self) {
        orig(self);

        foreach (var state in self.FsmStates) {
            foreach (var action in state.Actions) {
                switch (action) {
                    case ApplyMusicCue applyMusicCue: {
                        if (applyMusicCue.musicCue.Value is not MusicCue musicCue) continue;

                        Logger.Debug($"Found music cue '{musicCue.name}' in FSM '{self.Fsm.Name}' / '{state.Name}'");

                        if (MusicCueDataByName.TryGetValue(musicCue.name, out var cueData)) {
                            Logger.Debug($"  Resolved - type: {cueData.Type}");
                            cueData.MusicCue = musicCue;
                        }

                        break;
                    }

                    case TransitionToAudioSnapshot snapshotAction: {
                        if (snapshotAction.snapshot.Value is not AudioMixerSnapshot snapshot) continue;

                        Logger.Debug(
                            $"Found audio snapshot '{snapshot.name}' in FSM '{self.Fsm.Name}' / '{state.Name}'"
                        );

                        if (SnapshotDataByName.TryGetValue(snapshot.name, out var snapshotData)) {
                            Logger.Debug($"  Resolved - type: {snapshotData.Type}");
                            snapshotData.Snapshot = snapshot;
                        }

                        break;
                    }
                }
            }
        }
    }

    #endregion

    #region Capture tooling lifecycle

    /// <summary>
    /// Initializes the music capture system, loading previously captured music references.
    /// </summary>
    // TODO: Call this method during development/debugging to capture and dump music cues and snapshots to a file.
    public static void InitializeCapture() {
        /*
        try {
            var filePath = Path.Combine(FileUtil.GetConfigPath(), CapturedMusicFileName);

            if (File.Exists(filePath)) {
                var container = FileUtil.LoadObjectFromJsonFile<CapturedMusicContainer>(filePath);

                if (container != null) {
                    lock (SeenMusicCues) {
                        foreach (var name in container.MusicCues) {
                            SeenMusicCues.Add(name);
                        }
                    }

                    lock (SeenSnapshots) {
                        foreach (var name in container.Snapshots) {
                            SeenSnapshots.Add(name);
                        }
                    }
                }
            }
        } catch (Exception e) {
            Logger.Error($"Failed to load captured music data: {e}");
        }

        CustomHooks.ApplyMusicCueFromFsmAction += OnCapturedApplyMusicCue;
        CustomHooks.TransitionToAudioSnapshotFromFsmAction += OnCapturedTransitionToAudioSnapshot;
        */
    }

    /// <summary>
    /// Deinitializes the music capture system, unregistering event handlers.
    /// </summary>
    public static void DeinitializeCapture() {
        //CustomHooks.ApplyMusicCueFromFsmAction -= OnCapturedApplyMusicCue;
        //CustomHooks.TransitionToAudioSnapshotFromFsmAction -= OnCapturedTransitionToAudioSnapshot;
    }

    #endregion

    #region Capture tooling handlers

    /// <summary>
    /// Intercepts music cue application to record and save the cue name.
    /// </summary>
    /// <param name="action">The music cue action being intercepted.</param>
    private static void OnCapturedApplyMusicCue(ApplyMusicCue action) {
        if (action.musicCue.Value is not MusicCue musicCue) return;

        lock (SeenMusicCues) {
            if (!SeenMusicCues.Add(musicCue.name)) return;
        }

        Logger.Info($"[Capture] New MusicCue: {musicCue.name}");
        SaveCapturedData();
    }

    /// <summary>
    /// Intercepts audio snapshot transitions to record and save the snapshot name.
    /// </summary>
    /// <param name="action">The transition action being intercepted.</param>
    private static void OnCapturedTransitionToAudioSnapshot(TransitionToAudioSnapshot action) {
        if (action.snapshot.Value is not AudioMixerSnapshot snapshot) return;

        lock (SeenSnapshots) {
            if (!SeenSnapshots.Add(snapshot.name)) return;
        }

        Logger.Info($"[Capture] New AudioMixerSnapshot: {snapshot.name}");
        SaveCapturedData();
    }

    /// <summary>
    /// Saves the captured music cues and audio snapshots to the JSON configuration file.
    /// </summary>
    private static void SaveCapturedData() {
        try {
            var configPath = FileUtil.GetConfigPath();
            Directory.CreateDirectory(configPath);

            List<string> cues;
            List<string> snapshots;

            lock (SeenMusicCues) {
                cues = new List<string>(SeenMusicCues);
                cues.Sort();
            }

            lock (SeenSnapshots) {
                snapshots = new List<string>(SeenSnapshots);
                snapshots.Sort();
            }

            FileUtil.WriteObjectToJsonFile(
                new CapturedMusicContainer { MusicCues = cues, Snapshots = snapshots },
                Path.Combine(configPath, CapturedMusicFileName)
            );
        } catch (Exception e) {
            Logger.Error($"Failed to save captured music data: {e}");
        }
    }

    #endregion
}

#region Data containers

// ReSharper disable ClassNeverInstantiated.Global
/// <summary>
/// Data for music cues, used for looking up the index or the music cue from index for networking purposes.
/// </summary>
internal class MusicCueData {
    public string Type { get; }
    public string Name { get; }

    [JsonConstructor]
    public MusicCueData(string type, string name) {
        Type = type;
        Name = name;
    }

    [JsonIgnore] public byte Index { get; set; }

    [JsonIgnore] public MusicCue? MusicCue { get; set; }
}

// ReSharper disable ClassNeverInstantiated.Global
/// <summary>
/// Data for audio snapshots, used for looking up the index or the audio snapshot from index for networking purposes.
/// </summary>
internal class AudioMixerSnapshotData {
    public string Type { get; }
    public string Name { get; }

    [JsonConstructor]
    public AudioMixerSnapshotData(string type, string name) {
        Type = type;
        Name = name;
    }

    [JsonIgnore] public byte Index { get; set; }

    [JsonIgnore] public AudioMixerSnapshot? Snapshot { get; set; }
}

/// <summary>
/// Container for storing collections of captured music cues and audio snapshots.
/// </summary>
internal class CapturedMusicContainer {
    /// <summary>
    /// Gets the list of captured music cue names.
    /// </summary>
    public List<string> MusicCues { get; init; } = [];

    /// <summary>
    /// Gets the list of captured audio snapshot names.
    /// </summary>
    public List<string> Snapshots { get; init; } = [];
}

#endregion

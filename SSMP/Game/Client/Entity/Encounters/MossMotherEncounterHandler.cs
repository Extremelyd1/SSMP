using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using SSMP.Game.Client.Entity.Component;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Game.Client.Entity.Encounters;

/// <summary>
/// Encounter handler for the Mossgrub Mother boss fight in scene <c>Tut_03</c>.
/// <para>
/// Manages the guest-side battle scene when the guest has already defeated the boss
/// on their own save. Without this handler, the native Unity components
/// (<see cref="DeactivateIfPlayerdataTrue"/>, <see cref="PersistentBoolItem"/>) would
/// silently deactivate the encounter before it could start for the guest.
/// </para>
/// </summary>
internal class MossMotherEncounterHandler : IEncounterHandler {
    /// <summary>Registry entry for the stalactite selector ("PI Slam") entity.</summary>
    private static readonly EntityRegistryEntry StalactiteSelectorEntry = EntityRegistryEntry.Create(
        baseObjectName: "PI Slam",
        type: EntityType.MossMotherStalactiteSelector,
        parentName: StalactitesContainerName
    );

    /// <summary>Registry entry for individual stalactite ("Mossbone Stalactite") entities.</summary>
    private static readonly EntityRegistryEntry StalactiteEntry = EntityRegistryEntry.Create(
        baseObjectName: "Mossbone Stalactite",
        type: EntityType.MossMotherStalactite,
        parentName: StalactitesContainerName,
        componentTypes: [
            EntityComponentType.Velocity,
            EntityComponentType.GravityScale
        ],
        fsmNames: ["Control"]
    );

    /// <summary>All entity registry entries owned by this handler.</summary>
    private static readonly EntityRegistryEntry[] LocalEntityEntries = [
        StalactiteSelectorEntry,
        StalactiteEntry
    ];

    /// <summary>
    /// Snapshot of a managed gate's initial state, captured once at registration.
    /// Used to manually position and toggle the gate when its FSM is bypassed.
    /// </summary>
    private sealed class ManagedGateData {
        /// <summary>Local position of the gate when open (captured at registration, after FSM init).</summary>
        public required Vector3 OpenLocalPosition { get; init; }

        /// <summary>All <see cref="Collider2D"/> components in the gate hierarchy.</summary>
        public required Collider2D[] Colliders { get; init; }

        /// <summary>All <see cref="Renderer"/> components in the gate hierarchy.</summary>
        public required Renderer[] Renderers { get; init; }
    }

    /// <summary>
    /// Snapshot of a managed stalactite's initial state, captured once at registration.
    /// Used to reset the stalactite to dormant when its FSM is bypassed.
    /// </summary>
    private sealed class ManagedStalactiteData {
        /// <summary>Local position of the stalactite when dormant (captured at registration).</summary>
        public required Vector3 StartLocalPosition { get; init; }

        /// <summary>Optional rigidbody; null if the stalactite has no <see cref="Rigidbody2D"/>.</summary>
        public required Rigidbody2D? Rigidbody { get; init; }

        /// <summary>All <see cref="Collider2D"/> components in the stalactite hierarchy.</summary>
        public required Collider2D[] Colliders { get; init; }

        /// <summary>All <see cref="DamageHero"/> components in the stalactite hierarchy.</summary>
        public required DamageHero[] DamageHeroes { get; init; }

        /// <summary>All <see cref="Renderer"/> components in the stalactite hierarchy.</summary>
        public required Renderer[] Renderers { get; init; }
    }

    private const string MossMotherScene = "Tut_03";
    private const string DefeatedMossMotherKey = "defeatedMossMother";
    private const string BattleSceneName = "Battle Scene";
    private const string GatesContainerName = "Gates";
    private const string StalactitesContainerName = "Stals";
    private const string GateNamePrefix = "Battle Gate Mossbone";
    private const string StalactiteNamePrefix = "Mossbone Stalactite";
    private const string StalactiteSelectorName = "PI Slam";
    private const string BossActionStateStartBattle = "Start Battle";
    private const string GateCloseEvent = "BG CLOSE";
    private const string GateOpenEvent = "BG QUICK OPEN";
    private const string StalactiteFallEvent = "FALL";

    /// <summary>
    /// Minimum seconds between successive fallback stalactite triggers.
    /// Throttles the round-robin path used when the stalactite selector entity is not synced.
    /// </summary>
    private const float StalactiteTriggerCooldown = 0.75f;

    /// <summary>
    /// FSM states on the Moss Mother entity that signal the battle has begun.
    /// Any of these reaching the guest is sufficient to start the managed battle scene.
    /// </summary>
    private static readonly HashSet<string> BattleStartStates = [
        BossActionStateStartBattle,
        "Wake",
        "Burst Out",
        "Roar",
        "Roar 2"
    ];

    /// <summary>Whether this client is acting as the guest for the current scene load.</summary>
    private bool _isGuestRole;

    /// <summary>
    /// Whether the guest has already defeated the boss and we are actively driving
    /// a reconstructed battle scene on their client.
    /// </summary>
    private bool _isManagedGuestScene;

    /// <summary>Root GameObject of the native "Battle Scene" object in the loaded scene.</summary>
    private GameObject? _nativeBattleSceneRoot;

    /// <summary>
    /// Root of the battle scene we are driving for the guest.
    /// Currently equals <see cref="_nativeBattleSceneRoot"/> — we reuse the native
    /// hierarchy rather than cloning it.
    /// </summary>
    private GameObject? _managedBattleSceneRoot;

    /// <summary><see cref="BattleScene"/> component on <see cref="_managedBattleSceneRoot"/>.</summary>
    private BattleScene? _managedBattleScene;

    /// <summary>Guards against <see cref="StartManagedBattleScene"/> executing more than once per scene load.</summary>
    private bool _managedBattleStarted;

    /// <summary>
    /// Ring-buffer index into <see cref="_managedStalactites"/> for the round-robin fallback trigger path.
    /// </summary>
    private int _nextManagedStalactiteIndex;

    /// <summary>
    /// <see cref="Time.time"/> at which the last fallback stalactite trigger fired.
    /// Initialized to <see cref="float.NegativeInfinity"/> so the first trigger is never throttled.
    /// </summary>
    private float _lastManagedStalactiteTriggerTime = float.NegativeInfinity;

    /// <summary>True once a <see cref="EntityType.MossMotherStalactiteSelector"/> entity has been registered.</summary>
    private bool _hasSyncedStalactiteSelector;

    /// <summary>True once a <see cref="EntityType.MossMotherStalactite"/> entity has been registered.</summary>
    private bool _hasSyncedStalactites;

    /// <summary>All managed gate GameObjects discovered in the battle scene hierarchy.</summary>
    private readonly List<GameObject> _managedGates = [];

    /// <summary>All managed stalactite GameObjects discovered in the battle scene hierarchy.</summary>
    private readonly List<GameObject> _managedStalactites = [];

    /// <summary>Cached initial state keyed by gate GameObject. Populated in <see cref="RegisterManagedGate"/>.</summary>
    private readonly Dictionary<GameObject, ManagedGateData> _managedGateData = [];

    /// <summary>Cached initial state keyed by stalactite GameObject. Populated in <see cref="RegisterManagedStalactite"/>.</summary>
    private readonly Dictionary<GameObject, ManagedStalactiteData> _managedStalactiteData = [];

    /// <summary>
    /// Cached "Gates" container transform under <see cref="_managedBattleSceneRoot"/>.
    /// Avoids repeated <see cref="Transform.Find"/> calls in <see cref="IsManagedPropTarget"/>.
    /// </summary>
    private Transform? _managedGatesRoot;

    /// <summary>
    /// Cached "Stals" container transform under <see cref="_managedBattleSceneRoot"/>.
    /// Avoids repeated <see cref="Transform.Find"/> calls in <see cref="IsManagedPropTarget"/>.
    /// </summary>
    private Transform? _managedStalactitesRoot;

    /// <inheritdoc />
    public IEnumerable<string> SupportedScenes => [MossMotherScene];

    /// <inheritdoc />
    public bool TryGetEntityEntry(GameObject gameObject, [NotNullWhen(true)] out EntityRegistryEntry? entry) {
        entry = null;

        if (gameObject.scene.name != MossMotherScene) {
            return false;
        }

        return EntityRegistry.TryGetEntry(LocalEntityEntries, gameObject, out entry);
    }

    /// <inheritdoc />
    public void OnSceneLoaded(Scene scene, bool isHost) {
        ResetSceneState();

        _isGuestRole = !isHost;
        if (isHost) {
            if (IsBossDefeatedOnGuestSave()) {
                PrepareHostNativeBattleScene(scene);
            } else {
                Logger.Info("MossMotherEncounterHandler: host scene detected, leaving native encounter logic intact.");
            }

            return;
        }

        if (!IsBossDefeatedOnGuestSave()) {
            Logger.Info(
                "MossMotherEncounterHandler: guest has not defeated Moss Mother, keeping native scene authoritative."
            );
            SyncExistingEntities();
            return;
        }

        _isManagedGuestScene = true;
        _nativeBattleSceneRoot = FindSceneObject(scene, BattleSceneName);
        PrepareGuestNativeBattleScene();
        SyncExistingEntities();
    }

    /// <inheritdoc />
    public void OnEntityRegistered(Entity entity) {
        if (entity.Type == EntityType.MossMotherStalactiteSelector) {
            _hasSyncedStalactiteSelector = true;
        }

        if (entity.Type == EntityType.MossMotherStalactite) {
            _hasSyncedStalactites = true;
        }

        if (!_isGuestRole) {
            return;
        }

        if (entity.Object.Client != null) {
            AttachClientEntityToManagedHierarchy(entity);
            ActivateHierarchyChain(entity.Object.Client);
        }
    }

    /// <inheritdoc />
    public void OnEntityFsmStateChanged(Entity entity, PlayMakerFSM fsm, string stateName) {
        if (!_isManagedGuestScene || entity.Type != EntityType.MossMother || string.IsNullOrWhiteSpace(stateName)) {
            return;
        }

        Logger.Debug($"MossMotherEncounterHandler: observed FSM state '{fsm.Fsm.Name}/{stateName}'.");

        if (BattleStartStates.Contains(stateName)) {
            StartManagedBattleScene();
        }
    }

    /// <inheritdoc />
    public bool OnEntityFsmAction(Entity entity, string stateName, FsmStateAction action) {
        if (!_isManagedGuestScene || entity.Type != EntityType.MossMother) {
            return false;
        }

        if (BattleStartStates.Contains(stateName)) {
            StartManagedBattleScene();
        }

        switch (action) {
            case CallMethodProper callMethodAction:
                HandleCallMethodAction(stateName, callMethodAction);
                break;
            case SendEventByName sendEventByNameAction:
                if (ShouldSuppressBossStalactiteEvent(
                        sendEventByNameAction.sendEvent.Value, sendEventByNameAction.eventTarget
                    )) {
                    return true;
                }

                MirrorTargetedEvent(sendEventByNameAction.sendEvent.Value, sendEventByNameAction.eventTarget);
                break;
            case SendEventByNameV2 sendEventByNameV2Action:
                if (ShouldSuppressBossStalactiteEvent(
                        sendEventByNameV2Action.sendEvent.Value, sendEventByNameV2Action.eventTarget
                    )) {
                    return true;
                }

                MirrorTargetedEvent(sendEventByNameV2Action.sendEvent.Value, sendEventByNameV2Action.eventTarget);
                break;
        }

        return false;
    }

    /// <summary>
    /// Resets all per-scene mutable state to initial values.
    /// Must be the first call in every <see cref="OnSceneLoaded"/>.
    /// </summary>
    private void ResetSceneState() {
        _isGuestRole = false;
        _isManagedGuestScene = false;
        _nativeBattleSceneRoot = null;
        _managedBattleSceneRoot = null;
        _managedBattleScene = null;
        _managedBattleStarted = false;
        _nextManagedStalactiteIndex = 0;
        _lastManagedStalactiteTriggerTime = float.NegativeInfinity;
        _hasSyncedStalactiteSelector = false;
        _hasSyncedStalactites = false;
        _managedGatesRoot = null;
        _managedStalactitesRoot = null;
        _managedGates.Clear();
        _managedStalactites.Clear();
        _managedGateData.Clear();
        _managedStalactiteData.Clear();
    }

    /// <summary>
    /// Returns <see langword="true"/> if the guest's local save marks Moss Mother as defeated.
    /// </summary>
    private static bool IsBossDefeatedOnGuestSave() {
        return PlayerData.instance != null && PlayerData.instance.GetBool(DefeatedMossMotherKey);
    }

    /// <summary>
    /// Iterates all currently registered entities and calls <see cref="OnEntityRegistered"/> for each,
    /// catching up any entities that were registered before this handler was ready.
    /// </summary>
    private void SyncExistingEntities() {
        if (EntityManager.Instance == null) {
            return;
        }

        // ToList() defensive copy: OnEntityRegistered must not mutate Entities mid-iteration.
        foreach (var entity in EntityManager.Instance.Entities.ToList()) {
            OnEntityRegistered(entity);
        }
    }

    /// <summary>
    /// When the host's save has already defeated the boss, strips the revisit-guard
    /// components so the encounter can play again for the multiplayer session.
    /// </summary>
    private static void PrepareHostNativeBattleScene(Scene scene) {
        var battleSceneRoot = FindSceneObject(scene, BattleSceneName);
        if (battleSceneRoot == null) {
            Logger.Warn("MossMotherEncounterHandler: host defeated-save path could not find native Battle Scene.");
            return;
        }

        StripBattleSceneRevisitGuards(battleSceneRoot);
        ActivateHierarchyChain(battleSceneRoot);
        Logger.Info("MossMotherEncounterHandler: prepared native host battle scene for defeated-save re-encounter.");
    }

    /// <summary>
    /// When the guest's save has already defeated the boss, strips revisit-guard components
    /// and configures the managed battle scene the guest client will experience.
    /// </summary>
    private void PrepareGuestNativeBattleScene() {
        if (_nativeBattleSceneRoot == null) {
            Logger.Warn("MossMotherEncounterHandler: guest defeated-save path could not find native Battle Scene.");
            return;
        }

        StripBattleSceneRevisitGuards(_nativeBattleSceneRoot);

        _managedBattleSceneRoot = _nativeBattleSceneRoot;
        _managedBattleScene = _nativeBattleSceneRoot.GetComponent<BattleScene>();

        // Cache container roots now so IsManagedPropTarget never needs to call Find at runtime.
        _managedGatesRoot = _managedBattleSceneRoot.transform.Find(GatesContainerName);
        _managedStalactitesRoot = _managedBattleSceneRoot.transform.Find(StalactitesContainerName);

        ActivateHierarchyChain(_nativeBattleSceneRoot);

        // Disable the trigger colliders on the scene root so the guest entering the room
        // does not auto-fire native battle-start logic.
        foreach (var trigger in _nativeBattleSceneRoot.GetComponents<Collider2D>()) {
            trigger.enabled = false;
        }

        DiscoverManagedBattleSceneObjects(_nativeBattleSceneRoot);
        Logger.Info(
            $"MossMotherEncounterHandler: using native guest Battle Scene with {_managedGates.Count} gate(s) and {_managedStalactites.Count} stalactite root(s)."
        );
    }

    /// <summary>
    /// Destroys components that would deactivate or short-circuit the battle scene on a
    /// save that has already completed the fight (<see cref="DeactivateIfPlayerdataTrue"/>,
    /// <see cref="PersistentBoolItem"/>), and clears end-of-battle player-data write-backs
    /// on the <see cref="BattleScene"/> component.
    /// </summary>
    private static void StripBattleSceneRevisitGuards(GameObject battleSceneRoot) {
        foreach (var guard in battleSceneRoot.GetComponentsInChildren<DeactivateIfPlayerdataTrue>(true)) {
            Object.Destroy(guard);
        }

        foreach (var persistentBool in battleSceneRoot.GetComponentsInChildren<PersistentBoolItem>(true)) {
            Object.Destroy(persistentBool);
        }

        var battleScene = battleSceneRoot.GetComponent<BattleScene>();
        if (battleScene != null) {
            battleScene.setPDBoolOnEnd = string.Empty;
            battleScene.setExtraPDBoolOnEnd = string.Empty;
        }
    }

    /// <summary>
    /// Walks the "Gates" and "Stals" containers under <paramref name="managedBattleSceneRoot"/>,
    /// prepares and registers every gate and stalactite found there.
    /// </summary>
    private void DiscoverManagedBattleSceneObjects(GameObject managedBattleSceneRoot) {
        foreach (var gate in FindBattleSceneObjects(managedBattleSceneRoot, GatesContainerName, GateNamePrefix)) {
            PrepareManagedGate(gate);
            _managedGates.Add(gate);
            RegisterManagedGate(gate);
            Logger.Info(
                $"MossMotherEncounterHandler: discovered managed gate '{gate.name}' with {gate.GetComponentsInChildren<PlayMakerFSM>(true).Length} FSM(s)."
            );
        }

        foreach (var stalactite in FindBattleSceneObjects(
                     managedBattleSceneRoot, StalactitesContainerName, StalactiteNamePrefix
                 )) {
            PrepareManagedStalactite(stalactite);
            _managedStalactites.Add(stalactite);
            RegisterManagedStalactite(stalactite);
            Logger.Info(
                $"MossMotherEncounterHandler: discovered managed stalactite '{stalactite.name}' with {stalactite.GetComponentsInChildren<PlayMakerFSM>(true).Length} FSM(s)."
            );
        }
    }

    /// <summary>
    /// Re-parents the client-side copy of a synced entity to mirror the same hierarchy
    /// position as its host counterpart, relative to <see cref="_managedBattleSceneRoot"/>.
    /// </summary>
    private void AttachClientEntityToManagedHierarchy(Entity entity) {
        if (!_isManagedGuestScene || _nativeBattleSceneRoot == null || _managedBattleSceneRoot == null) {
            return;
        }

        var hostObject = entity.Object.Host;
        var clientObject = entity.Object.Client;
        if (hostObject == null || clientObject == null) {
            return;
        }

        if (ReferenceEquals(hostObject, clientObject)) {
            return;
        }

        var hostParent = hostObject.transform.parent;
        if (hostParent == null) {
            return;
        }

        var relativeParentPath = GetRelativePath(_nativeBattleSceneRoot.transform, hostParent);
        if (relativeParentPath == null) {
            return;
        }

        var managedParent = string.IsNullOrEmpty(relativeParentPath)
            ? _managedBattleSceneRoot.transform
            : _managedBattleSceneRoot.transform.Find(relativeParentPath);

        if (managedParent == null) {
            Logger.Warn(
                $"MossMotherEncounterHandler: could not map managed parent for entity '{clientObject.name}' from '{hostParent.name}'."
            );
            return;
        }

        if (clientObject.transform.parent == managedParent) {
            return;
        }

        clientObject.transform.SetParent(managedParent, worldPositionStays: true);
        clientObject.transform.SetSiblingIndex(
            System.Math.Min(hostObject.transform.GetSiblingIndex(), managedParent.childCount - 1)
        );

        Logger.Info(
            $"MossMotherEncounterHandler: attached synced entity '{clientObject.name}' under managed '{managedParent.name}'."
        );
    }

    /// <summary>
    /// Handles a <see cref="CallMethodProper"/> action from the boss FSM.
    /// Starts the managed battle scene if the action is a <see cref="BattleScene.StartBattle"/> call.
    /// <para>
    /// This is a secondary trigger path. The state-name check earlier in
    /// <see cref="OnEntityFsmAction"/> already covers the same case; both paths are guarded
    /// by <see cref="_managedBattleStarted"/> so firing twice is harmless.
    /// </para>
    /// </summary>
    private void HandleCallMethodAction(string stateName, CallMethodProper action) {
        if (_managedBattleScene == null || action.behaviour.Value == null || action.methodName.Value == null) {
            return;
        }

        if (stateName != BossActionStateStartBattle) {
            return;
        }

        if (action.behaviour.Value != nameof(BattleScene) ||
            action.methodName.Value != nameof(BattleScene.StartBattle)) {
            return;
        }

        StartManagedBattleScene();
    }

    /// <summary>
    /// Forwards a boss FSM event that targets a native battle scene object to its corresponding
    /// managed clone. The special case where the event targets the stalactite container ("Stals")
    /// routes instead to the round-robin fallback trigger.
    /// </summary>
    private void MirrorTargetedEvent(string? eventName, FsmEventTarget? eventTarget) {
        if (_managedBattleSceneRoot == null || string.IsNullOrWhiteSpace(eventName) || eventTarget == null) {
            return;
        }

        var originalTarget = eventTarget.gameObject.GameObject.Value;
        if (originalTarget == null) {
            return;
        }

        if (eventName == StalactiteFallEvent && originalTarget.name == StalactitesContainerName) {
            if (_hasSyncedStalactiteSelector) {
                Logger.Info(
                    "MossMotherEncounterHandler: skipping manual FALL routing because stalactite selector entity is synced."
                );
                return;
            }

            Logger.Info("MossMotherEncounterHandler: routing container FALL event to explicitly-selected stalactite.");
            TriggerNextManagedStalactite();
            return;
        }

        var managedTarget = FindManagedCloneForOriginal(originalTarget);
        if (managedTarget == null || !IsManagedPropTarget(managedTarget)) {
            return;
        }

        Logger.Info(
            $"MossMotherEncounterHandler: mirrored event '{eventName}' from '{originalTarget.name}' to managed '{managedTarget.name}'."
        );
        SendEventToManagedObject(managedTarget, eventName);
    }

    /// <summary>
    /// Returns <see langword="true"/> when a boss FSM "FALL" event targeting the stalactite
    /// selector or container should be suppressed because synced entities will drive the
    /// outcome instead.
    /// </summary>
    private bool ShouldSuppressBossStalactiteEvent(string? eventName, FsmEventTarget? eventTarget) {
        if (!_hasSyncedStalactiteSelector || !_hasSyncedStalactites || eventName != StalactiteFallEvent
            || eventTarget == null) {
            return false;
        }

        var originalTarget = eventTarget.gameObject.GameObject.Value;
        if (originalTarget == null) {
            return false;
        }

        if (originalTarget.name != StalactitesContainerName && originalTarget.name != StalactiteSelectorName) {
            return false;
        }

        Logger.Info(
            "MossMotherEncounterHandler: suppressing local boss->stalactite selector event; synced selector/stalactite entities will drive the result."
        );
        return true;
    }

    /// <summary>
    /// Begins the managed battle scene on the guest client: closes gates and calls
    /// <see cref="BattleScene.StartBattle"/>. Idempotent; subsequent calls are no-ops.
    /// </summary>
    private void StartManagedBattleScene() {
        if (_managedBattleScene == null || _managedBattleStarted) {
            return;
        }

        _managedBattleStarted = true;
        ActivateHierarchyChain(_managedBattleScene.gameObject);
        Logger.Info("MossMotherEncounterHandler: starting managed guest battle scene.");

        _managedBattleScene.SendEventToChildren(GateCloseEvent);
        foreach (var gate in _managedGates) {
            EnableVisuals(gate);
            SendEventToManagedObject(gate, GateCloseEvent);
        }

        _managedBattleScene.StartBattle();
    }

    /// <summary>
    /// Triggers the next stalactite in round-robin order on the fallback path,
    /// used when the stalactite selector entity is not synced.
    /// Throttled to at most one trigger per <see cref="StalactiteTriggerCooldown"/> seconds.
    /// </summary>
    private void TriggerNextManagedStalactite() {
        if (_managedStalactites.Count == 0) {
            return;
        }

        if (Time.time - _lastManagedStalactiteTriggerTime < StalactiteTriggerCooldown) {
            return;
        }

        _lastManagedStalactiteTriggerTime = Time.time;
        var stalactite = _managedStalactites[_nextManagedStalactiteIndex % _managedStalactites.Count];
        _nextManagedStalactiteIndex++;

        Logger.Info($"MossMotherEncounterHandler: fallback-triggering native stalactite FSM '{stalactite.name}'.");
        EnableVisuals(stalactite);
        SendEventToManagedObject(stalactite, StalactiteFallEvent);
    }

    /// <summary>
    /// Activates a gate's hierarchy, initializes all FSMs to the "Open" state,
    /// and sends the quick-open event.
    /// </summary>
    private static void PrepareManagedGate(GameObject gate) {
        ActivateHierarchyChain(gate);
        foreach (var fsm in gate.GetComponentsInChildren<PlayMakerFSM>(true)) {
            fsm.enabled = true;
            EntityInitializer.InitializeFsm(fsm);
            if (fsm.ActiveStateName != "Open") {
                fsm.SetState("Open");
            }
        }

        SendEventToManagedObject(gate, GateOpenEvent);
    }

    /// <summary>
    /// Activates a stalactite's hierarchy and initializes all FSMs to the "Dormant" state.
    /// </summary>
    private static void PrepareManagedStalactite(GameObject stalactite) {
        ActivateHierarchyChain(stalactite);
        foreach (var fsm in stalactite.GetComponentsInChildren<PlayMakerFSM>(true)) {
            fsm.enabled = true;
            EntityInitializer.InitializeFsm(fsm);
            if (fsm.ActiveStateName != "Dormant") {
                fsm.SetState("Dormant");
            }
        }
    }

    /// <summary>
    /// Snapshots a gate's initial state into <see cref="_managedGateData"/> and
    /// applies the open state via <see cref="OpenManagedGate"/>.
    /// </summary>
    private void RegisterManagedGate(GameObject gate) {
        _managedGateData[gate] = new ManagedGateData {
            OpenLocalPosition = gate.transform.localPosition,
            Colliders = gate.GetComponentsInChildren<Collider2D>(true),
            Renderers = gate.GetComponentsInChildren<Renderer>(true)
        };

        OpenManagedGate(gate);
    }

    /// <summary>
    /// Restores a gate to its open state: moves it to the recorded open position,
    /// enables renderers, and disables colliders.
    /// </summary>
    private void OpenManagedGate(GameObject gate) {
        if (!_managedGateData.TryGetValue(gate, out var gateData)) {
            return;
        }

        gate.transform.localPosition = gateData.OpenLocalPosition;

        foreach (var renderer in gateData.Renderers) {
            renderer.enabled = true;
        }

        foreach (var collider in gateData.Colliders) {
            collider.enabled = false;
        }
    }

    /// <summary>
    /// Snapshots a stalactite's initial state into <see cref="_managedStalactiteData"/> and
    /// applies the dormant state via <see cref="ResetManagedStalactite"/>.
    /// </summary>
    private void RegisterManagedStalactite(GameObject stalactite) {
        _managedStalactiteData[stalactite] = new ManagedStalactiteData {
            StartLocalPosition = stalactite.transform.localPosition,
            Rigidbody = stalactite.GetComponent<Rigidbody2D>(),
            Colliders = stalactite.GetComponentsInChildren<Collider2D>(true),
            DamageHeroes = stalactite.GetComponentsInChildren<DamageHero>(true),
            Renderers = stalactite.GetComponentsInChildren<Renderer>(true)
        };

        ResetManagedStalactite(stalactite);
    }

    /// <summary>
    /// Resets a stalactite to its dormant state: restores its start position, enables
    /// renderers, disables colliders and damage components, and zeroes the rigidbody.
    /// </summary>
    private void ResetManagedStalactite(GameObject stalactite) {
        if (!_managedStalactiteData.TryGetValue(stalactite, out var stalactiteData)) {
            return;
        }

        stalactite.transform.localPosition = stalactiteData.StartLocalPosition;

        foreach (var renderer in stalactiteData.Renderers) {
            renderer.enabled = true;
        }

        foreach (var collider in stalactiteData.Colliders) {
            collider.enabled = false;
        }

        foreach (var damageHero in stalactiteData.DamageHeroes) {
            damageHero.enabled = false;
        }

        if (stalactiteData.Rigidbody != null) {
            stalactiteData.Rigidbody.linearVelocity = Vector2.zero;
            stalactiteData.Rigidbody.angularVelocity = 0f;
            stalactiteData.Rigidbody.bodyType = RigidbodyType2D.Kinematic;
            stalactiteData.Rigidbody.simulated = true;
        }
    }

    /// <summary>
    /// Finds the managed clone of a native object by computing its path relative to
    /// <see cref="_nativeBattleSceneRoot"/> and resolving the same path under
    /// <see cref="_managedBattleSceneRoot"/>.
    /// Returns <see langword="null"/> if the original is not under the native root.
    /// </summary>
    private GameObject? FindManagedCloneForOriginal(GameObject original) {
        if (_nativeBattleSceneRoot == null || _managedBattleSceneRoot == null) {
            return null;
        }

        var path = GetRelativePath(_nativeBattleSceneRoot.transform, original.transform);
        if (path == null) {
            return null;
        }

        if (string.IsNullOrEmpty(path)) {
            return _managedBattleSceneRoot;
        }

        return _managedBattleSceneRoot.transform.Find(path)?.gameObject;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="managedTarget"/> is a descendant
    /// of the managed "Gates" or "Stals" containers — i.e. it is a prop we own,
    /// not an arbitrary scene object.
    /// </summary>
    private bool IsManagedPropTarget(GameObject managedTarget) {
        return IsDescendantOf(managedTarget.transform, _managedGatesRoot)
               || IsDescendantOf(managedTarget.transform, _managedStalactitesRoot);
    }

    /// <summary>
    /// Computes the "/" delimited path from <paramref name="root"/> down to <paramref name="target"/>.
    /// Returns <see langword="null"/> if <paramref name="target"/> is not under <paramref name="root"/>.
    /// Returns <see cref="string.Empty"/> if <paramref name="target"/> is <paramref name="root"/>.
    /// </summary>
    private static string? GetRelativePath(Transform root, Transform target) {
        var segments = new Stack<string>();
        var current = target;

        while (current != null && current != root) {
            segments.Push(current.name);
            current = current.parent;
        }

        return current == root ? string.Join("/", segments) : null;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="target"/> is <paramref name="ancestor"/>
    /// or any object in its subtree.
    /// </summary>
    private static bool IsDescendantOf(Transform? target, Transform? ancestor) {
        if (target == null || ancestor == null) {
            return false;
        }

        var current = target;
        while (current != null) {
            if (current == ancestor) {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    /// <summary>
    /// Enables all <see cref="Renderer"/> components and activates all <see cref="tk2dBaseSprite"/>
    /// GameObjects in the hierarchy rooted at <paramref name="gameObject"/>.
    /// </summary>
    private static void EnableVisuals(GameObject gameObject) {
        foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>(true)) {
            renderer.enabled = true;
        }

        foreach (var sprite in gameObject.GetComponentsInChildren<tk2dBaseSprite>(true)) {
            sprite.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Sends <paramref name="eventName"/> to every <see cref="PlayMakerFSM"/> in the hierarchy
    /// of <paramref name="gameObject"/>, activating the hierarchy chain first.
    /// </summary>
    private static void SendEventToManagedObject(GameObject gameObject, string eventName) {
        if (gameObject == null || string.IsNullOrWhiteSpace(eventName)) {
            return;
        }

        ActivateHierarchyChain(gameObject);
        foreach (var fsm in gameObject.GetComponentsInChildren<PlayMakerFSM>(true)) {
            fsm.enabled = true;
            fsm.SendEvent(eventName);
        }
    }

    /// <summary>
    /// Walks from <paramref name="gameObject"/> up to the scene root, calling
    /// <see cref="GameObject.SetActive(bool)">SetActive(true)</see> on every ancestor (inclusive).
    /// <para>
    /// <b>Note:</b> This unconditionally activates every object all the way to the scene root.
    /// Objects that were intentionally deactivated by other systems will be re-enabled.
    /// </para>
    /// </summary>
    private static void ActivateHierarchyChain(GameObject gameObject) {
        if (gameObject == null) {
            return;
        }

        var current = gameObject.transform;
        while (current != null) {
            current.gameObject.SetActive(true);
            current = current.parent;
        }
    }

    /// <summary>
    /// Finds the first GameObject in <paramref name="scene"/> with the given <paramref name="name"/>.
    /// Returns <see langword="null"/> if none is found.
    /// </summary>
    private static GameObject? FindSceneObject(Scene scene, string name) {
        return FindSceneObjects(scene, [name]).FirstOrDefault();
    }

    /// <summary>
    /// Returns all immediate children of the named container under <paramref name="battleScene"/>
    /// whose names start with <paramref name="objectNamePrefix"/>.
    /// </summary>
    private static IEnumerable<GameObject> FindBattleSceneObjects(
        GameObject battleScene,
        string containerName,
        string objectNamePrefix
    ) {
        var container = battleScene.transform.Find(containerName);
        if (container == null) {
            Logger.Warn($"MossMotherEncounterHandler: could not find '{battleScene.name}/{containerName}'.");
            return [];
        }

        var results = new List<GameObject>();
        for (var i = 0; i < container.childCount; i++) {
            var child = container.GetChild(i);
            if (child.name.StartsWith(objectNamePrefix)) {
                results.Add(child.gameObject);
            }
        }

        return results;
    }

    /// <summary>
    /// Searches all loaded transforms for those belonging to <paramref name="scene"/>
    /// whose names are in <paramref name="names"/>.
    /// <para>
    /// <b>Warning:</b> Uses <see cref="Resources.FindObjectsOfTypeAll{T}"/>, which is expensive.
    /// Only ever call this during scene setup, never per-frame.
    /// </para>
    /// </summary>
    private static IEnumerable<GameObject> FindSceneObjects(Scene scene, IEnumerable<string> names) {
        var nameSet = new HashSet<string>(names);

        // Resources.FindObjectsOfTypeAll can include the same instance multiple times
        // across Unity's internal prefab and scene caches; GroupBy deduplicates by instance ID.
        return Resources.FindObjectsOfTypeAll<Transform>()
                        .Where(t =>
                            t.hideFlags == HideFlags.None &&
                            t.gameObject.scene == scene &&
                            nameSet.Contains(t.name)
                        )
                        .Select(t => t.gameObject)
                        .GroupBy(go => go.GetInstanceID())
                        .Select(group => group.First());
    }
}

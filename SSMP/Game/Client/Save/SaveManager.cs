using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GlobalEnums;
using SSMP.Collection;
using SSMP.Game.Client.Entity;
using SSMP.Networking.Client;
using SSMP.Networking.Packet.Data;
using SSMP.Util;
using TeamCherry.SharedUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = SSMP.Logging.Logger;
using MapZone = GlobalEnums.MapZone;
using Object = UnityEngine.Object;

// ReSharper disable AssignNullToNotNullAttribute
#pragma warning disable CS8620 // Argument cannot be used for parameter due to differences in the nullability of
// reference types.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace SSMP.Game.Client.Save;

/// <summary>
/// Class that manages save data synchronisation.
/// </summary>
internal class SaveManager {
    /// <summary>
    /// The save data instance that contains mappings for what to sync and their indices.
    /// </summary>
    private static SaveDataMapping SaveDataMapping => SaveDataMapping.Instance;

    /// <summary>
    /// The interval in seconds at which the PlayerData is checked for changes.
    /// </summary>
    private const float PlayerDataCheckInterval = 0.1f;

    /// <summary>
    /// The fields of PlayerData by name, because looking up a field by name scans all fields of the class.
    /// </summary>
    private static readonly Dictionary<string, FieldInfo> PlayerDataFields =
        typeof(PlayerData).GetFields().ToDictionary(field => field.Name);

    /// <summary>
    /// The net client instance to send save updates.
    /// </summary>
    private readonly NetClient _netClient;

    /// <summary>
    /// The entity manager to check whether we are scene host.
    /// </summary>
    private readonly EntityManager _entityManager;

    /// <summary>
    /// The save changes instance to apply immediate in-world changes from received save data.
    /// </summary>
    private readonly SaveChanges _saveChanges;

    /// <summary>
    /// List of data classes for each FSM that has a persistent int/bool or geo rock attached to it.
    /// </summary>
    private readonly List<PersistentFsmData> _persistentFsmData;

    /// <summary>
    /// Dictionary of hash codes for list variables in the PlayerData for comparing changes against.
    /// </summary>
    private readonly Dictionary<string?, int> _listHashes;

    /// <summary>
    /// List of FieldInfo for fields in PlayerData that are simple values that should be synced. Used for looping
    /// over to check for changes and network those changes.
    /// </summary>
    private readonly List<FieldInfo> _playerDataSimpleSyncFields;

    /// <summary>
    /// Dictionary of variable names mapped to FieldInfo for fields in PlayerData that are compound values that should
    /// be synced. Used for resetting the instance of PlayerData for last values and checking for updates to compound
    /// values.
    /// </summary>
    private readonly List<FieldInfo> _playerDataCompoundSyncFields;

    /// <summary>
    /// PlayerData instance that contains the last values of the currently used PlayerData for comparison checking.
    /// </summary>
    private PlayerData? _lastPlayerData;

    /// <summary>
    /// The unscaled time at which the PlayerData should next be checked for changes.
    /// </summary>
    private float _nextPlayerDataCheckTime;

    /// <summary>
    /// The save data of a player that is starting a new game, which is applied again once the game has started.
    /// </summary>
    private CurrentSave? _newGameSave;

    /// <summary>
    /// List of HashSet variables in PlayerData.
    /// </summary>
    private static readonly List<string> HashSetVariables = [
        "scenesEncounteredBench",
        "scenesEncounteredCocoon",
        "scenesMapped",
        "scenesVisited"
    ];

    /// <summary>
    /// Whether the player is hosting the server, which means that player specific save data is not networked
    /// to the server.
    /// </summary>
    public bool IsHostingServer { get; set; }

    public SaveManager(NetClient netClient, EntityManager entityManager) {
        _netClient = netClient;
        _entityManager = entityManager;
        _saveChanges = new SaveChanges();

        _persistentFsmData = [];
        _listHashes = new Dictionary<string?, int>();
        _playerDataSimpleSyncFields = [];
        _playerDataCompoundSyncFields = [];
    }

    /// <summary>
    /// Initializes the save manager by loading the save data json.
    /// </summary>
    public void Initialize() {
        _netClient.ConnectEvent += OnConnect;

        foreach (var field in typeof(PlayerData).GetFields()) {
            var fieldName = field.Name;

            if (!SaveDataMapping.PlayerDataVarProperties.TryGetValue(fieldName, out var varProps)
                || !varProps.Sync
               ) {
                continue;
            }

            var compoundField = SaveDataMapping.StringListVariables.Contains(fieldName) ||
                                SaveDataMapping.VectorListVariables.Contains(fieldName) ||
                                SaveDataMapping.IntListVariables.Contains(fieldName) ||
                                HashSetVariables.Contains(fieldName);

            if (compoundField) {
                _playerDataCompoundSyncFields.Add(field);
            } else {
                _playerDataSimpleSyncFields.Add(field);
            }
        }
    }

    /// <summary>
    /// Register the relevant hooks for save-related operations.
    /// </summary>
    public void RegisterHooks() {
        SceneManager.activeSceneChanged += OnSceneChanged;

        MonoBehaviourUtil.Instance.OnUpdateEvent += OnUpdatePlayerDataChecks;
        MonoBehaviourUtil.Instance.OnUpdateEvent += OnUpdatePersistents;
    }

    /// <summary>
    /// Deregister the relevant hooks for save-related operations.
    /// </summary>
    public void DeregisterHooks() {
        SceneManager.activeSceneChanged -= OnSceneChanged;

        MonoBehaviourUtil.Instance.OnUpdateEvent -= OnUpdatePlayerDataChecks;
        MonoBehaviourUtil.Instance.OnUpdateEvent -= OnUpdatePersistents;
    }

    /// <summary>
    /// Callback method for when the player connects to a server, so we can reset the player data.
    /// </summary>
    /// <param name="serverInfo">The server info received from the server.</param>
    private void OnConnect(ServerInfo serverInfo) {
        if (serverInfo.FullSynchronisation) {
            ResetLastPlayerData();
        }
    }

    /// <summary>
    /// Resets the PlayerData instance that stores the last values of all synchronised fields.
    /// </summary>
    private void ResetLastPlayerData() {
        var pd = PlayerData.instance;

        _lastPlayerData = new PlayerData();

        foreach (var field in _playerDataSimpleSyncFields) {
            var value = field.GetValue(pd);
            field.SetValue(_lastPlayerData, value);
        }

        foreach (var field in _playerDataCompoundSyncFields) {
            var value = field.GetValue(pd);
            // Unset compound fields (e.g. on a fresh PlayerData) stay unset
            field.SetValue(_lastPlayerData, value == null ? null : GetCompoundCopy(value));
        }
    }

    /// <summary>
    /// Called every unity update. Checks the PlayerData for changes at a fixed interval, because reading the synced
    /// fields boxes every value and timers in the PlayerData would otherwise send a save update every frame.
    /// </summary>
    private void OnUpdatePlayerDataChecks() {
        if (Time.unscaledTime < _nextPlayerDataCheckTime) {
            return;
        }

        _nextPlayerDataCheckTime = Time.unscaledTime + PlayerDataCheckInterval;

        OnUpdatePlayerData();
        OnUpdateCompounds();
    }

    /// <summary>
    /// Check for changes in the PlayerData instance.
    /// </summary>
    private void OnUpdatePlayerData() {
        var pd = PlayerData.instance;
        if (_lastPlayerData == null) {
            return;
        }

        var gm = global::GameManager.instance;
        if (!gm) {
            return;
        }

        if (gm.GameState == GameState.MAIN_MENU) {
            return;
        }

        foreach (var field in _playerDataSimpleSyncFields) {
            var currentValue = field.GetValue(pd);
            var lastValue = field.GetValue(_lastPlayerData);

            if (Equals(currentValue, lastValue)) {
                continue;
            }

            Logger.Debug($"PlayerData value changed from: {lastValue} to {currentValue}");

            field.SetValue(_lastPlayerData, currentValue);

            if (field.FieldType == typeof(int)) {
                CheckSendSaveUpdate(
                    field.Name,
                    () => EncodeSaveDataValue(field.Name, currentValue),
                    () => {
                        var delta = (int) currentValue - (int) lastValue;
                        return EncodeSaveDataValue(field.Name, delta);
                    }
                );
            } else {
                CheckSendSaveUpdate(field.Name, () => EncodeSaveDataValue(field.Name, currentValue));
            }
        }
    }

    /// <summary>
    /// Callback method for when the scene changes. Used to check for GeoRock, PersistentInt and PersistentBool
    /// instances in the scene.
    /// </summary>
    /// <param name="oldScene">The old scene.</param>
    /// <param name="newScene">The new scene.</param>
    private void OnSceneChanged(Scene oldScene, Scene newScene) {
        _persistentFsmData.Clear();

        foreach (var geoRock in Object.FindObjectsByType<GeoRock>(FindObjectsSortMode.None)) {
            var geoRockObject = geoRock.gameObject;

            if (geoRockObject.scene != newScene) {
                continue;
            }

            var persistentItemData = new PersistentItemKey {
                Id = geoRockObject.name,
                SceneName = global::GameManager.GetBaseSceneName(geoRockObject.scene.name)
            };

            Logger.Info($"Found Geo Rock in scene: {persistentItemData}");

            var fsm = geoRock.GetComponent<PlayMakerFSM>();
            if (!fsm) {
                Logger.Info("  Could not find FSM belonging to Geo Rock object, skipping");
                continue;
            }

            var fsmInt = fsm.FsmVariables.GetFsmInt("Hits");

            var persistentFsmData = new PersistentFsmData {
                PersistentItemKey = persistentItemData,
                GetCurrentInt = () => fsmInt.Value,
                SetCurrentInt = value => fsmInt.Value = value,
                LastIntValue = fsmInt.Value
            };

            _persistentFsmData.Add(persistentFsmData);
        }

        foreach (var persistentBoolItem in Object.FindObjectsByType<PersistentBoolItem>(FindObjectsSortMode.None)) {
            var itemObject = persistentBoolItem.gameObject;

            if (itemObject.scene != newScene) {
                continue;
            }

            var persistentItemData = new PersistentItemKey {
                Id = itemObject.name,
                SceneName = global::GameManager.GetBaseSceneName(itemObject.scene.name)
            };

            Logger.Info($"Found persistent bool in scene: {persistentItemData}");

            Func<bool>? getCurrentBoolFunc = null;
            Action<bool>? setCurrentBoolAction = null;

            var fsm = FSMUtility.FindFSMWithPersistentBool(itemObject.GetComponents<PlayMakerFSM>());
            if (fsm) {
                var fsmBool = fsm.FsmVariables.GetFsmBool("Activated");
                getCurrentBoolFunc = () => fsmBool.Value;
                setCurrentBoolAction = value => fsmBool.Value = value;
            }

            var vinePlatform = itemObject.GetComponent<VinePlatform>();
            if (vinePlatform) {
                getCurrentBoolFunc = () => vinePlatform.activated;
                setCurrentBoolAction = value => vinePlatform.activated = value;
            }

            var breakable = itemObject.GetComponent<Breakable>();
            if (breakable) {
                getCurrentBoolFunc = () => breakable.isBroken;
                setCurrentBoolAction = value => breakable.isBroken = value;
            }

            var dreamPlant = itemObject.GetComponent<DreamPlant>();
            if (dreamPlant) {
                getCurrentBoolFunc = () => dreamPlant.completed;
                setCurrentBoolAction = value => dreamPlant.completed = value;
            }

            var dreamPlantOrb = itemObject.GetComponent<DreamPlantOrb>();
            if (dreamPlantOrb) {
                getCurrentBoolFunc = () => dreamPlantOrb.pickedUp;
                setCurrentBoolAction = value => dreamPlantOrb.pickedUp = value;
            }

            // Items without any of the above, such as collectable pickups,
            // handle their state through the item itself
            getCurrentBoolFunc ??= persistentBoolItem.GetCurrentValue;
            setCurrentBoolAction ??= persistentBoolItem.SetValueOverride;

            var persistentFsmData = new PersistentFsmData {
                PersistentItemKey = persistentItemData,
                GetCurrentBool = getCurrentBoolFunc,
                SetCurrentBool = setCurrentBoolAction,
                LastBoolValue = getCurrentBoolFunc.Invoke()
            };

            _persistentFsmData.Add(persistentFsmData);
        }

        foreach (var persistentIntItem in Object.FindObjectsByType<PersistentIntItem>(FindObjectsSortMode.None)) {
            var itemObject = persistentIntItem.gameObject;

            if (itemObject.scene != newScene) {
                continue;
            }

            var persistentItemData = new PersistentItemKey {
                Id = itemObject.name,
                SceneName = global::GameManager.GetBaseSceneName(itemObject.scene.name)
            };

            Logger.Info($"Found persistent int in scene: {persistentItemData}");

            var fsm = FSMUtility.FindFSMWithPersistentBool(itemObject.GetComponents<PlayMakerFSM>());
            if (!fsm) {
                Logger.Info("  Could not find FSM belonging to persistent int object, skipping");
                continue;
            }

            var fsmInt = fsm.FsmVariables.GetFsmInt("Value");

            var persistentFsmData = new PersistentFsmData {
                PersistentItemKey = persistentItemData,
                GetCurrentInt = () => fsmInt.Value,
                SetCurrentInt = value => fsmInt.Value = value,
                LastIntValue = fsmInt.Value
            };

            _persistentFsmData.Add(persistentFsmData);
        }

        if (_newGameSave != null && newScene.name != "Menu_Title") {
            ApplySaveData(_newGameSave);
            _newGameSave = null;
        }
    }

    /// <summary>
    /// Checks if a save update should be sent and send it using the encode function to encode the value of the
    /// changed variable.
    /// </summary>
    /// <param name="name">The name of the variable that was changed.</param>
    /// <param name="encodeFunc">Function to encode the value of the variable to a byte array.</param>
    /// <param name="deltaEncodeFunc">Function to encode the delta value of the variable of the type is applicable.
    /// </param>
    private void CheckSendSaveUpdate(string name, Func<byte[]> encodeFunc, Func<byte[]>? deltaEncodeFunc = null) {
        // If we are not connected or the 'permadeathMode' is 2, meaning we have broken/lost Steel Soul
        if (!_netClient.IsConnected || PlayerData.instance.GetInt("permadeathMode") == 2) {
            return;
        }

        if (!SaveDataMapping.PlayerDataVarProperties.TryGetValue(name, out var varProps)) {
            Logger.Info($"Not in save data values, not sending save update ({name})");
            return;
        }

        if (!varProps.Sync) {
            Logger.Info($"Value should not sync, not sending save update ({name})");
            return;
        }

        // If we should do the scene host check and the player is not scene host, skip sending
        if (!varProps.IgnoreSceneHost && !_entityManager.IsSceneHost) {
            Logger.Info($"Not scene host, but required, not sending save update ({name})");
            return;
        }

        if (!SaveDataMapping.PlayerDataIndices.TryGetValue(name, out var index)) {
            Logger.Info($"Cannot find save data index, not sending save update ({name})");
            return;
        }

        Func<byte[]> toUseEncodeFunc;
        if (varProps.Additive && deltaEncodeFunc != null) {
            toUseEncodeFunc = deltaEncodeFunc;

            Logger.Debug($"Sending \"{name}\" as save update (additive)");
        } else {
            toUseEncodeFunc = encodeFunc;

            Logger.Debug($"Sending \"{name}\" as save update");
        }

        _netClient.UpdateManager.SetSaveUpdate(
            index,
            toUseEncodeFunc.Invoke()
        );
    }

    /// <summary>
    /// Called every unity update. Used to check for changes in the GeoRock/PersistentInt/PersistentBool FSMs.
    /// </summary>
    private void OnUpdatePersistents() {
        using var enumerator = _persistentFsmData.GetEnumerator();

        while (enumerator.MoveNext()) {
            var persistentFsmData = enumerator.Current;
            if (persistentFsmData == null) {
                continue;
            }

            if (persistentFsmData.IsInt) {
                var value = persistentFsmData.GetCurrentInt.Invoke();
                if (value == persistentFsmData.LastIntValue) {
                    continue;
                }

                persistentFsmData.LastIntValue = value;

                var itemData = persistentFsmData.PersistentItemKey;

                Logger.Info($"Value for {itemData} changed to: {value}");

                if (!_netClient.IsConnected) {
                    continue;
                }

                if (SaveDataMapping.GeoRockBools.TryGetValue(itemData, out var shouldSync) && shouldSync) {
                    if (!_entityManager.IsSceneHost) {
                        Logger.Info(
                            $"Not scene host, not sending geo rock save update ({itemData.Id}, {itemData.SceneName})"
                        );
                        continue;
                    }

                    if (!SaveDataMapping.GeoRockIndices.TryGetValue(itemData, out var index)) {
                        Logger.Info(
                            $"Cannot find geo rock save data index, not sending save update ({itemData.Id}, {itemData.SceneName})"
                        );
                        continue;
                    }

                    Logger.Info($"Sending geo rock ({itemData.Id}, {itemData.SceneName}) as save update");

                    _netClient.UpdateManager.SetSaveUpdate(
                        index,
                        [(byte) value]
                    );
                } else if (
                    SaveDataMapping.PersistentIntVarProperties.TryGetValue(itemData, out var varProps) &&
                    varProps.Sync
                ) {
                    // If we should do the scene host check and the player is not scene host, skip sending
                    if (!varProps.IgnoreSceneHost && !_entityManager.IsSceneHost) {
                        Logger.Info(
                            $"Not scene host, not sending persistent int save update ({itemData.Id}, {itemData.SceneName})"
                        );
                        continue;
                    }

                    if (!SaveDataMapping.PersistentIntIndices.TryGetValue(itemData, out var index)) {
                        Logger.Info(
                            $"Cannot find persistent int save data index, not sending save update ({itemData.Id}, {itemData.SceneName})"
                        );
                        continue;
                    }

                    Logger.Info($"Sending persistent int ({itemData.Id}, {itemData.SceneName}) as save update");

                    _netClient.UpdateManager.SetSaveUpdate(
                        index,
                        [(byte) value]
                    );
                } else {
                    Logger.Info("Cannot find persistent int/geo rock data bool, not sending save update");
                }
            } else {
                var value = persistentFsmData.GetCurrentBool.Invoke();
                if (value == persistentFsmData.LastBoolValue) {
                    continue;
                }

                persistentFsmData.LastBoolValue = value;

                var itemData = persistentFsmData.PersistentItemKey;

                Logger.Info($"Value for {itemData} changed to: {value}");

                if (!_netClient.IsConnected) {
                    continue;
                }

                if (!SaveDataMapping.PersistentBoolVarProperties.TryGetValue(itemData, out var varProps) ||
                    !varProps.Sync) {
                    Logger.Info(
                        $"Not in persistent bool save data values or false in sync props, not sending save update ({itemData.Id}, {itemData.SceneName})"
                    );
                    continue;
                }

                // If we should do the scene host check and the player is not scene host, skip sending
                if (!varProps.IgnoreSceneHost && !_entityManager.IsSceneHost) {
                    Logger.Info(
                        $"Not scene host, not sending persistent bool save update ({itemData.Id}, {itemData.SceneName})"
                    );
                    continue;
                }

                if (!SaveDataMapping.PersistentBoolIndices.TryGetValue(itemData, out var index)) {
                    Logger.Info(
                        $"Cannot find persistent bool save data index, not sending save update ({itemData.Id}, {itemData.SceneName})"
                    );
                    continue;
                }

                Logger.Info($"Sending persistent bool ({itemData.Id}, {itemData.SceneName}) as save update");

                _netClient.UpdateManager.SetSaveUpdate(
                    index,
                    BitConverter.GetBytes(value)
                );
            }
        }
    }

    /// <summary>
    /// Check for changes in non-primitive variables in the PlayerData.
    /// </summary>
    private void OnUpdateCompounds() {
        CheckUpdates<List<string>>(
            SaveDataMapping.StringListVariables,
            GetListHashCode,
            (currentValue, lastValue) => {
                var currentList = currentValue as List<string> ?? [];
                var lastList = lastValue as List<string> ?? [];

                // TODO: also allow for negative updates, where something is deleted from the list
                // this also holds for the other two lambdas below
                var deltaList = currentList.Except(lastList).ToList();

                Logger.Debug(
                    $"String list var updated, currentList: {string.Join(", ", currentList)}, lastList: {string.Join(", ", lastList)}, deltaList: {string.Join(", ", deltaList)}"
                );

                return EncodeSaveDataValue(null, deltaList);
            }
        );

        CheckUpdates<List<Vector3>>(
            SaveDataMapping.VectorListVariables,
            GetListHashCode,
            (currentValue, lastValue) => {
                var currentList = currentValue as List<Vector3> ?? [];
                var lastList = lastValue as List<Vector3> ?? [];

                var deltaList = currentList.Except(lastList).ToList();

                Logger.Debug(
                    $"Vector3 list var updated, currentList: {string.Join(", ", currentList)}, lastList: {string.Join(", ", lastList)}, deltaList: {string.Join(", ", deltaList)}"
                );

                return EncodeSaveDataValue(null, deltaList);
            }
        );

        CheckUpdates<List<int>>(
            SaveDataMapping.IntListVariables,
            GetListHashCode,
            (currentValue, lastValue) => {
                var currentList = currentValue as List<int> ?? [];
                var lastList = lastValue as List<int> ?? [];

                var deltaList = currentList.Except(lastList).ToList();

                Logger.Debug(
                    $"Integer list var updated, currentList: {string.Join(", ", currentList)}, lastList: {string.Join(", ", lastList)}, deltaList: {string.Join(", ", deltaList)}"
                );

                return EncodeSaveDataValue(null, deltaList);
            }
        );

        CheckUpdates<HashSet<string>>(
            HashSetVariables,
            GetListHashCode,
            (currentValue, lastValue) => {
                var currentSet = currentValue as HashSet<string> ?? [];
                var lastSet = lastValue as HashSet<string> ?? [];

                var deltaList = currentSet.Except(lastSet).ToList();

                Logger.Debug(
                    $"HashSet string var updated, currentSet: {string.Join(", ", currentSet)}, lastSet: {string.Join(", ", lastSet)}, deltaList: {string.Join(", ", deltaList)}"
                );

                return EncodeSaveDataValue(null, deltaList);
            }
        );
    }

    /// <summary>
    /// Check the given PlayerData variables for changes by comparing their hashes and send a save update for each
    /// variable that changed.
    /// </summary>
    /// <param name="variableNames">The names of the variables in the PlayerData.</param>
    /// <param name="hashFunc">Function to calculate the hash of the value of a variable.</param>
    /// <param name="deltaEncodeFunc">Function to encode the difference between the current and the last value of a
    /// variable, which is used for additive variables.</param>
    /// <typeparam name="TVar">The type of the variables.</typeparam>
    private void CheckUpdates<TVar>(
        List<string?> variableNames,
        Func<TVar, int> hashFunc,
        Func<object, object, byte[]> deltaEncodeFunc
    ) {
        foreach (var varName in variableNames) {
            // Get current value from player data based on the variable name
            var currentValue = (TVar) PlayerDataFields[varName].GetValue(PlayerData.instance);
            var currentHash = hashFunc.Invoke(currentValue);

            // Check if the dictionary contains the last hash to check against
            if (!_listHashes.TryGetValue(varName, out var lastHash)) {
                // If not, we put the hash in the dictionary and continue
                _listHashes[varName] = currentHash;
                continue;
            }

            if (currentHash == lastHash) {
                continue;
            }

            Logger.Debug($"Compound variable ({varName}) changed value");

            // Since the value changed, we update it in the dictionary
            _listHashes[varName] = currentHash;

            var lastValue = _lastPlayerData.GetVariable<TVar>(varName);

            CheckSendSaveUpdate(
                varName,
                () => EncodeSaveDataValue(varName, currentValue),
                () => deltaEncodeFunc.Invoke(currentValue, lastValue)
            );

            // Also update the current value in the PlayerData instance for last values
            // We copy the value, because otherwise it will be updated whenever the list is updated
            _lastPlayerData.SetVariable(
                varName,
                currentValue == null ? currentValue : (TVar) GetCompoundCopy(currentValue)
            );
        }
    }

    /// <summary>
    /// Callback method for when a save update is received.
    /// </summary>
    /// <param name="saveUpdate">The save update that was received.</param>
    public void UpdateSaveWithData(SaveUpdate saveUpdate) {
        Logger.Info($"Received save update for index: {saveUpdate.SaveDataIndex}");

        var index = saveUpdate.SaveDataIndex;
        var value = saveUpdate.Value;

        UpdateSaveWithData(index, value);
    }

    /// <summary>
    /// Set the save data from the given CurrentSave by overriding all values.
    /// </summary>
    /// <param name="currentSave">The save data to set.</param>
    public void SetSaveWithData(CurrentSave currentSave) {
        if (IsHostingServer) {
            Logger.Info("Received current save, but player is hosting, not updating");
            return;
        }

        Logger.Info("Received current save, updating...");

        ApplySaveData(currentSave);

        // Starting a new game can reset the save data that we have just applied
        _newGameSave = currentSave.NewForPlayer ? currentSave : null;
    }

    /// <summary>
    /// Update the local save with all values in the given save data.
    /// </summary>
    /// <param name="currentSave">The save data to apply.</param>
    private void ApplySaveData(CurrentSave currentSave) {
        foreach (var (index, value) in currentSave.SaveData) {
            // A single invalid value should not stop the player from entering the game
            try {
                UpdateSaveWithData(index, value);
            } catch (Exception e) {
                Logger.Warn($"Could not apply save data with index {index}, skipping:\n{e}");
            }
        }
    }

    /// <summary>
    /// Update the local save with the given data (index and encoded value).
    /// </summary>
    /// <param name="index">The index of the save data.</param>
    /// <param name="encodedValue">A byte array containing the encoded value of the save data.</param>
    /// <exception cref="NotImplementedException">Thrown when the type belonging to the save data cannot be decoded
    /// due to a missing implementation.</exception>
    private void UpdateSaveWithData(ushort index, byte[] encodedValue) {
        var pd = PlayerData.instance;
        var sceneData = SceneData.instance;

        if (SaveDataMapping.PlayerDataIndices.TryGetValue(index, out var name)) {
            if (CheckPlayerSpecificHosting(SaveDataMapping.PlayerDataVarProperties, name)) {
                return;
            }

            Logger.Info($"Received save update ({index}, {name})");

            var decodedObject = DecodeSaveDataValue(name, encodedValue);

            switch (decodedObject) {
                case bool decodedBool:
                    _lastPlayerData?.SetBool(name, decodedBool);
                    pd.SetBool(name, decodedBool);
                    break;
                case float decodedFloat:
                    _lastPlayerData?.SetFloat(name, decodedFloat);
                    pd.SetFloat(name, decodedFloat);
                    break;
                case int decodedInt:
                    _lastPlayerData?.SetInt(name, decodedInt);
                    pd.SetInt(name, decodedInt);
                    break;
                case long decodedLong:
                    _lastPlayerData?.SetVariable(name, decodedLong);
                    pd.SetVariable(name, decodedLong);
                    break;
                case ulong decodedULong:
                    _lastPlayerData?.SetVariable(name, decodedULong);
                    pd.SetVariable(name, decodedULong);
                    break;
                case uint decodedUInt:
                    _lastPlayerData?.SetVariable(name, decodedUInt);
                    pd.SetVariable(name, decodedUInt);
                    break;
                case short decodedShort:
                    _lastPlayerData?.SetVariable(name, decodedShort);
                    pd.SetVariable(name, decodedShort);
                    break;
                case ushort decodedUShort:
                    _lastPlayerData?.SetVariable(name, decodedUShort);
                    pd.SetVariable(name, decodedUShort);
                    break;
                case double decodedDouble:
                    _lastPlayerData?.SetVariable(name, decodedDouble);
                    pd.SetVariable(name, decodedDouble);
                    break;
                case string decodedString:
                    _lastPlayerData?.SetString(name, decodedString);
                    pd.SetString(name, decodedString);
                    break;
                case Vector2 decodedVec2:
                    _lastPlayerData?.SetVariable(name, decodedVec2);
                    pd.SetVariable(name, decodedVec2);
                    break;
                case Vector3 decodedVec3:
                    _lastPlayerData?.SetVector3(name, decodedVec3);
                    pd.SetVector3(name, decodedVec3);
                    break;
                case List<string> decodedStringList:
                    // First set the new string list hash, so we don't trigger an update and subsequently a feedback
                    // loop
                    _listHashes[name] = GetListHashCode(decodedStringList);
                    _lastPlayerData?.SetVariable(name, (List<string>) GetCompoundCopy(decodedStringList));
                    pd.SetVariable(name, decodedStringList);
                    break;
                case List<Vector3> decodedVec3List:
                    // First set the new string list hash, so we don't trigger an update and subsequently a feedback
                    // loop
                    _listHashes[name] = GetListHashCode(decodedVec3List);
                    _lastPlayerData?.SetVariable(name, (List<Vector3>) GetCompoundCopy(decodedVec3List));
                    pd.SetVariable(name, decodedVec3List);
                    break;
                case MapZone decodedMapZone:
                    _lastPlayerData?.SetVariable(name, decodedMapZone);
                    pd.SetVariable(name, decodedMapZone);
                    break;
                case List<int> decodedIntList:
                    // First set the new string list hash, so we don't trigger an update and subsequently a feedback
                    // loop
                    _listHashes[name] = GetListHashCode(decodedIntList);
                    _lastPlayerData?.SetVariable(name, (List<int>) GetCompoundCopy(decodedIntList));
                    pd.SetVariable(name, decodedIntList);
                    break;
                case byte[] decodedBytes: {
                    var copy = decodedBytes.ToArray();
                    _lastPlayerData?.SetVariable(name, copy.ToArray());
                    pd.SetVariable(name, copy);
                    break;
                }
                case HashSet<string> decodedHashSet: {
                    _listHashes[name] = GetListHashCode(decodedHashSet);
                    _lastPlayerData?.SetVariable(name, (HashSet<string>) GetCompoundCopy(decodedHashSet));
                    pd.SetVariable(name, decodedHashSet);
                    break;
                }
                default: {
                    if (decodedObject.GetType().IsEnum) {
                        _lastPlayerData?.SetVariable(name, decodedObject);
                        pd.SetVariable(name, decodedObject);
                    } else {
                        throw new ArgumentException($"Could not decode type: {decodedObject.GetType()}");
                    }

                    break;
                }
            }

            _saveChanges.ApplyPlayerDataSaveChange(name);
        }

        if (SaveDataMapping.GeoRockIndices.TryGetValue(index, out var itemData)) {
            var value = encodedValue[0];

            Logger.Info($"Received geo rock save update: {itemData.Id}, {itemData.SceneName}, {value}");

            foreach (var persistentFsmData in _persistentFsmData) {
                var existingItemData = persistentFsmData.PersistentItemKey;

                if (existingItemData.Id == itemData.Id && existingItemData.SceneName == itemData.SceneName) {
                    persistentFsmData.SetCurrentInt.Invoke(value);
                    persistentFsmData.LastIntValue = value;
                }
            }

            sceneData.SaveMyState(
                new GeoRockData {
                    id = itemData.Id,
                    sceneName = itemData.SceneName,
                    hitsLeft = value
                }
            );
        } else if (SaveDataMapping.PersistentBoolIndices.TryGetValue(index, out itemData)) {
            if (CheckPlayerSpecificHosting(SaveDataMapping.PersistentBoolVarProperties, itemData)) {
                return;
            }

            var value = encodedValue[0] == 1;

            Logger.Info($"Received persistent bool save update: {itemData.Id}, {itemData.SceneName}, {value}");

            foreach (var persistentFsmData in _persistentFsmData) {
                var existingItemData = persistentFsmData.PersistentItemKey;

                if (existingItemData.Id == itemData.Id && existingItemData.SceneName == itemData.SceneName) {
                    Logger.Debug($"Setting last bool value for {existingItemData} to {value}");
                    persistentFsmData.SetCurrentBool.Invoke(value);
                    persistentFsmData.LastBoolValue = value;
                }
            }

            sceneData.persistentBools.TryGetValue(itemData.SceneName, itemData.Id, out var existingItem);
            sceneData.persistentBools.SetValue(CreateSceneItem(existingItem, itemData, value));

            _saveChanges.ApplyPersistentValueSaveChange(itemData);
        } else if (SaveDataMapping.PersistentIntIndices.TryGetValue(index, out itemData)) {
            if (CheckPlayerSpecificHosting(SaveDataMapping.PersistentIntVarProperties, itemData)) {
                return;
            }

            var value = (int) encodedValue[0];
            // Add a special case for the -1 value that some persistent ints might have
            // 255 is never used in the byte space, so we use it for compact networking
            if (value == 255) {
                value = -1;
            }

            Logger.Info($"Received persistent int save update: {itemData.Id}, {itemData.SceneName}, {value}");

            foreach (var persistentFsmData in _persistentFsmData) {
                var existingItemData = persistentFsmData.PersistentItemKey;

                if (existingItemData.Id == itemData.Id && existingItemData.SceneName == itemData.SceneName) {
                    persistentFsmData.SetCurrentInt.Invoke(value);
                    persistentFsmData.LastIntValue = value;
                }
            }

            sceneData.persistentInts.TryGetValue(itemData.SceneName, itemData.Id, out var existingItem);
            sceneData.persistentInts.SetValue(CreateSceneItem(existingItem, itemData, value));

            _saveChanges.ApplyPersistentValueSaveChange(itemData);
        }
    }

    /// <summary>
    /// Create the scene data item for a received value, keeping how the game handles the item if it is already known.
    /// </summary>
    /// <param name="existingItem">The item currently in the scene data, or null if there is none.</param>
    /// <param name="itemKey">The key of the item.</param>
    /// <param name="value">The received value.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>The new scene data item.</returns>
    private static PersistentItemData<T> CreateSceneItem<T>(
        PersistentItemData<T>? existingItem,
        PersistentItemKey itemKey,
        T value
    ) {
        return new PersistentItemData<T> {
            ID = itemKey.Id,
            SceneName = itemKey.SceneName,
            Value = value,
            IsSemiPersistent = existingItem?.IsSemiPersistent ?? false,
            Mutator = existingItem?.Mutator ?? default
        };
    }

    /// <summary>
    /// Check whether the player is hosting and the received save data is player specific and should thus be ignored.
    /// </summary>
    /// <param name="dict">The dictionary containing the variable properties of the save data.</param>
    /// <param name="value">The key of the save data in the dictionary.</param>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <returns>True if the data should be ignored, false otherwise.</returns>
    private bool CheckPlayerSpecificHosting<TKey>(Dictionary<TKey, SaveDataMapping.VarProperties> dict, TKey value) {
        if (!IsHostingServer) {
            return false;
        }

        if (!dict.TryGetValue(value, out var varProps)) {
            return true;
        }

        if (varProps.SyncType != SaveDataMapping.SyncType.Player) {
            return false;
        }

        Logger.Info($"Received player specific save update ({value}), but player is hosting");
        return true;
    }

    /// <summary>
    /// Encode a save data value by first recasting HK/Unity internal types to SSMP types and then using the EncodeUtil.
    /// </summary>
    /// <param name="name">The name of the save data variable.</param>
    /// <param name="value">The object to encode, which should be part of save data.</param>
    /// <returns>A byte array containing the encoded data.</returns>
    private static byte[] EncodeSaveDataValue(string? name, object? value) {
        // First cast HK or Unity internal types to SSMP types, this is to make sure we can use our internal
        // EncodeUtil to encode all types. This util is also used on the server side, where (in the case of the
        // standalone server) we have no reference of HK or Unity internal types
        var casted = value switch {
            Vector2 v => (Math.Vector2) v,
            Vector3 v => (Math.Vector3) v,
            MapZone m => (Serialization.MapZone) m,
            List<Vector3> l => l.Select(v => (Math.Vector3) v).ToList(),
            _ => value
        };

        return EncodeUtil.EncodeSaveDataValue(casted, name);
    }

    /// <summary>
    /// Decode a save data value by first using the EncodeUtil and then recasting SSMP types to SS/Unity internal types. 
    /// </summary>
    /// <param name="name">The name of the save data variable.</param>
    /// <param name="encodedValue">A byte array containing the encoded data.</param>
    /// <returns>The decoded object.</returns>
    private static object? DecodeSaveDataValue(string? name, byte[] encodedValue) {
        var val = EncodeUtil.DecodeSaveDataValue(name, encodedValue);

        // Now we cast SSMP types to SS or Unity internal types, this is to make sure we can use our internal
        // EncodeUtil to decode all types. This util is also used on the server side, where (in the case of the
        // standalone server) we have no reference of SS or Unity internal types
        return val switch {
            Math.Vector2 v => (Vector2) v,
            Math.Vector3 v => (Vector3) v,
            Serialization.MapZone m => (MapZone) m,
            List<Math.Vector3> l => l.Select(v => (Vector3) v).ToList(),
            int i when name != null && PlayerDataFields.TryGetValue(name, out var enumField) &&
                       enumField.FieldType.IsEnum => Enum.ToObject(enumField.FieldType, i),
            _ => val
        };
    }

    /// <summary>
    /// Get the current save data as a dictionary with mapped indices and encoded values. This returns the global save
    /// data if the <paramref name="server"/> boolean is set. Otherwise, it returns the player save data.
    /// </summary>
    /// <returns>A dictionary with mapped indices and byte-encoded values.</returns>
    public static Dictionary<ushort, byte[]> GetCurrentSaveData(bool server) {
        var pd = PlayerData.instance;
        var sd = SceneData.instance;

        var saveData = new Dictionary<ushort, byte[]>();

        AddToSaveData(
            saveData,
            typeof(PlayerData).GetFields(),
            fieldInfo => fieldInfo.Name,
            ShouldSync(SaveDataMapping.PlayerDataVarProperties, server),
            SaveDataMapping.PlayerDataIndices,
            fieldInfo => fieldInfo.GetValue(pd)
        );

        AddSceneData(
            saveData,
            sd.geoRocks.scenes,
            // All geo rocks are server data, so they are only added when we are getting server data
            key => server && SaveDataMapping.GeoRockBools.TryGetValue(key, out var sync) && sync,
            SaveDataMapping.GeoRockIndices
        );

        AddSceneData(
            saveData,
            sd.persistentBools.scenes,
            ShouldSync(SaveDataMapping.PersistentBoolVarProperties, server),
            SaveDataMapping.PersistentBoolIndices
        );

        AddSceneData(
            saveData,
            sd.persistentInts.scenes,
            ShouldSync(SaveDataMapping.PersistentIntVarProperties, server),
            SaveDataMapping.PersistentIntIndices
        );

        return saveData;
    }

    /// <summary>
    /// Get a function that checks whether a key should be synced, which is the case if its variable properties
    /// allow it and its sync type matches the type of data we are getting.
    /// </summary>
    /// <param name="varProperties">The dictionary containing the variable properties of the keys.</param>
    /// <param name="server">Whether we are getting the global save data instead of the player save data.</param>
    /// <typeparam name="TLookup">The type of the keys.</typeparam>
    /// <returns>The function that checks a key.</returns>
    private static Func<TLookup, bool> ShouldSync<TLookup>(
        Dictionary<TLookup, SaveDataMapping.VarProperties> varProperties,
        bool server
    ) {
        return key => varProperties.TryGetValue(key, out var varProps) &&
                      varProps.Sync &&
                      (varProps.SyncType == SaveDataMapping.SyncType.Server) == server;
    }

    /// <summary>
    /// Add the values of the given collection that should be synced to the given save data.
    /// </summary>
    /// <param name="saveData">The dictionary of indices and byte-encoded values to add to.</param>
    /// <param name="enumerable">The collection containing the values.</param>
    /// <param name="keyFunc">Function to get the key of an item in the collection for the mappings.</param>
    /// <param name="shouldSync">Function that checks whether the value of a key should be synced.</param>
    /// <param name="indexMapping">The mapping from a key to its save data index.</param>
    /// <param name="valueFunc">Function to get the value to encode from an item in the collection.</param>
    /// <typeparam name="TCollection">The type of the items in the collection.</typeparam>
    /// <typeparam name="TLookup">The type of the keys in the mappings.</typeparam>
    private static void AddToSaveData<TCollection, TLookup>(
        Dictionary<ushort, byte[]> saveData,
        IEnumerable<TCollection> enumerable,
        Func<TCollection, TLookup> keyFunc,
        Func<TLookup, bool> shouldSync,
        BiLookup<TLookup, ushort> indexMapping,
        Func<TCollection, object> valueFunc
    ) {
        foreach (var collectionValue in enumerable) {
            var key = keyFunc.Invoke(collectionValue);

            if (!shouldSync.Invoke(key) || !indexMapping.TryGetValue(key, out var index)) {
                continue;
            }

            var value = valueFunc.Invoke(collectionValue);

            saveData.Add(index, EncodeSaveDataValue(key as string, value));
        }
    }

    /// <summary>
    /// Add the persistent items of all scenes in a <see cref="SceneData"/> collection to the given save data.
    /// Semi-persistent items are left out, because they reset by themselves and the receiver has no flag for them.
    /// </summary>
    /// <param name="saveData">The dictionary of indices and byte-encoded values to add to.</param>
    /// <param name="scenes">The items of the collection, mapped by scene name and then by item ID.</param>
    /// <param name="shouldSync">Function that checks whether the value of an item should be synced.</param>
    /// <param name="indexMapping">The mapping from an item to its save data index.</param>
    /// <typeparam name="T">The type of the value of the items.</typeparam>
    private static void AddSceneData<T>(
        Dictionary<ushort, byte[]> saveData,
        Dictionary<string, Dictionary<string, PersistentItemData<T>>> scenes,
        Func<PersistentItemKey, bool> shouldSync,
        BiLookup<PersistentItemKey, ushort> indexMapping
    ) where T : struct {
        AddToSaveData(
            saveData,
            scenes.Values.SelectMany(scene => scene.Values).Where(item => !item.IsSemiPersistent),
            item => new PersistentItemKey { Id = item.ID, SceneName = item.SceneName },
            shouldSync,
            indexMapping,
            item => item.Value
        );
    }

    /// <summary>
    /// Get the hash code of the combined values in a collection.
    /// </summary>
    /// <param name="values">The collection to calculate the hash code for.</param>
    /// <returns>0 if the collection is empty or null, otherwise a hash code that does not depend on the order of the
    /// values.</returns>
    private static int GetListHashCode<T>(IEnumerable<T>? values) {
        var hash = 0;
        if (values == null) {
            return hash;
        }

        foreach (var value in values) {
            hash ^= value?.GetHashCode() ?? 0;
        }

        return hash;
    }

    /// <summary>
    /// Get a copy of the given object for compound objects in the PlayerData, such as string lists, integer lists,
    /// vector lists and string hash sets.
    /// </summary>
    /// <param name="value">The object value to get a copy from.</param>
    /// <returns>The copy of the given value.</returns>
    /// <exception cref="ArgumentException">Thrown when a copy cannot be made, due to the given value being of a
    /// non-compound or non-PlayerData type.</exception>
    private static object GetCompoundCopy(object value) {
        return value switch {
            List<string> stringListValue => new List<string>(stringListValue),
            List<int> intListValue => new List<int>(intListValue),
            List<Vector3> vecListValue => new List<Vector3>(vecListValue),
            HashSet<string> hashSetStringValue => new HashSet<string>(hashSetStringValue),
            _ => throw new ArgumentException($"Cannot get copy of value with type: {value.GetType()}")
        };
    }
}

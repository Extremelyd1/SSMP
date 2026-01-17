using System;
using System.Collections.Generic;
using System.Linq;
using SSMP.Fsm;
using SSMP.Game.Client.Skin;
using SSMP.Game.Settings;
using SSMP.Hooks;
using SSMP.Internals;
using SSMP.Util;
using TMProOld;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Math_Vector2 = SSMP.Math.Vector2;
using Object = UnityEngine.Object;

namespace SSMP.Game.Client;

/// <summary>
/// Class that manages player objects, spawning and recycling thereof.
/// </summary>
internal class PlayerManager {
    /// <summary>
    /// The name of the game object for the player container prefab.
    /// </summary>
    private const string PlayerContainerPrefabName = "Player Container Prefab";

    /// <summary>
    /// The name of the game object for the player object prefab.
    /// </summary>
    private const string PlayerObjectPrefabName = "Player Prefab";

    /// <summary>
    /// The name (and prefix) of the game object for player containers.
    /// </summary>
    private const string PlayerContainerName = "Player Container";

    /// <summary>
    /// The name of the game object for the username of players.
    /// </summary>
    private const string UsernameObjectName = "Username";

    /// <summary>
    /// The initial size of the pool of player container objects to be pre-instantiated.
    /// </summary>
    private const ushort InitialPoolSize = 64;

    /// <summary>
    /// The current server settings.
    /// </summary>
    private readonly ServerSettings _serverSettings;

    /// <summary>
    /// The skin manager instance.
    /// </summary>
    private readonly SkinManager _skinManager;

    /// <summary>
    /// Reference to the client player data dictionary (<see cref="ClientManager._playerData"/>)
    /// from <see cref="ClientManager"/>.
    /// </summary>
    private readonly Dictionary<ushort, ClientPlayerData> _playerData;

    /// <summary>
    /// The team that our local player is on.
    /// </summary>
    public Team LocalPlayerTeam { get; private set; } = Team.None;

    /// <summary>
    /// The player container prefab GameObject.
    /// </summary>
    private GameObject? _playerContainerPrefab;

    /// <summary>
    /// A queue of pre-instantiated players that will be used when spawning a player.
    /// </summary>
    private readonly Queue<GameObject> _inactivePlayers;

    /// <summary>
    /// The collection of active players spawned from and not in the player pool.
    /// </summary>
    private readonly Dictionary<ushort, GameObject> _activePlayers;

    public PlayerManager(
        ServerSettings serverSettings,
        Dictionary<ushort, ClientPlayerData> playerData
    ) {
        _serverSettings = serverSettings;

        _skinManager = new SkinManager();

        _playerData = playerData;

        _inactivePlayers = new Queue<GameObject>();
        _activePlayers = new Dictionary<ushort, GameObject>();
    }

    /// <summary>
    /// Initialize the player manager by initializing the skin manager.
    /// </summary>
    public void Initialize() {
        _skinManager.Initialize();
    }

    /// <summary>
    /// Updates interpolation for all active players. Call this from a centralized update loop.
    /// </summary>
    /// <param name="dt">The delta time for this frame.</param>
    public void UpdateInterpolations(float dt) {
        foreach (var container in _activePlayers.Values.OfType<GameObject>())
        {
            // Cache component reference if accessed frequently
            if (container.TryGetComponent<PredictiveInterpolation>(out var interpolation)) {
                interpolation.ManualUpdate(dt);
            }
        }
    }

    /// <summary>
    /// Register the relevant hooks for player-related operations.
    /// </summary>
    public void RegisterHooks() {
        _skinManager.RegisterHooks();
        
        CustomHooks.HeroControllerStartAction += HeroControllerOnStart;
    }

    /// <summary>
    /// Deregister the relevant hooks for player-related operations.
    /// </summary>
    public void DeregisterHooks() {
        _skinManager.DeregisterHooks();
        
        CustomHooks.HeroControllerStartAction -= HeroControllerOnStart;
    }

    /// <summary>
    /// Callback method for when the HeroController starts so we can create the player pool.
    /// </summary>
    private void HeroControllerOnStart() {
        TryCreatePlayerPool();
    }

    /// <summary>
    /// Try to create the initial pool of player objects if it hasn't been created yet.
    /// </summary>
    private void TryCreatePlayerPool() {
        if (_playerContainerPrefab) {
            return;
        }
        
        // Create a player container prefab, used to spawn players
        _playerContainerPrefab = new GameObject(PlayerContainerPrefabName);

        _playerContainerPrefab.AddComponent<PredictiveInterpolation>();

        var playerPrefab = new GameObject(PlayerObjectPrefabName,
            typeof(BoxCollider2D),
            typeof(MeshFilter),
            typeof(MeshRenderer),
            typeof(tk2dSprite),
            typeof(tk2dSpriteAnimator),
            typeof(Rigidbody2D),
            typeof(CoroutineCancelComponent)
        ) {
            layer = 9
        };
        
        playerPrefab.transform.SetParent(_playerContainerPrefab.transform);

        // Now we need to copy over a lot of variables from the local player object
        var localPlayerObject = HeroController.instance.gameObject;

        // Obtain colliders from both objects
        var collider = playerPrefab.GetComponent<BoxCollider2D>();
        // We're not using the fact that Hornet has a BoxCollider as opposed to any other collider
        var localCollider = localPlayerObject.GetComponent<Collider2D>();
        var localColliderBounds = localCollider.bounds;

        // Copy collider offset and size
        collider.isTrigger = true;
        collider.offset = localCollider.offset;
        collider.size = localColliderBounds.size;
        collider.enabled = true;

        // Copy collider bounds
        var bounds = collider.bounds;
        var localBounds = localColliderBounds;
        bounds.min = localBounds.min;
        bounds.max = localBounds.max;
        
        // Set Rigidbody properties
        var rigidbody = playerPrefab.GetComponent<Rigidbody2D>();
        rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rigidbody.gravityScale = 0;
        rigidbody.bodyType = RigidbodyType2D.Kinematic;

        // Add some extra gameObjects related to animation effects
        new GameObject("Attacks").transform.SetParent(playerPrefab.transform);
        new GameObject("Bind Effects").transform.SetParent(playerPrefab.transform);
        new GameObject("Effects").transform.SetParent(playerPrefab.transform);
        new GameObject("Charm Effects").transform.SetParent(playerPrefab.transform);
        new GameObject("Special Attacks").transform.SetParent(playerPrefab.transform);
        new GameObject("Tool Effects").transform.SetParent(playerPrefab.transform);

        CreateUsername(_playerContainerPrefab);

        _playerContainerPrefab.SetActive(false);
        Object.DontDestroyOnLoad(_playerContainerPrefab);

        // Instantiate all the player objects for the pool
        for (ushort i = 0; i < InitialPoolSize; i++) {
            _inactivePlayers.Enqueue(CreateNewPlayerContainer());
        }
    }

    /// <summary>
    /// Create a new player container object from the <see cref="_playerContainerPrefab"/> prefab.
    /// </summary>
    /// <returns>A new GameObject representing the player container.</returns>
    private GameObject CreateNewPlayerContainer() {
        var playerContainer = Object.Instantiate(_playerContainerPrefab);
        if (playerContainer == null) {
            throw new Exception("Could not create new player container, instantiation failed");
        }
        
        Object.DontDestroyOnLoad(playerContainer);
        playerContainer.name = PlayerContainerName;

        var playerObj = playerContainer.FindGameObjectInChildren(PlayerObjectPrefabName);
        if (playerObj == null) {
            throw new Exception("Player object could not be found in instantiated player container");
        }

        MakeUniqueSpriteAnimator(playerObj);

        return playerContainer;
    }

    /// <summary>
    /// Update the position of a player with the given position.
    /// </summary>
    /// <param name="id">The ID of the player.</param>
    /// <param name="position">The new position of the player.</param>
    public void UpdatePosition(ushort id, Math_Vector2 position) {
        if (!_playerData.TryGetValue(id, out var playerData) || !playerData.IsInLocalScene) {
            // Logger.Info($"Tried to update position for ID {id} while player data did not exists");
            return;
        }

        var playerContainer = playerData.PlayerContainer;
        if (playerContainer) {
            var unityPosition = new Vector3(position.X, position.Y);

            playerContainer.GetComponent<PredictiveInterpolation>().SetNewPosition(unityPosition);
        }
    }

    /// <summary>
    /// Update the scale of a player with the given boolean.
    /// </summary>
    /// <param name="id">The ID of the player.</param>
    /// <param name="scale">The new scale as a boolean, true indicating an X scale of 1,
    /// false indicating an X scale of -1.</param>
    public void UpdateScale(ushort id, bool scale) {
        if (!_playerData.TryGetValue(id, out var playerData) || !playerData.IsInLocalScene) {
            // Logger.Info($"Tried to update scale for ID {id} while player data did not exist");
            return;
        }

        var playerObject = playerData.PlayerObject;
        if (playerObject == null) {
            Logger.Warn("Could not update scale of player, because player object is null");
            return;
        }
        
        SetPlayerObjectBoolScale(playerObject, scale);
    }

    /// <summary>
    /// Sets the scale of a player object from a boolean.
    /// </summary>
    /// <param name="playerObject">The GameObject representing the player.</param>
    /// <param name="scale">The new scale as a boolean, true indicating a X scale of 1,
    /// false indicating an X scale of -1.</param>
    private void SetPlayerObjectBoolScale(GameObject playerObject, bool scale) {
        if (!playerObject) {
            return;
        }

        var transform = playerObject.transform;
        var localScale = transform.localScale;
        var currentScaleX = localScale.x;

        if (currentScaleX > 0 != scale) {
            transform.localScale = new Vector3(
                currentScaleX * -1,
                localScale.y,
                localScale.z
            );
        }
    }

    /// <summary>
    /// Get the player object given the player ID.
    /// </summary>
    /// <param name="id">The player ID.</param>
    /// <returns>The GameObject for the player.</returns>
    public GameObject? GetPlayerObject(ushort id) {
        if (!_playerData.TryGetValue(id, out var playerData) || !playerData.IsInLocalScene) {
            Logger.Debug($"Tried to get the player data that does not exists for ID {id}");
            return null;
        }

        return playerData.PlayerObject;
    }

    /// <summary>
    /// Callback method for when the local user disconnects. Will reset all player related things
    /// to their default values.
    /// </summary>
    public void OnDisconnect() {
        // Reset the local player's team
        LocalPlayerTeam = Team.None;

        // Clear all players
        RecycleAllPlayers();

        // Remove name
        RemoveNameFromLocalPlayer();

        // Reset the skin of the local player
        _skinManager.ResetLocalPlayerSkin();
    }

    /// <summary>
    /// Recycle the player container of the player with the given ID back into the queue.
    /// </summary>
    /// <param name="id">The ID of the player.</param>
    public void RecyclePlayer(ushort id) {
        if (!_playerData.TryGetValue(id, out var playerData)) {
            Logger.Debug($"Tried to recycle player that does not exists for ID {id}");
            return;
        }

        RecyclePlayerByData(playerData);
    }

    /// <summary>
    /// Recycle the player container of the player with the given player data.
    /// </summary>
    /// <param name="playerData">The player data of the player.</param>
    private void RecyclePlayerByData(ClientPlayerData playerData) {
        // First reset the player
        ResetPlayer(playerData);

        // Remove the player container from the player data
        playerData.PlayerContainer = null;

        // Find the player container in the active containers if it exists
        if (!_activePlayers.TryGetValue(playerData.Id, out var container)) {
            return;
        }

        container.SetActive(false);
        container.name = PlayerContainerName;

        _activePlayers.Remove(playerData.Id);
        _inactivePlayers.Enqueue(container);
    }

    /// <summary>
    /// Recycle all existing players. <seealso cref="RecyclePlayer"/>
    /// </summary>
    public void RecycleAllPlayers() {
        foreach (var id in _playerData.Keys) {
            // Recycle player
            RecyclePlayer(id);
        }
    }

    /// <summary>
    /// Reset the player with the given player data.
    /// </summary>
    /// <param name="playerData">The player data of the player.</param>
    private void ResetPlayer(ClientPlayerData playerData) {
        var container = playerData.PlayerContainer;
        if (!container) {
            return;
        }

        ResetPlayerContainer(container);
    }

    /// <summary>
    /// Reset the given player container by removing all game objects not inherent to it.
    /// </summary>
    /// <param name="playerContainer">The game object representing the player container.</param>
    private void ResetPlayerContainer(GameObject playerContainer) {
        // Destroy all descendants and components that weren't originally on the container object
        foreach (Transform child in playerContainer.transform) {
            if (child.name != PlayerObjectPrefabName) {
                continue;
            }

            foreach (Transform grandChild in child) {
                if (grandChild.name is "Attacks" or "Effects" or "Spells") {
                    // Remove all grandchildren from the player prefab's children; there should be none
                    foreach (Transform greatGrandChild in grandChild) {
                        Logger.Debug(
                            $"Destroying child of {grandChild.name}: {greatGrandChild.name}, type: {greatGrandChild.GetType()}");
                        Object.Destroy(greatGrandChild.gameObject);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Spawn a new player object with the given data.
    /// </summary>
    /// <param name="playerData">The client player data for the player.</param>
    /// <param name="name">The username of the player.</param>
    /// <param name="position">The Vector2 denoting the position of the player.</param>
    /// <param name="scale">The boolean representing the scale of the player.</param>
    /// <param name="team">The team the player is on.</param>
    /// <param name="skinId">The ID of the skin the player is using.</param>
    public void SpawnPlayer(
        ClientPlayerData playerData,
        string name,
        Math_Vector2 position,
        bool scale,
        Team team,
        byte skinId
    ) {
        // First recycle the player by player data if they have an active container
        RecyclePlayerByData(playerData);

        GameObject playerContainer;

        if (_inactivePlayers.Count <= 0) {
            // Create a new player container
            playerContainer = CreateNewPlayerContainer();
        } else {
            // Dequeue a player container from the inactive players
            playerContainer = _inactivePlayers.Dequeue();
        }

        playerContainer.name = $"{PlayerContainerName} {playerData.Id}";

        _activePlayers[playerData.Id] = playerContainer;

        playerContainer.transform.SetPosition2D(position.X, position.Y);

        var playerObject = playerContainer.FindGameObjectInChildren(PlayerObjectPrefabName);
        if (playerObject == null) {
            throw new Exception("Player object could not be found in player container while spawning player");
        }

        SetPlayerObjectBoolScale(playerObject, scale);

        // Set container and children active
        playerContainer.SetActive(true);
        playerContainer.SetActiveChildren(true);

        AddNameToPlayer(playerContainer, name, team);

        // Let the SkinManager update the skin
        _skinManager.UpdatePlayerSkin(playerObject, skinId);

        // Store the player data
        playerData.PlayerContainer = playerContainer;
        playerData.PlayerObject = playerObject;
        playerData.Team = team;
        playerData.SkinId = skinId;
    }

    /// <summary>
    /// Create a unique copy of a player object's sprite animator so that skins are unique to each player.
    /// </summary>
    /// <param name="playerObject">The player object with the sprite animator component.</param>
    private void MakeUniqueSpriteAnimator(GameObject playerObject) {
        var localPlayer = HeroController.instance;
        // Copy over mesh filter variables
        var meshFilter = playerObject.GetComponent<MeshFilter>();
        var mesh = meshFilter.mesh;
        var localMesh = localPlayer.GetComponent<MeshFilter>().sharedMesh;

        mesh.vertices = localMesh.vertices;
        mesh.normals = localMesh.normals;
        mesh.uv = localMesh.uv;
        mesh.triangles = localMesh.triangles;
        mesh.tangents = localMesh.tangents;

        // Copy mesh renderer material
        var meshRenderer = playerObject.GetComponent<MeshRenderer>();
        meshRenderer.material = new Material(localPlayer.GetComponent<MeshRenderer>().material);

        // Copy over animation library
        var spriteAnimator = playerObject.GetComponent<tk2dSpriteAnimator>();
        // Make a smart copy of the sprite animator library so we can
        // modify the animator without having to worry about other player objects
        spriteAnimator.Library = CopyUtil.SmartCopySpriteAnimation(
            localPlayer.GetComponent<tk2dSpriteAnimator>().Library,
            playerObject
        );
    }

    /// <summary>
    /// Add a name to the given player container object.
    /// </summary>
    /// <param name="playerContainer">The GameObject for the player container.</param>
    /// <param name="name">The username that the object should have.</param>
    /// <param name="team">The team that the player is on.</param>
    public void AddNameToPlayer(GameObject playerContainer, string name, Team team = Team.None) {
        // Create a name object to set the username to, slightly above the player object
        var nameObject = playerContainer.FindGameObjectInChildren(UsernameObjectName);

        if (!nameObject) {
            nameObject = CreateUsername(playerContainer);
        }

        var textMeshObject = nameObject.GetComponent<TextMeshPro>();
        if (textMeshObject == null) {
            textMeshObject = nameObject.AddComponent<TextMeshPro>();
        }

        if (textMeshObject) {
            textMeshObject.text = name.ToUpper();
            ChangeNameColor(textMeshObject, team);
        }

        nameObject.SetActive(_serverSettings.DisplayNames);
    }

    /// <summary>
    /// Callback method for when a player team update is received.
    /// </summary>
    /// <param name="self">Whether this update is for the local player.</param>
    /// <param name="team">The new team of the player.</param>
    /// <param name="playerId">The ID of the player that has updated their team if <paramref name="self"/> is true.
    /// </param>
    public void OnPlayerTeamUpdate(bool self, Team team, ushort playerId = 0) {
        if (self) {
            Logger.Debug($"Received PlayerTeamUpdate for local player: {Enum.GetName(typeof(Team), team)}");

            UpdateLocalPlayerTeam(team);
            return;
        }
        
        Logger.Debug($"Received PlayerTeamUpdate for ID: {playerId}, team: {Enum.GetName(typeof(Team), team)}");

        UpdatePlayerTeam(playerId, team);
    }

    /// <summary>
    /// Reset the local player's team to be None and reset all existing player names and hit-boxes.
    /// </summary>
    public void ResetAllTeams() {
        UpdateLocalPlayerTeam(Team.None);

        foreach (var id in _playerData.Keys) {
            UpdatePlayerTeam(id, Team.None);
        }
    }

    /// <summary>
    /// Update the team of a player.
    /// </summary>
    /// <param name="id">The ID of the player.</param>
    /// <param name="team">The team that the player should have.</param>
    private void UpdatePlayerTeam(ushort id, Team team) {
        if (!_playerData.TryGetValue(id, out var playerData)) {
            Logger.Debug($"Tried to update team for ID {id} while player data did not exists");
            return;
        }

        // Update the team in the player data
        playerData.Team = team;

        if (!playerData.IsInLocalScene || playerData.PlayerContainer == null) {
            return;
        }

        // Get the name object and update the color based on the new team
        var nameObject = playerData.PlayerContainer.FindGameObjectInChildren(UsernameObjectName);
        if (nameObject == null) {
            throw new Exception("Name object could not be found in player container while updating player team");
        }
        
        var textMeshObject = nameObject.GetComponent<TextMeshPro>();

        ChangeNameColor(textMeshObject, team);
    }

    /// <summary>
    /// Update the team for the local player.
    /// </summary>
    /// <param name="team">The new team of the local player.</param>
    private void UpdateLocalPlayerTeam(Team team) {
        LocalPlayerTeam = team;

        var nameObject = HeroController.instance.gameObject.FindGameObjectInChildren(UsernameObjectName);
        if (nameObject == null) {
            throw new Exception("Name object could not be found in hero controller object while updating local player team");
        }

        var textMeshObject = nameObject.GetComponent<TextMeshPro>();
        ChangeNameColor(textMeshObject, team);
    }

    /// <summary>
    /// Get the team of a player.
    /// </summary>
    /// <param name="id">The ID of the player.</param>
    /// <returns>The team of the player.</returns>
    public Team GetPlayerTeam(ushort id) {
        if (!_playerData.TryGetValue(id, out var playerData)) {
            return Team.None;
        }

        return playerData.Team;
    }

    /// <summary>
    /// Get the type of crest of a player.
    /// </summary>
    /// <param name="id">The ID of the player.</param>
    /// <returns>The crest type of the player.</returns>
    public CrestType GetPlayerCrestType(ushort id) {
        if (!_playerData.TryGetValue(id, out var playerData)) {
            return CrestType.Hunter;
        }

        return playerData.CrestType;
    }

    /// <summary>
    /// Callback method for when a player updates their skin.
    /// </summary>
    /// <param name="self">Whether this update is for the local player.</param>
    /// <param name="skinId">The ID of the new skin of the player.</param>
    /// <param name="playerId">The ID of the player that has updated their skin if <paramref name="self"/> is true.
    /// </param>
    public void OnPlayerSkinUpdate(bool self, byte skinId, ushort playerId = 0) {
        if (self) {
            Logger.Debug($"Received PlayerSkinUpdate for local player: {skinId}");
            
            _skinManager.UpdateLocalPlayerSkin(skinId);
            return;
        }
        
        Logger.Debug($"Received PlayerSkinUpdate for ID: {playerId}, skin ID: {skinId}");

        if (!_playerData.TryGetValue(playerId, out var playerData)) {
            Logger.Debug("  Could not find player");
            return;
        }

        playerData.SkinId = skinId;

        // If the player is not in the local scene, we don't have to apply the skin update to the player object
        if (!playerData.IsInLocalScene) {
            return;
        }

        if (playerData.PlayerObject == null) {
            Logger.Warn("Could not update player skin, because player object is null");
            return;
        }

        _skinManager.UpdatePlayerSkin(playerData.PlayerObject, skinId);
    }

    /// <summary>
    /// Reset the skins of all players.
    /// </summary>
    public void ResetAllPlayerSkins() {
        // For each registered player, reset their skin
        foreach (var playerData in _playerData.Values) {
            _skinManager.ResetPlayerSkin(playerData.PlayerObject);
        }

        // Also reset our local players skin
        _skinManager.ResetLocalPlayerSkin();
    }

    /// <summary>
    /// Change the color of a TextMeshPro object according to the team.
    /// </summary>
    /// <param name="textMeshObject">The TextMeshPro object representing the name.</param>
    /// <param name="team">The team that the name should be colored after.</param>
    private void ChangeNameColor(TextMeshPro textMeshObject, Team team) {
        switch (team) {
            case Team.Moss:
                textMeshObject.color = new Color(0f / 255f, 150f / 255f, 0f / 255f);
                break;
            case Team.Hive:
                textMeshObject.color = new Color(200f / 255f, 150f / 255f, 0f / 255f);
                break;
            case Team.Grimm:
                textMeshObject.color = new Color(250f / 255f, 50f / 255f, 50f / 255f);
                break;
            case Team.Lifeblood:
                textMeshObject.color = new Color(50f / 255f, 150f / 255f, 200f / 255f);
                break;
            default:
                textMeshObject.color = Color.white;
                break;
        }
    }

    /// <summary>
    /// Remove the name from the local player.
    /// </summary>
    private void RemoveNameFromLocalPlayer() {
        if (HeroController.instance) {
            RemoveNameFromPlayer(HeroController.instance.gameObject);
        }
    }

    /// <summary>
    /// Remove the name of a given player container.
    /// </summary>
    /// <param name="playerContainer">The GameObject for the player container.</param>
    private void RemoveNameFromPlayer(GameObject playerContainer) {
        // Get the name object
        var nameObject = playerContainer.FindGameObjectInChildren(UsernameObjectName);

        // Deactivate it if it exists
        if (nameObject) {
            nameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Callback method for when the server settings are updated.
    /// </summary>
    /// <param name="displayNamesChanged">Whether the display names setting changed.</param>
    public void OnServerSettingsUpdated(bool displayNamesChanged) {
        if (displayNamesChanged) {
            foreach (var playerData in _playerData.Values) {
                if (playerData.PlayerContainer == null) {
                    continue;
                }
                
                var nameObject = playerData.PlayerContainer.FindGameObjectInChildren(UsernameObjectName);
                if (nameObject) {
                    nameObject.SetActive(_serverSettings.DisplayNames);
                }
            }

            var localPlayerObject = HeroController.instance.gameObject;
            if (localPlayerObject) {
                var nameObject = localPlayerObject.FindGameObjectInChildren(UsernameObjectName);
                if (nameObject) {
                    nameObject.SetActive(_serverSettings.DisplayNames);
                }
            }
        }
    }

    /// <summary>
    /// Create a new username object and add it as a child of a player container.
    /// </summary>
    /// <param name="playerContainer">The player container to add the username object as a child of.</param>
    /// <returns>The new GameObject that was created for the username.</returns>
    private GameObject CreateUsername(GameObject playerContainer) {
        var nameObject = new GameObject(UsernameObjectName);

        nameObject.transform.position = playerContainer.transform.position + Vector3.up * 1.5f;
        nameObject.transform.SetParent(playerContainer.transform);
        nameObject.transform.localScale = new Vector3(0.25f, 0.25f, nameObject.transform.localScale.z);

        nameObject.AddComponent<KeepWorldScalePositive>();

        // Add a TextMeshPro component to it, so we can render text
        var textMeshObject = nameObject.AddComponent<TextMeshPro>();
        textMeshObject.text = UsernameObjectName;
        textMeshObject.alignment = TextAlignmentOptions.Center;
        textMeshObject.font = Ui.Resources.FontManager.InGameNameFont;
        textMeshObject.fontSize = 22;
        textMeshObject.outlineWidth = 0.2f;
        textMeshObject.outlineColor = Color.black;

        nameObject.transform.SetParent(playerContainer.transform);

        return nameObject;
    }
}

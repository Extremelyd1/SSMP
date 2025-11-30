using System;
using Steamworks;
using SSMP.Logging;

namespace SSMP.Game;

/// <summary>
/// Manages Steam API initialization and availability checks.
/// Handles graceful fallback when Steam is not available (multi-platform support).
/// </summary>
public static class SteamManager {
    /// <summary>
    /// The actual version of the mod. (Probably should be centralized).
    /// </summary>
    private const string MOD_VERSION = "0.0.6";
    private const int DEFAULT_MAX_PLAYERS = 4;
    private const ELobbyType DEFAULT_LOBBY_TYPE = ELobbyType.k_ELobbyTypeFriendsOnly;

    /// <summary>
    /// Whether Steam API has been successfully initialized.
    /// </summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>
    /// The current Steam lobby ID if hosting.
    /// </summary>
    public static CSteamID CurrentLobbyId { get; private set; }

    /// <summary>
    /// Whether we are currently hosting a Steam lobby.
    /// </summary>
    public static bool IsHostingLobby { get; private set; }

    /// <summary>
    /// Event fired when a Steam lobby is successfully created.
    /// Parameters: Lobby ID, Lobby owner's username
    /// </summary>
    public static event Action<CSteamID, string>? LobbyCreatedEvent;

    /// <summary>
    /// Event fired when a list of lobbies is received.
    /// Parameters: Array of Lobby IDs
    /// </summary>
    public static event Action<CSteamID[]>? LobbyListReceivedEvent;

    /// <summary>
    /// Event fired when a lobby is successfully joined.
    /// Parameters: Lobby ID
    /// </summary>
    public static event Action<CSteamID>? LobbyJoinedEvent;

    /// <summary>
    /// Callback for when a Steam lobby is created.
    /// </summary>
    private static CallResult<LobbyCreated_t>? _lobbyCreatedCallback;

    /// <summary>
    /// Callback for when a list of lobbies is received.
    /// </summary>
    private static CallResult<LobbyMatchList_t>? _lobbyMatchListCallback;

    /// <summary>
    /// Callback for when a lobby is entered.
    /// </summary>
    private static CallResult<LobbyEnter_t>? _lobbyEnterCallback;

    /// <summary>
    /// Callback for when a user accepts a lobby invite via Steam Overlay.
    /// </summary>
    private static Callback<GameLobbyJoinRequested_t>? _gameLobbyJoinRequestedCallback;

    /// <summary>
    /// Callback for when a user joins a friend's game via Steam Friends list.
    /// </summary>
    private static Callback<GameRichPresenceJoinRequested_t>? _gameRichPresenceJoinRequestedCallback;

    /// <summary>
    /// Stored username for lobby creation callback.
    /// </summary>
    private static string? _pendingLobbyUsername;

    /// <summary>
    /// Initializes the Steam API if available.
    /// Safe to call multiple times - subsequent calls are no-ops.
    /// </summary>
    /// <returns>True if Steam was initialized successfully, false otherwise.</returns>
    public static bool Initialize() {
        if (IsInitialized) return true;

        try {
            // Check if Steam client is running
            if (!Packsize.Test()) {
                Logger.Warn("Steam: Packsize test failed, Steam may not be available");
                return false;
            }

            // Initialize Steam API
            if (!SteamAPI.Init()) {
                Logger.Warn("Steam: SteamAPI.Init() failed - Steam client may not be running or game may not be launched through Steam");
                return false;
            }

            IsInitialized = true;
            Logger.Info($"Steam: Initialized successfully (SteamID: {SteamUser.GetSteamID()})");

            // Register callbacks for joining via overlay/friends
            _gameLobbyJoinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            _gameRichPresenceJoinRequestedCallback = Callback<GameRichPresenceJoinRequested_t>.Create(OnGameRichPresenceJoinRequested);

            return true;
        } catch (Exception e) {
            Logger.Error($"Steam: Exception during initialization: {e}");
            return false;
        }
    }

    /// <summary>
    /// Creates a Steam lobby for multiplayer.
    /// </summary>
    /// <param name="username">Host's username to set as lobby name</param>
    /// <param name="maxPlayers">Maximum number of players (default 4)</param>
    /// <param name="lobbyType">Type of lobby to create (default friends-only)</param>
    /// <returns>True if lobby creation was initiated, false if Steam is unavailable</returns>
    public static void CreateLobby(string username, int maxPlayers = DEFAULT_MAX_PLAYERS, ELobbyType lobbyType = DEFAULT_LOBBY_TYPE) {
        if (!IsInitialized) {
            Logger.Warn("Cannot create Steam lobby: Steam is not initialized");
            return;
        }

        if (IsHostingLobby) {
            Logger.Info("Already hosting a Steam lobby, leaving it first");
            LeaveLobby();
        }

        _pendingLobbyUsername = username;
        Logger.Info($"Creating Steam lobby for {maxPlayers} players...");

        // Create lobby and register callback
        var apiCall = SteamMatchmaking.CreateLobby(lobbyType, maxPlayers);
        _lobbyCreatedCallback = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
        _lobbyCreatedCallback.Set(apiCall);
    }

    /// <summary>
    /// Requests a list of lobbies from Steam.
    /// </summary>
    /// <returns>True if the request was sent, false otherwise.</returns>
    public static void RequestLobbyList() {
        if (!IsInitialized) return;

        Logger.Info("Requesting Steam lobby list...");
        
        // Add filters if needed (e.g. only lobbies with specific data)
        SteamMatchmaking.AddRequestLobbyListStringFilter("version", MOD_VERSION, ELobbyComparison.k_ELobbyComparisonEqual);
        
        var apiCall = SteamMatchmaking.RequestLobbyList();
        _lobbyMatchListCallback = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);
        _lobbyMatchListCallback.Set(apiCall);
    }

    /// <summary>
    /// Joins a Steam lobby.
    /// </summary>
    /// <param name="lobbyId">The ID of the lobby to join.</param>
    /// <returns>True if the join request was sent, false otherwise.</returns>
    public static void JoinLobby(CSteamID lobbyId) {
        if (!IsInitialized) return;

        Logger.Info($"Joining Steam lobby: {lobbyId}");
        
        var apiCall = SteamMatchmaking.JoinLobby(lobbyId);
        _lobbyEnterCallback = CallResult<LobbyEnter_t>.Create(OnLobbyEnter);
        _lobbyEnterCallback.Set(apiCall);
    }

    /// <summary>
    /// Leaves the current lobby if hosting one.
    /// </summary>
    public static void LeaveLobby() {
        if (CurrentLobbyId == CSteamID.Nil || !IsInitialized) return;

        Logger.Info($"Leaving Steam lobby: {CurrentLobbyId}");
        SteamMatchmaking.LeaveLobby(CurrentLobbyId);
        
        IsHostingLobby = false;
        CurrentLobbyId = CSteamID.Nil;
    }

    /// <summary>
    /// Shuts down the Steam API.
    /// Should be called on application exit if Steam was initialized.
    /// </summary>
    public static void Shutdown() {
        if (!IsInitialized) return;

        try {
            // Leave any active lobby
            LeaveLobby();

            SteamAPI.Shutdown();
            IsInitialized = false;
            Logger.Info("Steam: Shut down successfully");
        } catch (Exception e) {
            Logger.Error($"Steam: Exception during shutdown: {e}");
        }
    }

    /// <summary>
    /// Runs Steam callbacks. Should be called regularly (e.g. in Update loop).
    /// No-op if Steam is not initialized.
    /// </summary>
    public static void RunCallbacks() {
        if (!IsInitialized) return;

        try {
            SteamAPI.RunCallbacks();
        } catch (Exception e) {
            Logger.Error($"Steam: Exception in RunCallbacks: {e}");
        }
    }

    /// <summary>
    /// Gets the owner of the specified lobby.
    /// </summary>
    /// <param name="lobbyId">The lobby ID.</param>
    /// <returns>The SteamID of the lobby owner.</returns>
    public static CSteamID GetLobbyOwner(CSteamID lobbyId) => SteamMatchmaking.GetLobbyOwner(lobbyId);

    /// <summary>
    /// Callback invoked when a Steam lobby is created.
    /// </summary>
    private static void OnLobbyCreated(LobbyCreated_t callback, bool ioFailure) {
        if (ioFailure || callback.m_eResult != EResult.k_EResultOK) {
            Logger.Error($"Failed to create Steam lobby: {callback.m_eResult}");
            _pendingLobbyUsername = null;
            return;
        }

        CurrentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
        IsHostingLobby = true;

        Logger.Info($"Steam lobby created successfully: {CurrentLobbyId}");

        // Set lobby metadata
        if (_pendingLobbyUsername != null) {
            SteamMatchmaking.SetLobbyData(CurrentLobbyId, "name", $"{_pendingLobbyUsername}'s Lobby");
        }
        SteamMatchmaking.SetLobbyData(CurrentLobbyId, "version", MOD_VERSION);

        // Fire event for listeners
        LobbyCreatedEvent?.Invoke(CurrentLobbyId, _pendingLobbyUsername ?? "Unknown");
        _pendingLobbyUsername = null;
    }

    /// <summary>
    /// Callback invoked when a list of lobbies is received.
    /// </summary>
    private static void OnLobbyMatchList(LobbyMatchList_t callback, bool ioFailure) {
        if (ioFailure) {
            Logger.Error("Failed to get lobby list: IO Failure");
            return;
        }

        Logger.Info($"Received {callback.m_nLobbiesMatching} lobbies");
        
        var lobbyIds = new CSteamID[callback.m_nLobbiesMatching];
        for (int i = 0; i < callback.m_nLobbiesMatching; i++) {
            lobbyIds[i] = SteamMatchmaking.GetLobbyByIndex(i);
        }

        LobbyListReceivedEvent?.Invoke(lobbyIds);
    }

    /// <summary>
    /// Callback invoked when a lobby is entered.
    /// </summary>
    private static void OnLobbyEnter(LobbyEnter_t callback, bool ioFailure) {
        if (ioFailure) {
            Logger.Error("Failed to join lobby: IO Failure");
            return;
        }

        if (callback.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) {
            Logger.Error($"Failed to join lobby: {(EChatRoomEnterResponse)callback.m_EChatRoomEnterResponse}");
            return;
        }

        CurrentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
        IsHostingLobby = false; // We are a client
        
        Logger.Info($"Joined lobby successfully: {CurrentLobbyId}");
        LobbyJoinedEvent?.Invoke(CurrentLobbyId);
    }

    /// <summary>
    /// Callback for when the user accepts a lobby invite via Steam Overlay.
    /// </summary>
    private static void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback) {
        Logger.Info($"Accepting lobby invite: {callback.m_steamIDLobby}");
        JoinLobby(callback.m_steamIDLobby);
    }

    /// <summary>
    /// Callback for when the user joins a friend's game via Steam Friends list.
    /// </summary>
    private static void OnGameRichPresenceJoinRequested(GameRichPresenceJoinRequested_t callback) {
        Logger.Info($"Joining friend's game via Rich Presence: {callback.m_rgchConnect}");
        
        // Parse lobby ID from connection string
        if (ulong.TryParse(callback.m_rgchConnect, out ulong lobbyIdRaw)) {
            JoinLobby(new CSteamID(lobbyIdRaw));
        } else {
            Logger.Warn($"Could not parse lobby ID from connect string: {callback.m_rgchConnect}");
        }
    }
}

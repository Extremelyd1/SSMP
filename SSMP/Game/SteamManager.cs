using System;
using System.Reflection;
using System.Threading;
using Steamworks;
using SSMP.Logging;

namespace SSMP.Game;

/// <summary>
/// Manages Steam API initialization and availability checks.
/// Handles graceful fallback when Steam is not available (multi-platform support).
/// OPTIMIZED VERSION - Enhanced performance through caching, reduced allocations, and lock optimization.
/// </summary>
public static class SteamManager {
    /// <summary>
    /// The default maximum number of players allowed in a lobby (250 = Steam's max = unlimited).
    /// </summary>
    private const int DefaultMaxPlayers = 250;

    /// <summary>
    /// The default lobby visibility type.
    /// </summary>
    private const ELobbyType DefaultLobbyType = ELobbyType.k_ELobbyTypeFriendsOnly;

    /// <summary>
    /// Whether Steam API has been successfully initialized.
    /// </summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>
    /// The current Steam lobby ID if hosting.
    /// </summary>
    private static CSteamID CurrentLobbyId { get; set; }

    /// <summary>
    /// Whether we are currently hosting a Steam lobby.
    /// </summary>
    public static bool IsHostingLobby { get; private set; }

    /// <summary>
    /// Whether we are currently in a Steam lobby (hosting or client).
    /// </summary>
    public static bool IsInLobby => CurrentLobbyId != NilLobbyId;

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
    /// Stored username for lobby creation callback.
    /// </summary>
    private static string? _pendingLobbyUsername;

    /// <summary>
    /// Stored lobby type for lobby creation callback.
    /// Used to determine if Rich Presence should be set (not set for private lobbies).
    /// </summary>
    private static ELobbyType _pendingLobbyType;

    /// <summary>
    /// Callback timer interval in milliseconds (~60Hz).
    /// </summary>
    private const int CallbackIntervalMs = 17;

    /// <summary>
    /// Cached CSteamID.Nil value to avoid repeated struct creation.
    /// </summary>
    private static readonly CSteamID NilLobbyId = CSteamID.Nil;

    /// <summary>
    /// Cached string keys for lobby metadata to avoid allocations.
    /// </summary>
    private const string LobbyKeyName = "name";
    private const string LobbyKeyVersion = "version";

    /// <summary>
    /// Cached mod version string from assembly metadata.
    /// </summary>
    private static readonly string ModVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

    /// <summary>
    /// Reusable callback instances to avoid GC allocations.
    /// </summary>
    private static CallResult<LobbyCreated_t>? _lobbyCreatedCallback;
    private static CallResult<LobbyMatchList_t>? _lobbyMatchListCallback;
    private static CallResult<LobbyEnter_t>? _lobbyEnterCallback;

    /// <summary>
    /// Thread-safe timer using Threading.Timer instead of System.Timers.Timer for better performance.
    /// </summary>
    private static Timer? _callbackTimer;

    /// <summary>
    /// Cancellation flag for callback timer (int for atomic operations).
    /// </summary>
    private static int _isRunningCallbacks;

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
            Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            Callback<GameRichPresenceJoinRequested_t>.Create(OnGameRichPresenceJoinRequested);

            // Pre-allocate callback instances to reuse
            _lobbyCreatedCallback = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyMatchListCallback = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);
            _lobbyEnterCallback = CallResult<LobbyEnter_t>.Create(OnLobbyEnter);

            // Start a timer-based callback loop that runs independently of frame rate
            StartCallbackTimer();

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
    /// <param name="maxPlayers">Maximum number of players (default 250 = unlimited)</param>
    /// <param name="lobbyType">Type of lobby to create (default friends-only)</param>
    public static void CreateLobby(
        string username, 
        int maxPlayers = DefaultMaxPlayers, 
        ELobbyType lobbyType = DefaultLobbyType
    ) {
        if (!IsInitialized) {
            Logger.Warn("Cannot create Steam lobby: Steam is not initialized");
            return;
        }

        if (IsHostingLobby) {
            Logger.Info("Already hosting a Steam lobby, leaving it first");
            LeaveLobby();
        }

        // Use Interlocked for atomic write (faster than lock for simple assignments)
        Volatile.Write(ref _pendingLobbyUsername, username);
        _pendingLobbyType = lobbyType;
        
        Logger.Info($"Creating Steam lobby for {maxPlayers} players (type: {lobbyType})...");

        // Create lobby and register callback (reuse pre-allocated callback)
        var apiCall = SteamMatchmaking.CreateLobby(lobbyType, maxPlayers);
        _lobbyCreatedCallback?.Set(apiCall);
    }

    /// <summary>
    /// Requests a list of lobbies from Steam.
    /// </summary>
    public static void RequestLobbyList() {
        if (!IsInitialized) return;

        Logger.Info("Requesting Steam lobby list...");
        
        // Add filters to only show lobbies with matching game version
        SteamMatchmaking.AddRequestLobbyListStringFilter(
            LobbyKeyVersion, 
            ModVersion, 
            ELobbyComparison.k_ELobbyComparisonEqual
        );
        
        var apiCall = SteamMatchmaking.RequestLobbyList();
        _lobbyMatchListCallback?.Set(apiCall);
    }

    /// <summary>
    /// Joins a Steam lobby.
    /// </summary>
    /// <param name="lobbyId">The ID of the lobby to join.</param>
    public static void JoinLobby(CSteamID lobbyId) {
        if (!IsInitialized) return;

        Logger.Info($"Joining Steam lobby: {lobbyId}");
        
        var apiCall = SteamMatchmaking.JoinLobby(lobbyId);
        _lobbyEnterCallback?.Set(apiCall);
    }

    /// <summary>
    /// Leaves the current lobby if hosting one.
    /// Optimized: Reduced allocation and streamlined logic.
    /// </summary>
    public static void LeaveLobby() {
        if (!IsInitialized) return;

        // Fast path check - direct comparison is faster than property access
        if (CurrentLobbyId == NilLobbyId) return;

        // Take local copy and clear state
        var lobbyToLeave = CurrentLobbyId;
        CurrentLobbyId = NilLobbyId;
        IsHostingLobby = false;

        // Clear Rich Presence so friends no longer see "Join Game" option
        SteamFriends.ClearRichPresence();

        Logger.Info($"Leaving Steam lobby: {lobbyToLeave}");
        SteamMatchmaking.LeaveLobby(lobbyToLeave);
    }

    /// <summary>
    /// Opens the Steam overlay invite dialog to invite friends to the current lobby.
    /// Works for all lobby types (Public, Friends Only, Private).
    /// </summary>
    public static void OpenInviteDialog() {
        if (!IsInitialized) {
            Logger.Warn("Cannot open invite dialog: Steam is not initialized");
            return;
        }

        if (CurrentLobbyId == NilLobbyId) {
            Logger.Warn("Cannot open invite dialog: Not in a lobby");
            return;
        }

        Logger.Info($"Opening Steam invite dialog for lobby: {CurrentLobbyId}");
        SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobbyId);
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

            // Stop the callback timer
            StopCallbackTimer();

            SteamAPI.Shutdown();
            IsInitialized = false;
            Logger.Info("Steam: Shut down successfully");
        } catch (Exception e) {
            Logger.Error($"Steam: Exception during shutdown: {e}");
        }
    }

    /// <summary>
    /// Starts the timer-based callback loop.
    /// Optimized: Uses Threading.Timer for lower overhead and better performance.
    /// </summary>
    private static void StartCallbackTimer() {
        if (_callbackTimer != null) return;

        _callbackTimer = new Timer(
            OnCallbackTimerElapsed,
            null,
            CallbackIntervalMs,
            CallbackIntervalMs
        );

        Logger.Info($"Steam: Started callback timer at {1000 / CallbackIntervalMs:F0}Hz");
    }

    /// <summary>
    /// Stops the timer-based callback loop.
    /// </summary>
    private static void StopCallbackTimer() {
        var timer = Interlocked.Exchange(ref _callbackTimer, null);
        if (timer == null) return;

        timer.Dispose();
        Logger.Info("Steam: Stopped callback timer");
    }

    /// <summary>
    /// Timer callback that runs Steam API callbacks.
    /// Optimized: Uses atomic flag to prevent concurrent execution.
    /// </summary>
    private static void OnCallbackTimerElapsed(object? state) {
        if (!IsInitialized) return;

        // Prevent concurrent callback execution (lock-free)
        if (Interlocked.CompareExchange(ref _isRunningCallbacks, 1, 0) != 0) {
            return; // Already running, skip this tick
        }

        try {
            SteamAPI.RunCallbacks();
        } catch (Exception ex) {
            Logger.Error($"Steam: Exception in timer RunCallbacks:\n{ex}");
        } finally {
            Volatile.Write(ref _isRunningCallbacks, 0);
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
    /// Optimized: Reduced allocations and lock contention.
    /// </summary>
    private static void OnLobbyCreated(LobbyCreated_t callback, bool ioFailure) {
        if (ioFailure || callback.m_eResult != EResult.k_EResultOK) {
            Logger.Error($"Failed to create Steam lobby: {callback.m_eResult}");
            Volatile.Write(ref _pendingLobbyUsername, null);
            return;
        }

        var lobbyId = new CSteamID(callback.m_ulSteamIDLobby);
        CurrentLobbyId = lobbyId;
        IsHostingLobby = true;

        Logger.Info($"Steam lobby created successfully: {lobbyId}");

        // Get username atomically
        var username = Volatile.Read(ref _pendingLobbyUsername);

        // Set lobby metadata using Steam persona name and game version
        var steamName = SteamFriends.GetPersonaName();
        SteamMatchmaking.SetLobbyData(lobbyId, LobbyKeyName, $"{steamName}'s Lobby");
        SteamMatchmaking.SetLobbyData(lobbyId, LobbyKeyVersion, ModVersion);

        // Set Rich Presence based on lobby type
        // Private lobbies: NO connect key (truly invite-only, no "Join Game" button)
        // Public/Friends: Set connect key so friends can "Join Game" from Steam
        if (_pendingLobbyType != ELobbyType.k_ELobbyTypePrivate) {
            SteamFriends.SetRichPresence("connect", lobbyId.m_SteamID.ToString());
            SteamFriends.SetRichPresence("status", "In Lobby");
            Logger.Info($"Rich Presence set with connect={lobbyId.m_SteamID}");
        } else {
            // Private lobby: set status only, use /invite command to send invites
            SteamFriends.SetRichPresence("status", "In Private Lobby");
            Logger.Info("Private lobby - use /invite to send Steam invites");
        }

        // Fire event for listeners
        LobbyCreatedEvent?.Invoke(lobbyId, username ?? "Unknown");
        
        Volatile.Write(ref _pendingLobbyUsername, null);
    }

    /// <summary>
    /// Callback invoked when a list of lobbies is received.
    /// Optimized: Pre-allocated array size, single loop.
    /// </summary>
    private static void OnLobbyMatchList(LobbyMatchList_t callback, bool ioFailure) {
        if (ioFailure) {
            Logger.Error("Failed to get lobby list: IO Failure");
            return;
        }

        var count = (int)callback.m_nLobbiesMatching;
        Logger.Info($"Received {count} lobbies");
        
        var lobbyIds = new CSteamID[count];
        for (var i = 0; i < count; i++) {
            lobbyIds[i] = SteamMatchmaking.GetLobbyByIndex(i);
        }

        LobbyListReceivedEvent?.Invoke(lobbyIds);
    }

    /// <summary>
    /// Callback invoked when a lobby is entered.
    /// Optimized: Reduced lock scope.
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

        var lobbyId = new CSteamID(callback.m_ulSteamIDLobby);
        CurrentLobbyId = lobbyId;
        IsHostingLobby = false; // We are a client
        
        Logger.Info($"Joined lobby successfully: {lobbyId}");
        LobbyJoinedEvent?.Invoke(lobbyId);
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
    /// Optimized: Direct ulong parsing without intermediate variable.
    /// </summary>
    private static void OnGameRichPresenceJoinRequested(GameRichPresenceJoinRequested_t callback) {
        Logger.Info($"Joining friend's game via Rich Presence: {callback.m_rgchConnect}");
        
        // Parse lobby ID from connection string
        if (ulong.TryParse(callback.m_rgchConnect, out var lobbyIdRaw)) {
            JoinLobby(new CSteamID(lobbyIdRaw));
        } else {
            Logger.Warn($"Could not parse lobby ID from connect string: {callback.m_rgchConnect}");
        }
    }
}

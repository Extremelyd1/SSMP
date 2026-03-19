namespace MMS.Services.Lobby;

using Matchmaking;

/// <summary>Background service that removes expired lobbies and matchmaking sessions every 30 seconds.</summary>
public class LobbyCleanupService(
    LobbyService lobbyService,
    JoinSessionService joinSessionService,
    ILogger<LobbyCleanupService> logger
) : BackgroundService {
    /// <summary>How often the cleanup pass runs.</summary>
    private static readonly TimeSpan CleanupInterval
        = TimeSpan.FromSeconds(30);

    /// <summary>Accumulated lobby removals not yet written to the log.</summary>
    private int _pendingLobbies;

    /// <summary>Accumulated session removals not yet written to the log.</summary>
    private int _pendingSessions;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            if (!await WaitForNextCycle(stoppingToken))
                return;

            RunCleanup();
        }
    }

    /// <summary>
    /// Delays execution for one <see cref="CleanupInterval"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the delay completed normally;
    /// <see langword="false"/> if the service is stopping and the loop should exit.
    /// </returns>
    private static async Task<bool> WaitForNextCycle(CancellationToken stoppingToken) {
        try {
            await Task.Delay(CleanupInterval, stoppingToken);
            return true;
        } catch (OperationCanceledException) {
            return false;
        }
    }

    /// <summary>
    /// Runs one cleanup pass, removing dead lobbies and expired sessions,
    /// then delegates logging to <see cref="AccumulateAndLog"/>.
    /// </summary>
    private void RunCleanup() {
        try {
            var removedLobbies 
                = lobbyService.CleanupDeadLobbies(joinSessionService.CleanupSessionsForLobby);
            var removedSessions
                = joinSessionService.CleanupExpiredSessions();

            AccumulateAndLog(removedLobbies, removedSessions);
        } catch (Exception ex) {
            logger.LogError(ex, "Lobby cleanup iteration failed");
        }
    }

    /// <summary>
    /// Accumulates removal counts across consecutive active cycles.
    /// Calls <see cref="FlushPendingLogIfAny"/> once a quiet cycle (nothing removed) is observed,
    /// emitting at most one log line per burst regardless of how many cycles it spans.
    /// </summary>
    /// <param name="removedLobbies">Lobbies removed during this cycle.</param>
    /// <param name="removedSessions">Sessions removed during this cycle.</param>
    private void AccumulateAndLog(int removedLobbies, int removedSessions) {
        if (removedLobbies > 0 || removedSessions > 0) {
            _pendingLobbies += removedLobbies;
            _pendingSessions += removedSessions;
            return;
        }

        FlushPendingLogIfAny();
    }

    /// <summary>
    /// Writes accumulated removal counts to the log and resets the pending totals.
    /// Does nothing if there is nothing to report.
    /// </summary>
    private void FlushPendingLogIfAny() {
        if (_pendingLobbies == 0 && _pendingSessions == 0)
            return;

        logger.LogInformation(
            "Cleanup removed {Lobbies} expired lobbies and {Sessions} orphaned sessions",
            _pendingLobbies, _pendingSessions
        );

        _pendingLobbies = 0;
        _pendingSessions = 0;
    }
}

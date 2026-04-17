namespace MMS.Services.Lobbies;

using Matchmaking;

/// <summary>Background service that removes expired lobbies and matchmaking sessions every 30 seconds.</summary>
public class LobbyCleanupService(
    LobbyService lobbyService,
    JoinSessionService joinSessionService,
    ILogger<LobbyCleanupService> logger
) : BackgroundService {
    /// <summary>How often the cleanup pass runs.</summary>
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await Task.Delay(CleanupInterval, stoppingToken);

                var removedLobbies = lobbyService.CleanupDeadLobbies(joinSessionService.CleanupSessionsForLobby);
                var removedSessions = joinSessionService.CleanupExpiredSessions();

                if (removedLobbies > 0 || removedSessions > 0) {
                    logger.LogInformation(
                        "Cleanup removed {Lobbies} expired lobbies and {Sessions} orphaned sessions", removedLobbies,
                        removedSessions
                    );
                }
            } catch (OperationCanceledException) {
                return;
            } catch (Exception ex) {
                logger.LogError(ex, "Lobby cleanup iteration failed");
            }
        }
    }
}

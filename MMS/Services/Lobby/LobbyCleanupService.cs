namespace MMS.Services.Lobby;

using Matchmaking;

/// <summary>Background service that removes expired lobbies and matchmaking sessions every 30 seconds.</summary>
public class LobbyCleanupService(
    LobbyService lobbyService,
    JoinSessionService joinSessionService,
    ILogger<LobbyCleanupService> logger
) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Lobby cleanup service started");

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            } catch (OperationCanceledException) {
                break;
            }

            try {
                var removed = lobbyService.CleanupDeadLobbies(joinSessionService.CleanupSessionsForLobby);
                joinSessionService.CleanupExpiredSessions();
                if (removed > 0) {
                    logger.LogInformation("Removed {RemovedCount} expired lobbies", removed);
                }
            } catch (Exception ex) {
                logger.LogError(ex, "Lobby cleanup iteration failed");
            }
        }

        logger.LogInformation("Lobby cleanup service stopped");
    }
}

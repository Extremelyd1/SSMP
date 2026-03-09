namespace MMS.Services;

/// <summary>Background service that removes expired lobbies and discovery cache entries every 30 seconds.</summary>
public class LobbyCleanupService(
    LobbyService lobbyService,
    DiscoveryService discoveryService,
    ILogger<LobbyCleanupService> logger
) : BackgroundService {
    /// <summary>
    /// Runs the cleanup loop until <paramref name="stoppingToken"/> is signalled.
    /// Waits 30 seconds between each pass; expired lobbies and stale discovery entries
    /// are removed on every iteration.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token that stops the service when the host shuts down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("[CLEANUP] Service started");

        while (!stoppingToken.IsCancellationRequested) {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            var removed = lobbyService.CleanupDeadLobbies();
            if (removed > 0) logger.LogInformation("[CLEANUP] Removed {Count} expired lobbies", removed);
            discoveryService.Cleanup();
        }
    }
}

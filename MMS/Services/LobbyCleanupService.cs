namespace MMS.Services;

/// <summary>
/// Background service that periodically cleans up dead lobbies.
/// </summary>
public class LobbyCleanupService : BackgroundService {
    private readonly LobbyService _lobbyService;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(30);

    public LobbyCleanupService(LobbyService lobbyService) {
        _lobbyService = lobbyService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        Console.WriteLine("[CLEANUP] Background cleanup service started");
        
        while (!stoppingToken.IsCancellationRequested) {
            await Task.Delay(_cleanupInterval, stoppingToken);
            
            var removed = _lobbyService.CleanupDeadLobbies();
            if (removed > 0) {
                Console.WriteLine($"[CLEANUP] Removed {removed} dead lobbies");
            }
        }
    }
}

using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Providers;

public interface IGameProvider
{
    string Name { get; }
    GamePlatform Platform { get; }
    bool IsInstalled();
    bool IsRunning();
    Task StartClientAsync(CancellationToken cancellationToken = default);
    Task<List<Game>> GetGamesAsync(CancellationToken cancellationToken = default);
    Task LaunchGameAsync(Game game, CancellationToken cancellationToken = default);
}

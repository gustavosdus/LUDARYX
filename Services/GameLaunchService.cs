using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed class GameLaunchService
{
    private readonly LibraryService _library;

    public GameLaunchService(LibraryService library) => _library = library;

    public async Task LaunchAsync(Game game, CancellationToken cancellationToken = default)
    {
        var provider = _library.GetProvider(game.Platform)
            ?? throw new InvalidOperationException($"Provider não encontrado: {game.Platform}");
        await provider.LaunchGameAsync(game, cancellationToken);
    }
}

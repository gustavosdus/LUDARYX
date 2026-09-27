using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher.Providers;

public sealed class ManualProvider : IGameProvider
{
    private readonly JsonSettingsService _settingsService = new();
    public string Name => "Adicionados manualmente";
    public GamePlatform Platform => GamePlatform.Manual;
    public bool IsInstalled() => true;
    public bool IsRunning() => false;
    public Task StartClientAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<List<Game>> GetGamesAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();
        var result = settings.ManualGames.Select(x => new Game
        {
            Id = x.Id,
            Name = x.Name,
            Platform = GamePlatform.Manual,
            Executable = x.Executable,
            LaunchArguments = x.Arguments,
            LaunchUri = x.LaunchUri,
            CoverImage = x.CoverPath
        }).ToList();
        return Task.FromResult(result);
    }

    public Task LaunchGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        LaunchTargetValidator.ValidateForLaunch(game);
        if (!string.IsNullOrWhiteSpace(game.LaunchUri)) ProcessService.StartUri(game.LaunchUri);
        else if (!string.IsNullOrWhiteSpace(game.Executable)) ProcessService.Start(game.Executable, game.LaunchArguments);
        else throw new InvalidOperationException("O jogo manual não possui executável ou URI de inicialização.");
        return Task.CompletedTask;
    }
}

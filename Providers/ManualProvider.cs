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
        var result = settings.ManualGames.Select(definition =>
        {
            var profile = GetPreferredProfile(definition);
            return new Game
            {
                Id = definition.Id,
                Name = definition.Name,
                Platform = GamePlatform.Manual,
                Executable = profile?.Executable ?? definition.Executable,
                LaunchArguments = profile?.Arguments ?? definition.Arguments,
                LaunchUri = profile?.LaunchUri ?? definition.LaunchUri,
                InstallPath = profile?.WorkingDirectory ?? definition.WorkingDirectory,
                CoverImage = definition.CoverPath
            };
        }).ToList();

        return Task.FromResult(result);
    }

    public Task LaunchGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        var definition = _settingsService.Load().ManualGames
            .FirstOrDefault(x => x.Id.Equals(game.Id, StringComparison.OrdinalIgnoreCase));
        var profile = definition is null ? null : GetPreferredProfile(definition);

        var launchUri = profile?.LaunchUri ?? game.LaunchUri;
        var executable = profile?.Executable ?? game.Executable;
        var arguments = profile?.Arguments ?? game.LaunchArguments;
        var workingDirectory = profile?.WorkingDirectory ?? definition?.WorkingDirectory;
        var runAsAdministrator = profile?.RunAsAdministrator ?? definition?.RunAsAdministrator == true;

        if (!string.IsNullOrWhiteSpace(launchUri))
        {
            ProcessService.StartUri(launchUri);
        }
        else if (!string.IsNullOrWhiteSpace(executable))
        {
            ProcessService.Start(
                executable,
                arguments,
                workingDirectory,
                runAsAdministrator);
        }
        else
        {
            throw new InvalidOperationException("O perfil de inicialização selecionado não possui executável ou URI.");
        }

        return Task.CompletedTask;
    }

    private static ManualLaunchProfile? GetPreferredProfile(ManualGameDefinition definition)
    {
        definition.LaunchProfiles ??= new();

        if (definition.LaunchProfiles.Count == 0)
            return null;

        return definition.LaunchProfiles.FirstOrDefault(profile =>
                   profile.Id.Equals(definition.PreferredLaunchProfileId, StringComparison.OrdinalIgnoreCase))
               ?? definition.LaunchProfiles[0];
    }
}

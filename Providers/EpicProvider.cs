using System.Text.Json;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher.Providers;

public sealed class EpicProvider : IGameProvider
{
    public string Name => "Epic Games";
    public GamePlatform Platform => GamePlatform.Epic;
    private const string LauncherExe = @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe";
    private const string ManifestDirectory = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";

    public bool IsInstalled() => File.Exists(LauncherExe) || Directory.Exists(ManifestDirectory);
    public bool IsRunning() => ProcessService.IsRunning("EpicGamesLauncher");

    public Task StartClientAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning() && File.Exists(LauncherExe)) ProcessService.Start(LauncherExe);
        return Task.CompletedTask;
    }

    // O manifesto .item informa InstallLocation individual, inclusive para bibliotecas personalizadas.
    public Task<List<Game>> GetGamesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Game>();
        if (!Directory.Exists(ManifestDirectory)) return Task.FromResult(result);
        foreach (var file in Directory.EnumerateFiles(ManifestDirectory, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(SafeFileReadService.ReadAllText(file, SafeFileReadService.ManifestMaxBytes, "manifesto Epic"));
                var root = doc.RootElement;
                var name = StringProp(root, "DisplayName");
                var appName = StringProp(root, "AppName");
                var install = StringProp(root, "InstallLocation");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appName)) continue;
                result.Add(new Game
                {
                    Id = appName,
                    Name = name,
                    Platform = Platform,
                    InstallPath = install,
                    LaunchUri = $"com.epicgames.launcher://apps/{Uri.EscapeDataString(appName)}?action=launch"
                });
            }
            catch { }
        }
        return Task.FromResult(result);
    }

    private static string? StringProp(JsonElement root, string name) => root.TryGetProperty(name, out var v) ? v.GetString() : null;

    public async Task LaunchGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        await StartClientAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(game.LaunchUri)) ProcessService.StartUri(game.LaunchUri);
    }
}

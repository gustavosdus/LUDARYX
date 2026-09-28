using System.Text.Json;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher.Providers;

public sealed class EpicProvider : IGameProvider
{
    public string Name => "Epic Games";
    public GamePlatform Platform => GamePlatform.Epic;
    private const string ManifestDirectory = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";

    public bool IsInstalled() => !string.IsNullOrWhiteSpace(IntegrationDiagnosticService.FindExecutable(Platform)) || Directory.Exists(ManifestDirectory);
    public bool IsRunning() => ProcessService.IsRunning("EpicGamesLauncher");

    public Task StartClientAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsRunning()) return Task.CompletedTask;

        var launcherExe = IntegrationDiagnosticService.FindExecutable(Platform);
        if (string.IsNullOrWhiteSpace(launcherExe))
            throw new InvalidOperationException("O Epic Games Launcher não foi encontrado no caminho detectado.");

        ProcessService.StartTrustedDetectedLocalReparse(launcherExe);
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

    public Task LaunchGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(game.LaunchUri))
            throw new InvalidOperationException("A URI de inicialização do jogo da Epic não foi encontrada.");

        // Não abra EpicGamesLauncher.exe antes da URI. Quando o cliente está fechado,
        // iniciar o executável e logo em seguida acionar o protocolo faz o Windows/Epic
        // processar duas solicitações independentes; em algumas instalações isso gera
        // duas confirmações e a ação de launch se perde enquanto o cliente ainda inicia.
        //
        // O protocolo oficial com.epicgames.launcher:// é responsável por abrir o
        // cliente (se necessário) e enfileirar a abertura do jogo em uma única ação.
        ProcessService.StartUri(game.LaunchUri);
        return Task.CompletedTask;
    }
}

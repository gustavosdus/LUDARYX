using Microsoft.Win32;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher.Providers;

public sealed class SteamProvider : IGameProvider
{
    private static readonly string[] NonGameAppNameFragments =
    {
        "steamworks common redistributables",
        "steam linux runtime",
        "proton ",
        "steamvr",
        "dedicated server",
        "sdk ",
        "software development kit"
    };

    public string Name => "Steam";
    public GamePlatform Platform => GamePlatform.Steam;
    private string? SteamRoot => FindSteamRoot();

    private static string? FindSteamRoot()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var sub in new[] { @"Software\Valve\Steam", @"Software\WOW6432Node\Valve\Steam" })
            {
                using var key = hive.OpenSubKey(sub);
                var path = key?.GetValue("SteamPath")?.ToString() ?? key?.GetValue("InstallPath")?.ToString();
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return path;
            }
        }
        var candidates = new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" };
        return candidates.FirstOrDefault(Directory.Exists);
    }

    public bool IsInstalled() => !string.IsNullOrWhiteSpace(SteamRoot);
    public bool IsRunning() => ProcessService.IsRunning("steam");

    public Task StartClientAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning()) return Task.CompletedTask;
        var exe = Path.Combine(SteamRoot ?? "", "steam.exe");
        if (File.Exists(exe)) ProcessService.Start(exe);
        return Task.CompletedTask;
    }

    // libraryfolders.vdf é a fonte do Steam para bibliotecas padrão e adicionais em outros discos.
    public Task<List<Game>> GetGamesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Game>();
        if (SteamRoot is null) return Task.FromResult(result);

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SteamRoot };
        var libraryFile = Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            foreach (var path in VdfParser.FindValues(SafeFileReadService.ReadAllText(libraryFile, SafeFileReadService.ManifestMaxBytes, "biblioteca Steam"), "path"))
                if (Directory.Exists(path)) libraries.Add(path);
        }

        foreach (var library in libraries)
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var file in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var text = SafeFileReadService.ReadAllText(file, SafeFileReadService.ManifestMaxBytes, "manifesto Steam");
                    var appId = VdfParser.FindFirstValue(text, "appid");
                    var name = VdfParser.FindFirstValue(text, "name");
                    var installDir = VdfParser.FindFirstValue(text, "installdir");
                    if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name)) continue;
                    if (IsNonGameSteamApp(name)) continue;
                    result.Add(new Game
                    {
                        Id = appId,
                        Name = name,
                        Platform = Platform,
                        InstallPath = Path.Combine(apps, "common", installDir ?? ""),
                        LaunchUri = $"steam://run/{appId}"
                    });
                }
                catch { }
            }
        }
        return Task.FromResult(result);
    }

    private static bool IsNonGameSteamApp(string name)
    {
        var normalized = name.Trim();
        return NonGameAppNameFragments.Any(fragment =>
            normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    public async Task LaunchGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        await StartClientAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(game.LaunchUri)) ProcessService.StartUri(game.LaunchUri);
    }

    private static class VdfParser
    {
        public static string? FindFirstValue(string text, string key) => FindValues(text, key).FirstOrDefault();
        public static IEnumerable<string> FindValues(string text, string key)
        {
            var needle = $"\"{key}\"";
            var pos = 0;
            while ((pos = text.IndexOf(needle, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var start = text.IndexOf('"', pos + needle.Length);
                if (start < 0) yield break;
                var end = text.IndexOf('"', start + 1);
                if (end < 0) yield break;
                yield return text[(start + 1)..end];
                pos = end + 1;
            }
        }
    }
}

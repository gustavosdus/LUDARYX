using System.Diagnostics;
using Microsoft.Win32;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher.Providers;

public abstract class ClientShortcutProvider : IGameProvider
{
    public abstract string Name { get; }
    public abstract GamePlatform Platform { get; }
    protected abstract string ProcessName { get; }
    protected abstract string[] LauncherExecutables { get; }
    protected abstract string[] ShortcutDirectoryNames { get; }
    protected abstract string[] RegistrySubKeys { get; }
    protected virtual string[] ExcludedShortcutNames => Array.Empty<string>();

    public virtual bool IsInstalled() => FindLauncherExecutable() is not null || ShortcutRoots().Any(Directory.Exists);
    public bool IsRunning() => ProcessService.IsRunning(ProcessName);

    public Task StartClientAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning()) return Task.CompletedTask;
        var exe = FindLauncherExecutable();
        if (exe is not null) ProcessService.Start(exe);
        return Task.CompletedTask;
    }

    public virtual Task<List<Game>> GetGamesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Game>();
        foreach (var root in ShortcutRoots().Where(Directory.Exists))
        {
            try
            {
                foreach (var shortcut in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(shortcut);
                    if (string.IsNullOrWhiteSpace(name) || ExcludedShortcutNames.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("update", StringComparison.OrdinalIgnoreCase)) continue;

                    // Atalhos antigos podem permanecer no Menu Iniciar depois que um jogo
                    // é desinstalado. Não os trate como jogos instalados se o destino real
                    // do .lnk já não existir ou não for um arquivo local seguro.
                    if (!WindowsShortcutService.PointsToExistingLocalTarget(shortcut)) continue;

                    result.Add(new Game
                    {
                        Id = shortcut,
                        Name = name,
                        Platform = Platform,
                        LaunchUri = shortcut
                    });
                }
            }
            catch { }
        }
        result.AddRange(DiscoverInstalledGamesWithoutShortcuts());
        return Task.FromResult(result.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList());
    }

    protected virtual IEnumerable<Game> DiscoverInstalledGamesWithoutShortcuts() => Array.Empty<Game>();

    public virtual async Task LaunchGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        await StartClientAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(game.LaunchUri))
        {
            if (!TryValidateDetectedShortcut(game.LaunchUri, out var shortcut))
                throw new InvalidOperationException("O atalho detectado não é mais considerado seguro para execução.");
            ProcessService.StartShellFile(shortcut);
        }
        else if (!string.IsNullOrWhiteSpace(game.Executable))
        {
            ProcessService.Start(game.Executable);
        }
    }

    protected bool TryValidateDetectedShortcut(string value, out string shortcut)
    {
        shortcut = string.Empty;
        try
        {
            var fullPath = Path.GetFullPath(value);
            if (!fullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                return false;

            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return false;

            foreach (var root in ShortcutRoots().Where(Directory.Exists))
            {
                var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                               + Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    shortcut = fullPath;
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    protected string? FindLauncherExecutable()
    {
        foreach (var exe in LauncherExecutables)
            if (File.Exists(exe)) return exe;

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var sub in RegistrySubKeys)
        {
            try
            {
                using var key = hive.OpenSubKey(sub);
                var install = key?.GetValue("InstallLocation")?.ToString() ?? key?.GetValue("path")?.ToString() ?? key?.GetValue("Path")?.ToString();
                if (!string.IsNullOrWhiteSpace(install))
                {
                    foreach (var exeName in LauncherExecutables.Select(Path.GetFileName).Where(x => x is not null))
                    {
                        var candidate = Path.Combine(install, exeName!);
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    private IEnumerable<string> ShortcutRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        foreach (var baseRoot in new[] { common, user })
            foreach (var folder in ShortcutDirectoryNames)
            {
                if (!string.IsNullOrWhiteSpace(baseRoot)) roots.Add(Path.Combine(baseRoot, folder));
            }
        return roots;
    }
}

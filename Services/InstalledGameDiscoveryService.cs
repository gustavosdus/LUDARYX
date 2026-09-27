using Microsoft.Win32;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

/// <summary>Detecta jogos instalados sem depender de atalhos do Menu Iniciar.</summary>
public static class InstalledGameDiscoveryService
{
    private static readonly RegistryView[] Views = { RegistryView.Registry64, RegistryView.Registry32 };

    public static IEnumerable<Game> FromRegistryUninstall(GamePlatform platform, string[] publisherHints, string[] productNameExclusions)
    {
        var results = new List<Game>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in Views)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var subName in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var key = uninstall.OpenSubKey(subName);
                        var name = key?.GetValue("DisplayName")?.ToString();
                        var install = key?.GetValue("InstallLocation")?.ToString();
                        var publisher = key?.GetValue("Publisher")?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(install)) continue;
                        install = Environment.ExpandEnvironmentVariables(install.Trim().TrimEnd('\\'));
                        if (!Directory.Exists(install)) continue;
                        if (publisherHints.Length > 0 && !publisherHints.Any(h => publisher.Contains(h, StringComparison.OrdinalIgnoreCase) || name.Contains(h, StringComparison.OrdinalIgnoreCase))) continue;
                        if (productNameExclusions.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
                        var exe = FindLikelyGameExe(install, name);
                        if (exe is null || !LaunchTargetValidator.TryValidateDetectedExecutable(exe, out exe, out _)) continue;
                        results.Add(new Game { Id = subName, Name = name.Trim(), Platform = platform, InstallPath = install, Executable = exe, Installed = true });
                    }
                    catch { }
                }
            }
            catch { }
        }
        return Deduplicate(results);
    }

    public static IEnumerable<Game> FromRegistryUninstall(GamePlatform platform, string[] publisherHints, string[] productNameExclusions, string[]? pathHints)
    {
        var results = new List<Game>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in Views)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var subName in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var key = uninstall.OpenSubKey(subName);
                        var name = key?.GetValue("DisplayName")?.ToString();
                        var install = key?.GetValue("InstallLocation")?.ToString();
                        var publisher = key?.GetValue("Publisher")?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(install)) continue;
                        install = Environment.ExpandEnvironmentVariables(install.Trim().TrimEnd('\\'));
                        if (!Directory.Exists(install)) continue;
                        if (publisherHints.Length > 0 && !publisherHints.Any(h => publisher.Contains(h, StringComparison.OrdinalIgnoreCase) || name.Contains(h, StringComparison.OrdinalIgnoreCase))) continue;
                        if (productNameExclusions.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
                        if (pathHints is { Length: > 0 } && !pathHints.Any(h => install.Contains(h, StringComparison.OrdinalIgnoreCase))) continue;
                        var exe = FindLikelyGameExe(install, name);
                        if (exe is null || !LaunchTargetValidator.TryValidateDetectedExecutable(exe, out exe, out _)) continue;
                        results.Add(new Game { Id = subName, Name = name.Trim(), Platform = platform, InstallPath = install, Executable = exe, Installed = true });
                    }
                    catch { }
                }
            }
            catch { }
        }
        return Deduplicate(results);
    }

    public static IEnumerable<Game> FromRegistryInstallKeys(GamePlatform platform, string[] registryPaths, string installValueName, Func<string, string, string?>? nameResolver = null, Func<string, string, string?>? launchResolver = null)
    {
        var results = new List<Game>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in Views)
        foreach (var path in registryPaths)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var root = baseKey.OpenSubKey(path);
                if (root is null) continue;
                foreach (var subName in root.GetSubKeyNames())
                {
                    try
                    {
                        using var key = root.OpenSubKey(subName);
                        var install = key?.GetValue(installValueName)?.ToString();
                        if (string.IsNullOrWhiteSpace(install)) continue;
                        install = Environment.ExpandEnvironmentVariables(install.Trim().TrimEnd('\\'));
                        if (!Directory.Exists(install)) continue;
                        var name = nameResolver?.Invoke(subName, install) ?? subName;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var exe = FindLikelyGameExe(install, name);
                        var launchUri = launchResolver?.Invoke(subName, install);
                        if (exe is not null && !LaunchTargetValidator.TryValidateDetectedExecutable(exe, out exe, out _)) exe = null;
                        if (exe is null && launchUri is null) continue;
                        results.Add(new Game { Id = subName, Name = name.Trim(), Platform = platform, InstallPath = install, Executable = exe, LaunchUri = launchUri, Installed = true });
                    }
                    catch { }
                }
            }
            catch { }
        }
        return Deduplicate(results);
    }

    public static string? FindLikelyGameExe(string directory, string gameName)
    {
        try
        {
            var candidates = Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly).Where(IsGameExecutable).ToList();
            if (candidates.Count == 0)
                candidates = Directory.EnumerateFiles(directory, "*.exe", SearchOption.AllDirectories)
                    .Where(p => RelativeDepth(directory, p) <= 2).Where(IsGameExecutable).Take(100).ToList();
            var tokens = Tokenize(gameName);
            return candidates.OrderByDescending(p => tokens.Count(t => Normalize(Path.GetFileNameWithoutExtension(p)).Contains(t, StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(p => new FileInfo(p).Length).FirstOrDefault();
        }
        catch { return null; }
    }

    private static int RelativeDepth(string root, string path) =>
        Math.Max(0, path.Substring(root.Length).Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length);

    private static bool IsGameExecutable(string path)
    {
        var n = Path.GetFileName(path);
        return !n.Contains("unins", StringComparison.OrdinalIgnoreCase) && !n.Contains("install", StringComparison.OrdinalIgnoreCase)
            && !n.Contains("setup", StringComparison.OrdinalIgnoreCase) && !n.Contains("update", StringComparison.OrdinalIgnoreCase)
            && !n.Contains("launcher", StringComparison.OrdinalIgnoreCase) && !n.Contains("crash", StringComparison.OrdinalIgnoreCase)
            && !n.Equals("EADesktop.exe", StringComparison.OrdinalIgnoreCase) && !n.Equals("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase)
            && !n.Equals("UbisoftConnect.exe", StringComparison.OrdinalIgnoreCase) && !n.Equals("upc.exe", StringComparison.OrdinalIgnoreCase)
            && !n.Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Game> Deduplicate(IEnumerable<Game> games) => games.GroupBy(g => $"{g.Platform}:{Normalize(g.Name)}", StringComparer.OrdinalIgnoreCase).Select(g => g.First());
    private static string[] Tokenize(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Normalize).Where(x => x.Length > 2).ToArray();
    private static string Normalize(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}

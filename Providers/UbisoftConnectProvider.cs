using Microsoft.Win32;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher.Providers;

public sealed class UbisoftConnectProvider : ClientShortcutProvider
{
    public override string Name => "Ubisoft Connect";
    public override GamePlatform Platform => GamePlatform.UbisoftConnect;
    protected override string ProcessName => "upc";
    protected override string[] LauncherExecutables => new[] { @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe", @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\upc.exe" };
    protected override string[] ShortcutDirectoryNames => new[] { "Ubisoft", "Ubisoft Game Launcher", "Ubisoft Connect" };
    protected override string[] RegistrySubKeys => new[] { @"Software\Ubisoft", @"Software\WOW6432Node\Ubisoft" };
    protected override string[] ExcludedShortcutNames => new[] { "Ubisoft Connect", "Ubisoft Game Launcher", "Uninstall" };

    protected override IEnumerable<Game> DiscoverInstalledGamesWithoutShortcuts()
    {
        // InstallDir é a fonte principal e funciona tanto para C: quanto para discos personalizados.
        // O identificador é lançado pelo próprio Ubisoft Connect, e não pelo shell do Windows.
        return InstalledGameDiscoveryService.FromRegistryInstallKeys(Platform,
            new[] { @"SOFTWARE\Ubisoft\Launcher\Installs", @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs" }, "InstallDir", ResolveName,
            (id, _) => $"uplay://launch/{id}/0");
    }

    public override async Task LaunchGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var launchUri = game.LaunchUri;
        if (string.IsNullOrWhiteSpace(launchUri))
            throw new InvalidOperationException($"Não foi possível encontrar o identificador do Ubisoft Connect para \"{game.Name}\".");

        // Não usamos Process.Start(uri) aqui. Em algumas instalações do Windows, o
        // protocolo uplay:// não está registrado corretamente e o sistema encaminha
        // o link para a Microsoft Store. O método suportado é entregar o deep link
        // diretamente ao executável do Ubisoft Connect.
        var launcher = FindUbisoftLauncher();
        if (launcher is null)
            throw new FileNotFoundException("Não foi possível localizar o Ubisoft Connect (upc.exe/UbisoftConnect.exe).");

        ProcessService.Start(launcher, launchUri);
        await Task.CompletedTask;
    }

    private static string? FindUbisoftLauncher()
    {
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\upc.exe",
            @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe",
            @"C:\Program Files\Ubisoft\Ubisoft Game Launcher\upc.exe",
            @"C:\Program Files\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe"
        };

        var existing = candidates.FirstOrDefault(File.Exists);
        if (existing is not null) return existing;

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var subKey in new[] { @"SOFTWARE\Ubisoft\Launcher", @"SOFTWARE\WOW6432Node\Ubisoft\Launcher" })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(subKey);
                var installDir = key?.GetValue("InstallDir")?.ToString()
                    ?? key?.GetValue("InstallLocation")?.ToString();
                if (string.IsNullOrWhiteSpace(installDir)) continue;

                foreach (var fileName in new[] { "upc.exe", "UbisoftConnect.exe" })
                {
                    var path = Path.Combine(installDir, fileName);
                    if (File.Exists(path)) return path;
                }
            }
            catch { }
        }

        return null;
    }

    private static string? ResolveName(string id, string installDir)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\UPlay Install {id}");
                var name = key?.GetValue("DisplayName")?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch { }
        }
        return Path.GetFileName(installDir.TrimEnd('\\', '/'));
    }
}

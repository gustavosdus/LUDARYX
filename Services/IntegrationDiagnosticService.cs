using System.Diagnostics;
using Microsoft.Win32;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed record IntegrationDiagnostic(string? ExecutablePath, string? Version, string Summary);

public static class IntegrationDiagnosticService
{
    public static IntegrationDiagnostic Get(GamePlatform platform)
    {
        var path = platform switch
        {
            GamePlatform.Steam => FirstExisting(
                ReadRegistryString(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe")),
            GamePlatform.Epic => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe")),
            GamePlatform.GOG => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "GalaxyClient.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GOG Galaxy", "GalaxyClient.exe")),
            GamePlatform.EAApp => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Electronic Arts", "EA Desktop", "EA Desktop", "EADesktop.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Electronic Arts", "EA Desktop", "EA Desktop", "EADesktop.exe")),
            GamePlatform.UbisoftConnect => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher", "UbisoftConnect.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ubisoft", "Ubisoft Game Launcher", "UbisoftConnect.exe")),
            GamePlatform.BattleNet => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net Launcher.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Battle.net", "Battle.net Launcher.exe")),
            GamePlatform.RiotClient => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games", "Metadata", "RiotClientServices.exe"),
                @"C:\Riot Games\Riot Client\RiotClientServices.exe"),
            _ => null
        };

        if (platform == GamePlatform.Xbox)
            return new IntegrationDiagnostic(null, null, "Aplicativo Microsoft Store / Gaming Services");

        if (string.IsNullOrWhiteSpace(path))
            return new IntegrationDiagnostic(null, null, "Executável do cliente não localizado em caminho padrão.");

        string? version = null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            version = info.ProductVersion ?? info.FileVersion;
        }
        catch
        {
        }

        var summary = string.IsNullOrWhiteSpace(version)
            ? path
            : $"{path} • v{version}";
        return new IntegrationDiagnostic(path, version, summary);
    }

    private static string? FirstExisting(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            try
            {
                var normalized = candidate.Replace('/', Path.DirectorySeparatorChar);
                if (File.Exists(normalized))
                    return Path.GetFullPath(normalized);
            }
            catch
            {
            }
        }

        return null;
    }

    private static string? ReadRegistryString(string keyPath, string valueName)
    {
        try
        {
            return Registry.GetValue(keyPath, valueName, null)?.ToString();
        }
        catch
        {
            return null;
        }
    }
}

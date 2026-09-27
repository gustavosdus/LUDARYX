using Microsoft.Win32;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Descobre raízes de instalação sem assumir que os jogos estejam apenas em C:.
/// A prioridade é: configuração/registro do cliente, caminhos padrão e raízes comuns
/// em outros discos. A descoberta de jogos continua sendo responsabilidade de cada launcher.
/// </summary>
public static class GameInstallRootsService
{
    private static readonly RegistryView[] Views = { RegistryView.Registry64, RegistryView.Registry32 };

    public static IEnumerable<string> Existing(params string[] paths)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            try
            {
                expanded = Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (Directory.Exists(expanded)) result.Add(expanded);
            }
            catch { }
        }
        return result;
    }

    public static IEnumerable<string> FixedDriveRoots(params string[] folderNames)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                foreach (var folder in folderNames)
                {
                    if (string.IsNullOrWhiteSpace(folder)) continue;
                    try
                    {
                        var path = Path.Combine(drive.RootDirectory.FullName, folder);
                        if (Directory.Exists(path)) result.Add(path);
                    }
                    catch { }
                }
            }
        }
        catch { }
        return result;
    }

    public static IEnumerable<string> RegistryValues(params string[] registryPaths)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in Views)
        foreach (var path in registryPaths)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(path);
                if (key is null) continue;
                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName)?.ToString();
                    AddDirectory(result, value);
                }
                foreach (var subName in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(subName);
                    foreach (var valueName in sub?.GetValueNames() ?? Array.Empty<string>())
                    {
                        var value = sub?.GetValue(valueName)?.ToString();
                        AddDirectory(result, value);
                    }
                }
            }
            catch { }
        }
        return result;
    }

    private static void AddDirectory(HashSet<string> result, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        value = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        try
        {
            if (Directory.Exists(value)) result.Add(Path.GetFullPath(value).TrimEnd('\\', '/'));
        }
        catch { }
    }
}

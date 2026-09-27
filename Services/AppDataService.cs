namespace UnifiedGameLauncher.Services;

public static class AppDataService
{
    private const string CurrentFolderName = "LUDARYX";
    private const string LegacyFolderName = "UnifiedGameLauncher";

    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CurrentFolderName);

    public static string LegacyRootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LegacyFolderName);

    public static string GetPath(params string[] parts)
    {
        var path = parts.Aggregate(RootDirectory, Path.Combine);
        var directory = Path.HasExtension(path) ? Path.GetDirectoryName(path) : path;
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        return path;
    }

    public static void EnsureMigrated()
    {
        Directory.CreateDirectory(RootDirectory);

        if (!Directory.Exists(LegacyRootDirectory) ||
            string.Equals(LegacyRootDirectory, RootDirectory, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            CopyDirectoryIfMissing(LegacyRootDirectory, RootDirectory);
            File.WriteAllText(
                Path.Combine(RootDirectory, ".migration-from-unifiedgamelauncher-complete"),
                DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException("Could not migrate legacy application data", ex);
        }
    }

    private static void CopyDirectoryIfMissing(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            if (!File.Exists(target))
                File.Copy(file, target, overwrite: false);
        }
    }
}

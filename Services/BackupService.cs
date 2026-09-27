using System.IO.Compression;

namespace UnifiedGameLauncher.Services;

public static class BackupService
{
    private static readonly string[] ExcludedFolders = { "Logs", "Updates" };

    public static void Export(string destinationZip)
    {
        AppDataService.EnsureMigrated();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationZip)!);

        if (File.Exists(destinationZip))
            File.Delete(destinationZip);

        using var archive = ZipFile.Open(destinationZip, ZipArchiveMode.Create);
        var root = AppDataService.RootDirectory;

        if (!Directory.Exists(root))
            return;

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (ExcludedFolders.Contains(firstSegment, StringComparer.OrdinalIgnoreCase))
                continue;

            archive.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
        }
    }

    public static void Import(string sourceZip)
    {
        if (!File.Exists(sourceZip))
            throw new FileNotFoundException("O arquivo de backup não foi encontrado.", sourceZip);

        AppDataService.EnsureMigrated();

        using var archive = ZipFile.OpenRead(sourceZip);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var target = Path.GetFullPath(Path.Combine(AppDataService.RootDirectory, entry.FullName));
            var root = Path.GetFullPath(AppDataService.RootDirectory) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O backup contém um caminho inválido.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}

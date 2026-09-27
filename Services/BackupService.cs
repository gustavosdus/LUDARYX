using System.IO.Compression;

namespace UnifiedGameLauncher.Services;

public static class BackupService
{
    // "covers" é cache regenerável. Além de aumentar muito o backup, ele pode estar
    // sendo atualizado por um provider ou por outra instância do LUDARYX durante a
    // restauração. Artes escolhidas pelo usuário ficam em "custom-covers" e continuam
    // fazendo parte do backup.
    private static readonly string[] ExcludedFolders = { "Logs", "Updates", "covers" };

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
            if (ShouldSkip(relative))
                continue;

            archive.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
        }
    }

    public static void Import(string sourceZip)
    {
        if (!File.Exists(sourceZip))
            throw new FileNotFoundException("O arquivo de backup não foi encontrado.", sourceZip);

        AppDataService.EnsureMigrated();

        var root = Path.GetFullPath(AppDataService.RootDirectory) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(sourceZip);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            // Backups produzidos pelas primeiras builds da 1.0.2 incluíam o cache
            // de capas. Ignora essas entradas também na importação para que backups
            // antigos possam ser restaurados sem tentar substituir imagens em uso.
            if (ShouldSkip(entry.FullName))
                continue;

            var target = Path.GetFullPath(Path.Combine(AppDataService.RootDirectory, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O backup contém um caminho inválido.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static bool ShouldSkip(string relativePath)
    {
        var normalized = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var firstSegment = normalized
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return firstSegment is not null &&
               ExcludedFolders.Contains(firstSegment, StringComparer.OrdinalIgnoreCase);
    }
}

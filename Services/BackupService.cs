using System.IO.Compression;

namespace UnifiedGameLauncher.Services;

public sealed class BackupSelection
{
    public bool SettingsAndLibrary { get; set; } = true;
    public bool CustomArtwork { get; set; } = true;
    public bool MetadataCache { get; set; } = true;

    public bool HasAny => SettingsAndLibrary || CustomArtwork || MetadataCache;
}

public static class BackupService
{
    private static readonly string[] AlwaysExcludedFolders = { "Logs", "Updates", "covers" };

    public static long GetEstimatedBackupSourceSizeBytes(BackupSelection? selection = null)
    {
        selection ??= new BackupSelection();
        AppDataService.EnsureMigrated();

        var root = AppDataService.RootDirectory;
        if (!Directory.Exists(root))
            return 0;

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (!ShouldInclude(relative, selection))
                continue;

            try { total += new FileInfo(file).Length; }
            catch { }
        }

        return total;
    }

    public static void Export(string destinationZip, BackupSelection? selection = null)
    {
        selection ??= new BackupSelection();
        if (!selection.HasAny)
            throw new InvalidOperationException("Selecione pelo menos uma categoria para o backup.");

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
            if (!ShouldInclude(relative, selection))
                continue;

            archive.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
        }
    }

    public static long GetEstimatedImportSizeBytes(string sourceZip, BackupSelection? selection = null)
    {
        selection ??= new BackupSelection();
        if (!File.Exists(sourceZip))
            return 0;

        long total = 0;
        using var archive = ZipFile.OpenRead(sourceZip);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || !ShouldInclude(entry.FullName, selection))
                continue;

            total += Math.Max(0, entry.Length);
        }

        return total;
    }

    public static void Import(string sourceZip, BackupSelection? selection = null)
    {
        selection ??= new BackupSelection();
        if (!selection.HasAny)
            throw new InvalidOperationException("Selecione pelo menos uma categoria para restaurar.");

        if (!File.Exists(sourceZip))
            throw new FileNotFoundException("O arquivo de backup não foi encontrado.", sourceZip);

        AppDataService.EnsureMigrated();
        var root = Path.GetFullPath(AppDataService.RootDirectory) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(sourceZip);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            // Aplica a mesma seleção usada na exportação também durante a restauração.
            // Isso impede que um backup completo restaure categorias desmarcadas.
            if (!ShouldInclude(entry.FullName, selection))
                continue;

            var target = Path.GetFullPath(Path.Combine(AppDataService.RootDirectory, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O backup contém um caminho inválido.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static bool ShouldInclude(string relativePath, BackupSelection selection)
    {
        if (IsAlwaysExcluded(relativePath))
            return false;

        var normalized = Normalize(relativePath);
        var firstSegment = normalized
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        var fileName = Path.GetFileName(normalized);

        if (firstSegment.Equals("custom-covers", StringComparison.OrdinalIgnoreCase))
            return selection.CustomArtwork;

        if (fileName.Equals("metadata.json", StringComparison.OrdinalIgnoreCase))
            return selection.MetadataCache;

        if (fileName.Equals("settings.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("settings.backup.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(".migration-", StringComparison.OrdinalIgnoreCase))
            return selection.SettingsAndLibrary;

        // Arquivos locais futuros não categorizados acompanham configurações/biblioteca
        // para que novos dados importantes não desapareçam silenciosamente do backup.
        return selection.SettingsAndLibrary;
    }

    private static bool IsAlwaysExcluded(string relativePath)
    {
        var normalized = Normalize(relativePath);
        var firstSegment = normalized
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return firstSegment is not null &&
               AlwaysExcludedFolders.Contains(firstSegment, StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string relativePath) =>
        relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
}

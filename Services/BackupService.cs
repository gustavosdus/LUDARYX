using System.IO.Compression;
using System.Text.Json;
using UnifiedGameLauncher.Models;

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
    private const string ManifestEntry = ".ludaryx-backup.json";
    private const string SettingsEntry = "settings/settings.json";
    private const string MetadataEntry = "metadata/metadata.json";
    private const string ArtworkStateEntry = "artwork/artwork-state.json";
    private const string ArtworkPrefix = "artwork/custom-covers/";

    private static readonly string[] AlwaysExcludedFolders = { "Logs", "Updates", "covers" };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static long GetEstimatedBackupSourceSizeBytes(BackupSelection? selection = null)
    {
        selection ??= new BackupSelection();
        AppDataService.EnsureMigrated();

        long total = 0;
        var root = AppDataService.RootDirectory;
        if (!Directory.Exists(root))
            return total;

        if (selection.SettingsAndLibrary)
            total += SafeLength(Path.Combine(root, "settings.json"));

        if (selection.MetadataCache)
            total += SafeLength(Path.Combine(root, "metadata.json"));

        if (selection.CustomArtwork)
        {
            var artworkRoot = Path.Combine(root, "custom-covers");
            if (Directory.Exists(artworkRoot))
            {
                foreach (var file in Directory.EnumerateFiles(artworkRoot, "*", SearchOption.AllDirectories))
                    total += SafeLength(file);
            }
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

        WriteJsonEntry(archive, ManifestEntry, new BackupManifest
        {
            FormatVersion = 2,
            CreatedAtUtc = DateTime.UtcNow,
            SettingsAndLibrary = selection.SettingsAndLibrary,
            CustomArtwork = selection.CustomArtwork,
            MetadataCache = selection.MetadataCache
        });

        if (selection.SettingsAndLibrary)
        {
            var settingsFile = Path.Combine(root, "settings.json");
            if (File.Exists(settingsFile))
                archive.CreateEntryFromFile(settingsFile, SettingsEntry, CompressionLevel.Optimal);
        }

        if (selection.MetadataCache)
        {
            var metadataFile = Path.Combine(root, "metadata.json");
            if (File.Exists(metadataFile))
                archive.CreateEntryFromFile(metadataFile, MetadataEntry, CompressionLevel.Optimal);
        }

        if (selection.CustomArtwork)
        {
            var artworkRoot = Path.Combine(root, "custom-covers");
            if (Directory.Exists(artworkRoot))
            {
                foreach (var file in Directory.EnumerateFiles(artworkRoot, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(artworkRoot, file)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    archive.CreateEntryFromFile(file, ArtworkPrefix + relative, CompressionLevel.Optimal);
                }
            }

            WriteJsonEntry(archive, ArtworkStateEntry, CaptureArtworkState(new JsonSettingsService().Load()));
        }
    }

    public static long GetEstimatedImportSizeBytes(string sourceZip, BackupSelection? selection = null)
    {
        selection ??= new BackupSelection();
        if (!File.Exists(sourceZip))
            return 0;

        using var archive = ZipFile.OpenRead(sourceZip);
        var manifest = ReadManifest(archive);
        if (manifest is null)
        {
            long legacyTotal = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || !ShouldIncludeLegacy(entry.FullName, selection))
                    continue;
                legacyTotal += Math.Max(0, entry.Length);
            }
            return legacyTotal;
        }

        var effective = Intersect(selection, manifest);
        long total = 0;

        foreach (var entry in archive.Entries)
        {
            if (effective.SettingsAndLibrary &&
                entry.FullName.Equals(SettingsEntry, StringComparison.OrdinalIgnoreCase))
                total += Math.Max(0, entry.Length);
            else if (effective.MetadataCache &&
                     entry.FullName.Equals(MetadataEntry, StringComparison.OrdinalIgnoreCase))
                total += Math.Max(0, entry.Length);
            else if (effective.CustomArtwork &&
                     entry.FullName.StartsWith(ArtworkPrefix, StringComparison.OrdinalIgnoreCase))
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

        using var archive = ZipFile.OpenRead(sourceZip);
        var manifest = ReadManifest(archive);

        if (manifest is null)
        {
            ImportLegacyArchive(archive, selection);
            return;
        }

        var effective = Intersect(selection, manifest);
        if (!effective.HasAny)
            throw new InvalidOperationException("O backup não contém nenhuma das categorias selecionadas.");

        ImportVersion2Archive(archive, effective);
    }

    private static void ImportVersion2Archive(ZipArchive archive, BackupSelection selection)
    {
        var root = AppDataService.RootDirectory;
        var settingsService = new JsonSettingsService();

        // Captura os vínculos atuais antes de substituir settings/metadata. Se artes
        // personalizadas não forem selecionadas, estes vínculos devem permanecer locais.
        var currentSettings = settingsService.Load();
        var currentArtworkState = CaptureArtworkState(currentSettings);
        var currentMetadataArtwork = CaptureMetadataArtwork(Path.Combine(root, "metadata.json"));

        if (selection.SettingsAndLibrary)
        {
            var entry = archive.GetEntry(SettingsEntry);
            if (entry is not null)
            {
                var settingsTarget = Path.Combine(root, "settings.json");
                ExtractEntryAtomically(entry, settingsTarget);

                // Evita que settings.backup.json antigo desfaça a restauração caso o
                // arquivo principal seja lido durante uma recuperação posterior.
                try { File.Copy(settingsTarget, Path.Combine(root, "settings.backup.json"), true); }
                catch { }
            }
        }

        if (selection.MetadataCache)
        {
            var entry = archive.GetEntry(MetadataEntry);
            if (entry is not null)
            {
                var metadataTarget = Path.Combine(root, "metadata.json");
                ExtractEntryAtomically(entry, metadataTarget);

                if (!selection.CustomArtwork)
                    RestoreMetadataArtwork(metadataTarget, currentMetadataArtwork);
            }
        }

        if (selection.CustomArtwork)
        {
            var artworkRoot = Path.Combine(root, "custom-covers");

            // "Restaurar artes" significa substituir somente esta categoria, e não
            // misturar silenciosamente arquivos atuais com os do backup.
            try
            {
                if (Directory.Exists(artworkRoot))
                    Directory.Delete(artworkRoot, recursive: true);
            }
            catch
            {
                // Se algum arquivo estiver em uso, os arquivos extraídos abaixo ainda
                // substituem os correspondentes possíveis sem afetar outras categorias.
            }
            Directory.CreateDirectory(artworkRoot);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) ||
                    !entry.FullName.StartsWith(ArtworkPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relative = entry.FullName[ArtworkPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
                var target = SafeCombineUnder(artworkRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        // Reaplica apenas os vínculos de arte correspondentes à seleção. Isso torna
        // "Configurações/biblioteca" e "Artes personalizadas" categorias independentes.
        var resultingSettings = settingsService.Load();
        if (selection.CustomArtwork)
        {
            var state = ReadJsonEntry<ArtworkBackupState>(archive, ArtworkStateEntry);
            if (state is not null)
                ApplyArtworkState(resultingSettings, state);
        }
        else if (selection.SettingsAndLibrary)
        {
            ApplyArtworkState(resultingSettings, currentArtworkState);
        }

        if (selection.SettingsAndLibrary || selection.CustomArtwork)
            settingsService.Save(resultingSettings);
    }

    private static void ImportLegacyArchive(ZipArchive archive, BackupSelection selection)
    {
        var root = Path.GetFullPath(AppDataService.RootDirectory) + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || !ShouldIncludeLegacy(entry.FullName, selection))
                continue;

            var target = Path.GetFullPath(Path.Combine(AppDataService.RootDirectory, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O backup contém um caminho inválido.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static BackupSelection Intersect(BackupSelection requested, BackupManifest manifest) => new()
    {
        SettingsAndLibrary = requested.SettingsAndLibrary && manifest.SettingsAndLibrary,
        CustomArtwork = requested.CustomArtwork && manifest.CustomArtwork,
        MetadataCache = requested.MetadataCache && manifest.MetadataCache
    };

    private static BackupManifest? ReadManifest(ZipArchive archive) =>
        ReadJsonEntry<BackupManifest>(archive, ManifestEntry);

    private static T? ReadJsonEntry<T>(ZipArchive archive, string entryName)
    {
        try
        {
            var entry = archive.GetEntry(entryName);
            if (entry is null)
                return default;

            using var stream = entry.Open();
            return JsonSerializer.Deserialize<T>(stream, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static void WriteJsonEntry<T>(ZipArchive archive, string entryName, T value)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, value, JsonOptions);
    }

    private static void ExtractEntryAtomically(ZipArchiveEntry entry, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".restore.tmp";

        using (var input = entry.Open())
        using (var output = File.Create(temp))
            input.CopyTo(output);

        File.Move(temp, target, overwrite: true);
    }

    private static string SafeCombineUnder(string root, string relative)
    {
        var normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O backup contém um caminho inválido.");
        return target;
    }

    private static ArtworkBackupState CaptureArtworkState(LauncherSettings settings)
    {
        var state = new ArtworkBackupState();

        foreach (var pair in settings.ManualMetadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Value.HorizontalCoverUrl) &&
                string.IsNullOrWhiteSpace(pair.Value.VerticalCoverUrl))
                continue;

            state.ManualMetadata[pair.Key] = new ArtworkOverride
            {
                Horizontal = pair.Value.HorizontalCoverUrl,
                Vertical = pair.Value.VerticalCoverUrl
            };
        }

        foreach (var game in settings.ManualGames)
        {
            if (string.IsNullOrWhiteSpace(game.CoverPath) && string.IsNullOrWhiteSpace(game.IconPath))
                continue;

            state.ManualGames[game.Id] = new ManualGameArtwork
            {
                CoverPath = game.CoverPath,
                IconPath = game.IconPath
            };
        }

        return state;
    }

    private static void ApplyArtworkState(LauncherSettings settings, ArtworkBackupState state)
    {
        foreach (var manual in settings.ManualMetadata.Values)
        {
            manual.HorizontalCoverUrl = null;
            manual.VerticalCoverUrl = null;
        }

        foreach (var pair in state.ManualMetadata)
        {
            if (!settings.ManualMetadata.TryGetValue(pair.Key, out var manual))
            {
                manual = new ManualGameMetadata();
                settings.ManualMetadata[pair.Key] = manual;
            }

            manual.HorizontalCoverUrl = pair.Value.Horizontal;
            manual.VerticalCoverUrl = pair.Value.Vertical;
        }

        foreach (var game in settings.ManualGames)
        {
            if (state.ManualGames.TryGetValue(game.Id, out var artwork))
            {
                game.CoverPath = artwork.CoverPath;
                game.IconPath = artwork.IconPath;
            }
            else
            {
                game.CoverPath = null;
                game.IconPath = null;
            }
        }
    }

    private static Dictionary<string, MetadataArtworkState> CaptureMetadataArtwork(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new(StringComparer.OrdinalIgnoreCase);

            var json = SafeFileReadService.ReadAllText(
                path,
                SafeFileReadService.MetadataCacheMaxBytes,
                "cache de metadados");

            var metadata = JsonSerializer.Deserialize<Dictionary<string, GameMetadata>>(json, JsonOptions)
                           ?? new(StringComparer.OrdinalIgnoreCase);

            return metadata.ToDictionary(
                pair => pair.Key,
                pair => new MetadataArtworkState
                {
                    CustomHorizontal = pair.Value.CustomHorizontalCoverLocalPath,
                    CustomVertical = pair.Value.CustomVerticalCoverLocalPath
                },
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void RestoreMetadataArtwork(
        string metadataPath,
        Dictionary<string, MetadataArtworkState> artwork)
    {
        try
        {
            var json = SafeFileReadService.ReadAllText(
                metadataPath,
                SafeFileReadService.MetadataCacheMaxBytes,
                "cache de metadados");

            var metadata = JsonSerializer.Deserialize<Dictionary<string, GameMetadata>>(json, JsonOptions)
                           ?? new(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in metadata)
            {
                if (artwork.TryGetValue(pair.Key, out var current))
                {
                    pair.Value.CustomHorizontalCoverLocalPath = current.CustomHorizontal;
                    pair.Value.CustomVerticalCoverLocalPath = current.CustomVertical;
                }
                else
                {
                    pair.Value.CustomHorizontalCoverLocalPath = null;
                    pair.Value.CustomVerticalCoverLocalPath = null;
                }
            }

            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions));
        }
        catch
        {
            // O cache é regenerável; falha ao preservar referências não deve impedir
            // a restauração das demais categorias.
        }
    }

    private static long SafeLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private static bool ShouldIncludeLegacy(string relativePath, BackupSelection selection)
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

    private sealed class BackupManifest
    {
        public int FormatVersion { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public bool SettingsAndLibrary { get; set; }
        public bool CustomArtwork { get; set; }
        public bool MetadataCache { get; set; }
    }

    private sealed class ArtworkBackupState
    {
        public Dictionary<string, ArtworkOverride> ManualMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ManualGameArtwork> ManualGames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ArtworkOverride
    {
        public string? Horizontal { get; set; }
        public string? Vertical { get; set; }
    }

    private sealed class ManualGameArtwork
    {
        public string? CoverPath { get; set; }
        public string? IconPath { get; set; }
    }

    private sealed class MetadataArtworkState
    {
        public string? CustomHorizontal { get; set; }
        public string? CustomVertical { get; set; }
    }
}

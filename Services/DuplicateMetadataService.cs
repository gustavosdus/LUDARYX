using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Mantém metadados textuais coerentes entre entradas que o LUDARYX já reconheceu
/// como versões duplicadas do mesmo jogo. Artes e dados de inicialização continuam
/// pertencendo a cada entrada individual.
/// </summary>
public static class DuplicateMetadataService
{
    public static bool Synchronize(
        IEnumerable<Game> games,
        LauncherSettings settings)
    {
        var changed = false;
        var groups = games
            .Where(game => game.IsDuplicate && !string.IsNullOrWhiteSpace(game.CanonicalGameId))
            .GroupBy(game => game.CanonicalGameId, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var entries = group.ToList();
            if (entries.Count < 2)
                continue;

            var source = ResolveSource(entries, settings);
            foreach (var target in entries)
            {
                if (ReferenceEquals(target, source))
                    continue;

                CopyTextMetadata(source.Metadata, target.Metadata);
                changed |= SynchronizeManualOverrides(source, target, settings);
            }
        }

        return changed;
    }

    private static Game ResolveSource(
        IReadOnlyCollection<Game> entries,
        LauncherSettings settings)
    {
        var canonicalId = entries.First().CanonicalGameId;
        if (settings.PreferredDuplicateProviders.TryGetValue(canonicalId, out var preferred))
        {
            var selected = entries.FirstOrDefault(game =>
                game.ProviderId.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
                return selected;
        }

        return entries
            .OrderByDescending(Score)
            .ThenBy(game => game.ProviderId, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int Score(Game game)
    {
        var metadata = game.Metadata;
        var score = 0;

        if (!string.IsNullOrWhiteSpace(metadata.Description)) score += 3;
        score += Math.Min(6, metadata.LocalizedDescriptions?.Count ?? 0) * 2;
        if (metadata.Genres.Count > 0) score += 2;
        if (!string.IsNullOrWhiteSpace(metadata.Developer)) score += 2;
        if (!string.IsNullOrWhiteSpace(metadata.Publisher)) score += 2;
        if (metadata.ReleaseYear.HasValue) score += 2;
        if (metadata.AgeRatings.Count > 0) score += 1;

        var source = metadata.Source ?? string.Empty;
        if (!source.Equals("Local", StringComparison.OrdinalIgnoreCase))
            score += 2;
        if (source.Contains("Steam Store", StringComparison.OrdinalIgnoreCase))
            score += 2;
        if (source.Contains("Wikidata", StringComparison.OrdinalIgnoreCase))
            score += 1;

        return score;
    }

    private static void CopyTextMetadata(GameMetadata source, GameMetadata target)
    {
        target.CanonicalName = source.CanonicalName;
        target.Description = source.Description;
        target.LocalizedDescriptions = new Dictionary<string, string>(
            source.LocalizedDescriptions ?? new(),
            StringComparer.OrdinalIgnoreCase);
        target.Genres = source.Genres.ToList();
        target.Platforms = source.Platforms.ToList();
        target.Developer = source.Developer;
        target.Publisher = source.Publisher;
        target.ReleaseYear = source.ReleaseYear;
        target.AgeRatings = new Dictionary<string, string>(
            source.AgeRatings ?? new(),
            StringComparer.OrdinalIgnoreCase);
        target.Source = source.Source;
        target.UpdatedAtUtc = source.UpdatedAtUtc;

        // ExternalId e todas as artes são intencionalmente preservados no target:
        // IDs podem ser específicos do provider e cada versão pode ter capa própria.
    }

    private static bool SynchronizeManualOverrides(
        Game source,
        Game target,
        LauncherSettings settings)
    {
        if (target.Platform != GamePlatform.Manual)
            return false;

        settings.ManualMetadata.TryGetValue(source.ProviderId, out var sourceManual);
        settings.ManualMetadata.TryGetValue(target.ProviderId, out var targetManual);

        // Entradas manuais duplicadas antigas podem conter um snapshot automático em
        // ManualMetadata. Mantemos apenas overrides que também existam na principal;
        // os demais campos voltam a seguir os metadados automáticos/localizados.
        targetManual ??= new ManualGameMetadata();

        var horizontal = targetManual.HorizontalCoverUrl;
        var vertical = targetManual.VerticalCoverUrl;
        var disableVertical = targetManual.DisableAutomaticSteamGridDbVertical;
        var disableHorizontal = targetManual.DisableAutomaticSteamGridDbHorizontal;

        targetManual.Name = sourceManual?.Name ?? source.Name;
        target.Name = targetManual.Name;
        targetManual.Description = sourceManual?.Description;
        targetManual.Genres = sourceManual?.Genres?.ToList() ?? new();
        targetManual.Developer = sourceManual?.Developer;
        targetManual.Publisher = sourceManual?.Publisher;
        targetManual.ReleaseYear = sourceManual?.ReleaseYear;
        targetManual.AgeRatings = new Dictionary<string, string>(
            sourceManual?.AgeRatings ?? new(),
            StringComparer.OrdinalIgnoreCase);

        targetManual.HorizontalCoverUrl = horizontal;
        targetManual.VerticalCoverUrl = vertical;
        targetManual.DisableAutomaticSteamGridDbVertical = disableVertical;
        targetManual.DisableAutomaticSteamGridDbHorizontal = disableHorizontal;

        settings.ManualMetadata[target.ProviderId] = targetManual;
        return true;
    }
}

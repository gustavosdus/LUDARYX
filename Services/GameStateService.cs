using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed class GameStateService
{
    private readonly JsonSettingsService _settingsService = new();

    public void Apply(Game game, LauncherSettings settings)
    {
        game.IsFavorite = settings.FavoriteGameIds.Contains(game.ProviderId, StringComparer.OrdinalIgnoreCase);
        game.IsHidden = settings.HiddenGameIds.Contains(game.ProviderId, StringComparer.OrdinalIgnoreCase);
        game.IsExcluded = settings.ExcludedGameIds.Contains(game.ProviderId, StringComparer.OrdinalIgnoreCase);
        if (settings.LastPlayedUtc.TryGetValue(game.ProviderId, out var last)) game.LastPlayedUtc = last;
        if (settings.PlayCounts.TryGetValue(game.ProviderId, out var count)) game.PlayCount = count;
        if (settings.ManualMetadata.TryGetValue(game.ProviderId, out var manual)) ApplyManualMetadata(game, manual);
    }

    public void MarkPlayed(Game game, LauncherSettings settings)
    {
        var id = game.ProviderId;
        settings.LastPlayedUtc[id] = DateTime.UtcNow;
        settings.PlayCounts[id] = settings.PlayCounts.TryGetValue(id, out var count) ? count + 1 : 1;
        game.LastPlayedUtc = settings.LastPlayedUtc[id];
        game.PlayCount = settings.PlayCounts[id];
        _settingsService.Save(settings);
    }

    public void ToggleFavorite(Game game, LauncherSettings settings)
    {
        var existing = settings.FavoriteGameIds.FirstOrDefault(x => x.Equals(game.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (existing == null) settings.FavoriteGameIds.Add(game.ProviderId);
        else settings.FavoriteGameIds.Remove(existing);
        game.IsFavorite = existing == null;
        _settingsService.Save(settings);
    }

    public void SaveManualMetadata(Game game, LauncherSettings settings, ManualGameMetadata manual)
    {
        if (settings.ManualMetadata.TryGetValue(game.ProviderId, out var existing))
        {
            manual.HorizontalCoverUrl = existing.HorizontalCoverUrl;
            manual.VerticalCoverUrl = existing.VerticalCoverUrl;
            manual.DisableAutomaticSteamGridDbVertical = existing.DisableAutomaticSteamGridDbVertical;
            manual.DisableAutomaticSteamGridDbHorizontal = existing.DisableAutomaticSteamGridDbHorizontal;
        }
        settings.ManualMetadata[game.ProviderId] = manual;
        ApplyManualMetadata(game, manual);
        _settingsService.Save(settings);
    }

    private static void ApplyManualMetadata(Game game, ManualGameMetadata manual)
    {
        if (!string.IsNullOrWhiteSpace(manual.Name)) game.Name = manual.Name.Trim();
        if (!string.IsNullOrWhiteSpace(manual.Description)) game.Metadata.Description = manual.Description;
        if (manual.Genres.Count > 0) game.Metadata.Genres = GenreService.NormalizeMany(manual.Genres).ToList();
        if (!string.IsNullOrWhiteSpace(manual.Developer)) game.Metadata.Developer = manual.Developer;
        if (!string.IsNullOrWhiteSpace(manual.Publisher)) game.Metadata.Publisher = manual.Publisher;
        if (manual.ReleaseYear.HasValue) game.Metadata.ReleaseYear = manual.ReleaseYear;
        if (!string.IsNullOrWhiteSpace(manual.HorizontalCoverUrl)) game.Metadata.CustomHorizontalCoverLocalPath = manual.HorizontalCoverUrl;
        if (!string.IsNullOrWhiteSpace(manual.VerticalCoverUrl)) game.Metadata.CustomVerticalCoverLocalPath = manual.VerticalCoverUrl;

        // Flags de bloqueio gravadas pelas revisões v117-v119 não removem mais
        // capas automáticas. Restaurar agora significa voltar à cadeia padrão de
        // arte (provedor -> SteamGridDB), e não manter a orientação vazia.
    }

    private static void ClearAutomaticSteamGridDbArtwork(GameMetadata metadata, bool vertical)
    {
        if (vertical)
        {
            if (IsSteamGridDbPath(metadata.VerticalCoverLocalPath))
            {
                if (string.Equals(metadata.CoverLocalPath, metadata.VerticalCoverLocalPath, StringComparison.OrdinalIgnoreCase))
                    metadata.CoverLocalPath = null;
                metadata.VerticalCoverLocalPath = null;
            }
            if (IsSteamGridDbUrl(metadata.VerticalCoverUrl)) metadata.VerticalCoverUrl = null;
        }
        else
        {
            if (IsSteamGridDbPath(metadata.HorizontalCoverLocalPath))
            {
                if (string.Equals(metadata.CoverLocalPath, metadata.HorizontalCoverLocalPath, StringComparison.OrdinalIgnoreCase))
                    metadata.CoverLocalPath = null;
                metadata.HorizontalCoverLocalPath = null;
            }
            if (IsSteamGridDbUrl(metadata.HorizontalCoverUrl)) metadata.HorizontalCoverUrl = null;
        }
    }

    private static bool IsSteamGridDbPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.GetFileName(path).Contains("steamgriddb", StringComparison.OrdinalIgnoreCase);

    private static bool IsSteamGridDbUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Host.Contains("steamgriddb", StringComparison.OrdinalIgnoreCase);
}


using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed partial class MetadataService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _localizedTextGates = new();
    private readonly ConcurrentDictionary<string, DateTime> _localizedTextAttempts = new();
    private sealed record LocalizedStoreText(string? Name, string? Description);

    public async Task<string?> EnsureLocalizedDescriptionAsync(
        Game game, LauncherSettings settings, CancellationToken token = default)
    {
        var language = NormalizeDescriptionLanguage(settings.Language);
        var requestKey = $"{game.ProviderId}:{language}";
        var gate = _localizedTextGates.GetOrAdd(game.ProviderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            LocalizedMetadataText.Prepare(game.Metadata);
            var metadata = game.Metadata;
            if (metadata.LocalizedDescriptions.ContainsKey(language) && metadata.LocalizedNames.ContainsKey(language))
                return GetDisplayDescription(game, settings);
            if (_localizedTextAttempts.TryGetValue(requestKey, out var lastAttempt) &&
                DateTime.UtcNow - lastAttempt < TimeSpan.FromMinutes(5))
                return GetDisplayDescription(game, settings);

            int? appId = game.Platform == GamePlatform.Steam && int.TryParse(game.Id, out var installedId)
                ? installedId
                : metadata.Source?.Contains("Steam Store", StringComparison.OrdinalIgnoreCase) == true &&
                  int.TryParse(metadata.ExternalId, out var relatedId) ? relatedId : null;

            // Fetch English independently. A fallback response must never be saved
            // under the requested locale, otherwise future refreshes never retry it.
            if (!metadata.LocalizedNames.ContainsKey("en-US") || !metadata.LocalizedDescriptions.ContainsKey("en-US"))
                await FetchAndStoreLocaleAsync(metadata, game.Name, appId, "en-US", token);
            if (!language.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                await FetchAndStoreLocaleAsync(metadata, game.Name, appId, language, token);

            // Metadata enrichment can replace the object while a request is running.
            if (!ReferenceEquals(game.Metadata, metadata))
                MergeLocalizedDescriptions(game.Metadata, metadata);
            if (settings.ManualMetadata.TryGetValue(game.ProviderId, out var manual))
            {
                foreach (var pair in manual.LocalizedNames ?? new())
                    game.Metadata.LocalizedNames[pair.Key] = pair.Value;
                foreach (var pair in manual.LocalizedDescriptions ?? new())
                    game.Metadata.LocalizedDescriptions[pair.Key] = pair.Value;
            }
            lock (_cacheSync)
                _cache[game.ProviderId] = game.Metadata;
            SaveCache();
            _localizedTextAttempts[requestKey] = DateTime.UtcNow;
            return GetDisplayDescription(game, settings);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task FetchAndStoreLocaleAsync(
        GameMetadata metadata, string lookupName, int? appId, string language, CancellationToken token)
    {
        var english = LocalizedMetadataText.Select(metadata.LocalizedDescriptions, "en-US");
        var text = appId.HasValue ? await FetchSteamTextAsync(appId.Value, language, token) : null;
        var name = text?.Name;
        var englishName = LocalizedMetadataText.Select(metadata.LocalizedNames, "en-US");
        if (!language.StartsWith("en", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(name, englishName, StringComparison.OrdinalIgnoreCase))
            name = null;
        var description = text?.Description;
        if (!IsRequestedDescription(description, language, english))
            description = null;

        if (string.IsNullOrWhiteSpace(name) || IsLowQualityDescription(description))
        {
            var wiki = await FetchLinkedWikipediaTextAsync(lookupName, language, token);
            name = string.IsNullOrWhiteSpace(name) ? wiki?.Name : name;
            description = IsLowQualityDescription(description) ? wiki?.Description : description;
        }
        if (IsLowQualityDescription(description))
            description = await GetLocalizedWikipediaDescriptionAsync(lookupName, language, token);

        if (!string.IsNullOrWhiteSpace(name))
            metadata.LocalizedNames[language] = name.Trim();
        if (IsRequestedDescription(description, language, english))
            metadata.LocalizedDescriptions[language] = SanitizeDescription(description)!;
    }

    private static bool IsRequestedDescription(string? value, string language, string? english)
    {
        if (IsLowQualityDescription(value))
            return false;
        var japaneseScript = Regex.IsMatch(value!, @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]");
        if (language == "ja-JP")
            return japaneseScript;
        if (japaneseScript && language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return false;
        return language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(SanitizeDescription(value), SanitizeDescription(english), StringComparison.Ordinal);
    }

    private async Task<LocalizedStoreText?> FetchSteamTextAsync(int appId, string language, CancellationToken token)
    {
        var (steamLanguage, country) = GetSteamLocale(language);
        await _steamStoreGate.WaitAsync(token);
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l={steamLanguage}&cc={country}";
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
                return null;
            using var document = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(
                response, maxBytes: 2 * 1024 * 1024, cancellationToken: token));
            if (!document.RootElement.TryGetProperty(appId.ToString(CultureInfo.InvariantCulture), out var entry) ||
                !entry.TryGetProperty("success", out var success) || !success.GetBoolean() ||
                !entry.TryGetProperty("data", out var data))
                return null;
            return new LocalizedStoreText(
                data.TryGetProperty("name", out var name) ? name.GetString() : null,
                data.TryGetProperty("short_description", out var description) ? SanitizeDescription(description.GetString()) : null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return null; }
        finally { _steamStoreGate.Release(); }
    }

    private async Task<LocalizedStoreText?> FetchLinkedWikipediaTextAsync(string name, string language, CancellationToken token)
    {
        var wikiLanguage = GetWikipediaLanguage(language);
        // Resolve translations through a language link on the same verified game
        // article. Do not guess that a similarly named Japanese page is the game.
        foreach (var candidate in new[] { name, $"{name} (video game)" })
        {
            try
            {
                var url = "https://en.wikipedia.org/w/api.php?action=query&prop=extracts%7Clanglinks" +
                          $"&exintro=1&explaintext=1&redirects=1&lllang={wikiLanguage}&format=json" +
                          $"&titles={Uri.EscapeDataString(candidate)}";
                using var response = await _http.GetAsync(url, token);
                if (!response.IsSuccessStatusCode)
                    continue;
                using var document = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(
                    response, maxBytes: 1024 * 1024, cancellationToken: token));
                if (!document.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages))
                    continue;
                foreach (var page in pages.EnumerateObject())
                {
                    var data = page.Value;
                    if (!data.TryGetProperty("extract", out var extract) ||
                        !LooksLikeVideoGameSummary(extract.GetString(), "en"))
                        continue;
                    if (wikiLanguage == "en" && data.TryGetProperty("title", out var title))
                        return new LocalizedStoreText(RemoveArticleQualifier(title.GetString()), SanitizeDescription(extract.GetString()));
                    if (!data.TryGetProperty("langlinks", out var links))
                        continue;
                    foreach (var link in links.EnumerateArray())
                    {
                        if (!link.TryGetProperty("*", out var linkedTitle))
                            continue;
                        var localizedTitle = linkedTitle.GetString();
                        var summary = await GetWikipediaSummaryAsync(localizedTitle!, token, wikiLanguage);
                        if (!IsLowQualityDescription(summary))
                            return new LocalizedStoreText(RemoveArticleQualifier(localizedTitle), SanitizeDescription(summary));
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { }
        }
        return null;
    }

    private static string? RemoveArticleQualifier(string? title) => title is null ? null :
        Regex.Replace(title, @"\s*\((?:video game|jogo eletrônico|videojogo|videojuego|ゲーム|コンピュータゲーム)(?:,?\s*\d{4})?\)\s*$", "", RegexOptions.IgnoreCase);
}

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Consulta metadados estruturados do PCGamingWiki pela API MediaWiki.
/// Não usa scraping de páginas HTML. Mantém cache em memória e limita o ritmo
/// das requisições para respeitar a infraestrutura comunitária do serviço.
/// </summary>
public sealed class PcGamingWikiService
{
    private const string ApiUrl = "https://www.pcgamingwiki.com/w/api.php";
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(1100);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Dictionary<string, GameMetadata?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public PcGamingWikiService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "LUDARYX/1.0.1 (PC game library metadata client; MediaWiki API) .NET/8.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    public async Task<GameMetadata?> GetMetadataByExactTitleAsync(string gameName, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(gameName))
            return null;

        var normalized = NormalizeTitle(gameName);
        lock (_cache)
        {
            if (_cache.TryGetValue(normalized, out var cached))
                return Clone(cached);
        }

        try
        {
            var pageTitle = await FindExactPageTitleAsync(gameName, token);
            if (string.IsNullOrWhiteSpace(pageTitle))
            {
                Cache(normalized, null);
                return null;
            }

            var wikitext = await GetPageWikitextAsync(pageTitle, token);
            if (string.IsNullOrWhiteSpace(wikitext))
            {
                Cache(normalized, null);
                return null;
            }

            var metadata = ParseInfobox(pageTitle, wikitext);
            GenreService.NormalizeInPlace(metadata);
            Cache(normalized, metadata);
            return Clone(metadata);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            Cache(normalized, null);
            return null;
        }
    }

    private async Task<string?> FindExactPageTitleAsync(string gameName, CancellationToken token)
    {
        // Primeiro tenta abrir diretamente o título. MediaWiki resolve redirects.
        var directUrl = ApiUrl + "?action=query&redirects=1&format=json&formatversion=2&titles=" +
                        Uri.EscapeDataString(gameName);
        using (var direct = await SendAsync(directUrl, token))
        {
            if (direct?.IsSuccessStatusCode == true)
            {
                using var doc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(direct, cancellationToken: token));
                if (doc.RootElement.TryGetProperty("query", out var query) &&
                    query.TryGetProperty("pages", out var pages) &&
                    pages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var page in pages.EnumerateArray())
                    {
                        if (page.TryGetProperty("missing", out _))
                            continue;
                        if (!page.TryGetProperty("title", out var titleElement))
                            continue;

                        var title = titleElement.GetString();
                        if (TitlesMatch(gameName, title))
                            return title;
                    }
                }
            }
        }

        // Fallback de pesquisa. Só aceita título exato normalizado.
        var searchUrl = ApiUrl + "?action=query&list=search&format=json&formatversion=2&srlimit=8&srnamespace=0&srsearch=" +
                        Uri.EscapeDataString(gameName);
        using var response = await SendAsync(searchUrl, token);
        if (response?.IsSuccessStatusCode != true)
            return null;

        using var searchDoc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(response, cancellationToken: token));
        if (!searchDoc.RootElement.TryGetProperty("query", out var searchQuery) ||
            !searchQuery.TryGetProperty("search", out var results) ||
            results.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("title", out var titleElement))
                continue;
            var title = titleElement.GetString();
            if (TitlesMatch(gameName, title))
                return title;
        }

        return null;
    }

    private async Task<string?> GetPageWikitextAsync(string pageTitle, CancellationToken token)
    {
        var url = ApiUrl + "?action=query&prop=revisions&rvprop=content&rvslots=main&format=json&formatversion=2&titles=" +
                  Uri.EscapeDataString(pageTitle);
        using var response = await SendAsync(url, token);
        if (response?.IsSuccessStatusCode != true)
            return null;

        using var doc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(response, cancellationToken: token));
        if (!doc.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("pages", out var pages) ||
            pages.ValueKind != JsonValueKind.Array)
            return null;

        var page = pages.EnumerateArray().FirstOrDefault();
        if (page.ValueKind == JsonValueKind.Undefined ||
            !page.TryGetProperty("revisions", out var revisions) ||
            revisions.ValueKind != JsonValueKind.Array)
            return null;

        var revision = revisions.EnumerateArray().FirstOrDefault();
        if (revision.ValueKind == JsonValueKind.Undefined ||
            !revision.TryGetProperty("slots", out var slots) ||
            !slots.TryGetProperty("main", out var main) ||
            !main.TryGetProperty("content", out var content))
            return null;

        return content.GetString();
    }

    private static GameMetadata ParseInfobox(string pageTitle, string wikitext)
    {
        var metadata = new GameMetadata
        {
            CanonicalName = pageTitle,
            Source = "PCGamingWiki"
        };

        metadata.Developer = JoinDistinct(ExtractTemplateFirstArguments(
            GetSection(wikitext, "developers", "publishers"),
            "Infobox game/row/developer"));

        metadata.Publisher = JoinDistinct(ExtractTemplateFirstArguments(
            GetSection(wikitext, "publishers", "engines"),
            "Infobox game/row/publisher"));

        var releaseSection = GetSection(wikitext, "release dates", "reception", "taxonomy");
        metadata.ReleaseYear = ExtractReleaseYear(releaseSection);

        var genreMatches = Regex.Matches(
            wikitext,
            @"\{\{\s*Infobox\s+game/row/taxonomy/genres\s*\|\s*([^}|]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (Match match in genreMatches)
        {
            var genre = CleanWikiValue(match.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(genre))
                metadata.Genres.Add(genre);
        }

        // Também lê o campo steam appid quando disponível; útil para uma etapa posterior
        // de enriquecimento pela Steam sem depender de pesquisa textual.
        var steamId = Regex.Match(
            wikitext,
            @"^\s*\|\s*steam\s+appid\s*=\s*([^\r\n|}]+)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
        if (steamId.Success)
        {
            var id = Regex.Match(steamId.Groups[1].Value, @"\d+");
            if (id.Success)
                metadata.ExternalId = id.Value;
        }

        return metadata;
    }

    private static string GetSection(string wikitext, string startField, params string[] endFields)
    {
        var start = Regex.Match(
            wikitext,
            @"^\s*\|\s*" + Regex.Escape(startField) + @"\s*=",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
        if (!start.Success)
            return string.Empty;

        var end = wikitext.Length;
        foreach (var endField in endFields)
        {
            var match = Regex.Match(
                wikitext[(start.Index + start.Length)..],
                @"^\s*\|\s*" + Regex.Escape(endField) + @"\s*=",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
            if (match.Success)
                end = Math.Min(end, start.Index + start.Length + match.Index);
        }

        return wikitext[start.Index..end];
    }

    private static IEnumerable<string> ExtractTemplateFirstArguments(string section, string templateName)
    {
        if (string.IsNullOrWhiteSpace(section))
            yield break;

        var pattern = @"\{\{\s*" + Regex.Escape(templateName) + @"\s*\|\s*([^}|]+)";
        foreach (Match match in Regex.Matches(section, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var value = CleanWikiValue(match.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }
    }

    private static int? ExtractReleaseYear(string section)
    {
        if (string.IsNullOrWhiteSpace(section))
            return null;

        // Prefere Windows; se não existir, usa a primeira data reconhecível de PC.
        var matches = Regex.Matches(
            section,
            @"\{\{\s*Infobox\s+game/row/date\s*\|\s*([^|}]+)\|\s*([^|}]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        Match? preferred = null;
        foreach (Match match in matches)
        {
            var platform = CleanWikiValue(match.Groups[1].Value);
            if (platform.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                preferred = match;
                break;
            }

            preferred ??= match;
        }

        if (preferred is null)
            return null;

        var dateText = CleanWikiValue(preferred.Groups[2].Value);
        var yearMatch = Regex.Match(dateText, @"\b(19|20)\d{2}\b");
        if (yearMatch.Success && int.TryParse(yearMatch.Value, CultureInfo.InvariantCulture, out var year))
            return year;

        return null;
    }

    private async Task<HttpResponseMessage?> SendAsync(string url, CancellationToken token)
    {
        await _requestGate.WaitAsync(token);
        try
        {
            var wait = MinimumRequestInterval - (DateTime.UtcNow - _lastRequestUtc);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, token);

            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            _lastRequestUtc = DateTime.UtcNow;
            return response;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private void Cache(string key, GameMetadata? metadata)
    {
        lock (_cache)
            _cache[key] = Clone(metadata);
    }

    private static GameMetadata? Clone(GameMetadata? source)
    {
        if (source is null)
            return null;

        return new GameMetadata
        {
            CanonicalName = source.CanonicalName,
            Description = source.Description,
            Genres = source.Genres.ToList(),
            Platforms = source.Platforms.ToList(),
            Developer = source.Developer,
            Publisher = source.Publisher,
            ReleaseYear = source.ReleaseYear,
            CoverUrl = source.CoverUrl,
            CoverLocalPath = source.CoverLocalPath,
            HorizontalCoverUrl = source.HorizontalCoverUrl,
            HorizontalCoverLocalPath = source.HorizontalCoverLocalPath,
            VerticalCoverUrl = source.VerticalCoverUrl,
            VerticalCoverLocalPath = source.VerticalCoverLocalPath,
            CustomHorizontalCoverLocalPath = source.CustomHorizontalCoverLocalPath,
            CustomVerticalCoverLocalPath = source.CustomVerticalCoverLocalPath,
            Source = source.Source,
            ExternalId = source.ExternalId,
            UpdatedAtUtc = source.UpdatedAtUtc
        };
    }

    private static string JoinDistinct(IEnumerable<string> values)
    {
        var list = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count == 0 ? string.Empty : string.Join(" / ", list);
    }

    private static string CleanWikiValue(string value)
    {
        var text = value.Trim();
        text = Regex.Replace(text, @"<ref[^>]*>[\s\S]*?</ref>|<ref[^>]*/>", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[\[([^]|]+)\|([^]]+)\]\]", "$2");
        text = Regex.Replace(text, @"\[\[([^]]+)\]\]", "$1");
        text = Regex.Replace(text, @"\{\{[^{}]*\}\}", string.Empty);
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static bool TitlesMatch(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        NormalizeTitle(left) == NormalizeTitle(right);

    private static string NormalizeTitle(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

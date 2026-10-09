using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Consulta a base oficial de dados abertos da Classificação Indicativa (MJSP).
/// O recurso é descoberto pelo catálogo CKAN para evitar depender de nomes de
/// arquivo datados. O resultado é armazenado localmente para reduzir requisições.
/// </summary>
public sealed class ClassIndOpenDataService
{
    private const string PackageApi =
        "https://dados.mj.gov.br/api/3/action/package_show?id=classind-sistema-gerencial-da-classificacao-indicativa";

    // Fallback apenas para indisponibilidade temporária do endpoint CKAN.
    // A descoberta dinâmica acima continua sendo a rota principal.
    private const string CurrentCsvFallback =
        "https://dados.mj.gov.br/dataset/5138a6ca-8009-4ffb-b95a-052f76d62a33/resource/a59c1601-12d3-4e6d-a1d0-b036632fe00e/download/jogoeletronico202511141500.csv";

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(30);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(18) };
    private readonly string _cacheFile;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<ClassIndEntry>? _entries;
    private DateTime _loadedAtUtc;

    public ClassIndOpenDataService()
    {
        AppDataService.EnsureMigrated();
        _cacheFile = Path.Combine(AppDataService.RootDirectory, "classind-open-data-v5.json");

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "LUDARYX/1.2.0 (+https://github.com/gustavosdus/LUDARYX)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/csv,text/plain;q=0.9,*/*;q=0.8");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9,en;q=0.7");
    }

    public async Task<string?> GetRatingAsync(
        string? gameName,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(gameName))
            return null;

        var entries = await GetEntriesAsync(token);
        if (entries.Count == 0)
            return null;

        var normalized = NormalizeTitle(gameName);
        if (normalized.Length == 0)
            return null;

        // 1) Correspondência exata continua sendo a regra principal.
        var match = entries.FirstOrDefault(entry =>
            NormalizeTitle(entry.TitleBrazil) == normalized ||
            NormalizeTitle(entry.TitleSeries) == normalized);

        // 2) A base oficial às vezes acrescenta subtítulo/plataforma ao título
        // (ex.: "... Ultimate All-Stars Wii" ou "... Starring Mickey Mouse").
        // Aceita apenas uma correspondência parcial forte e inequívoca para não
        // associar classificações entre jogos diferentes da mesma série.
        match ??= FindStrongUniquePartialMatch(entries, normalized);

        if (match is null)
            return null;

        return NormalizeRating(match.Rating);
    }

    private async Task<IReadOnlyList<ClassIndEntry>> GetEntriesAsync(CancellationToken token)
    {
        if (_entries is not null && DateTime.UtcNow - _loadedAtUtc < CacheLifetime)
            return _entries;

        await _gate.WaitAsync(token);
        try
        {
            if (_entries is not null && DateTime.UtcNow - _loadedAtUtc < CacheLifetime)
                return _entries;

            if (TryLoadDiskCache(out var diskEntries, out var cachedAtUtc) &&
                DateTime.UtcNow - cachedAtUtc < CacheLifetime)
            {
                _entries = diskEntries;
                _loadedAtUtc = cachedAtUtc;
                return _entries;
            }

            try
            {
                var resourceUrl = await ResolveCurrentCsvUrlAsync(token)
                    ?? CurrentCsvFallback;

                var fresh = await DownloadEntriesAsync(resourceUrl, token);
                if (fresh.Count > 0)
                {
                    _entries = fresh;
                    _loadedAtUtc = DateTime.UtcNow;
                    SaveDiskCache(_entries, _loadedAtUtc);
                    return _entries;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Usa cache antigo abaixo se o portal estiver temporariamente indisponível.
            }

            if (TryLoadDiskCache(out diskEntries, out cachedAtUtc))
            {
                _entries = diskEntries;
                _loadedAtUtc = cachedAtUtc;
                return _entries;
            }

            _entries = new();
            _loadedAtUtc = DateTime.UtcNow;
            return _entries;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> ResolveCurrentCsvUrlAsync(CancellationToken token)
    {
        using var response = await _http.GetAsync(PackageApi, token);
        if (!response.IsSuccessStatusCode)
            return null;

        using var doc = JsonDocument.Parse(
            await SafeHttpResponseService.ReadTextAsync(
                response,
                maxBytes: 2 * 1024 * 1024,
                cancellationToken: token));

        if (!doc.RootElement.TryGetProperty("success", out var success) ||
            !success.GetBoolean() ||
            !doc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("resources", out var resources) ||
            resources.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var resource in resources.EnumerateArray())
        {
            var name = resource.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            var format = resource.TryGetProperty("format", out var formatElement)
                ? formatElement.GetString()
                : null;
            var url = resource.TryGetProperty("url", out var urlElement)
                ? urlElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(url) ||
                !string.Equals(format, "CSV", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedName = NormalizeHeader(name);
            if (normalizedName.Contains("listajogosdadosabertos", StringComparison.Ordinal) ||
                normalizedName.Contains("jogoseletronicos", StringComparison.Ordinal))
            {
                return url;
            }
        }

        return null;
    }

    private async Task<List<ClassIndEntry>> DownloadEntriesAsync(
        string url,
        CancellationToken token)
    {
        using var response = await _http.GetAsync(url, token);
        if (!response.IsSuccessStatusCode)
            return new();

        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        if (bytes.Length == 0)
            return new();

        var text = DecodeCsv(bytes);
        return ParseEntries(text);
    }

    private static string DecodeCsv(byte[] bytes)
    {
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // Alguns exports governamentais/Excel chegam como UTF-16 sem BOM.
        // A presença regular de NULs permite detectar isso sem depender do header HTTP.
        var evenNulls = 0;
        var oddNulls = 0;
        var sampleLength = Math.Min(bytes.Length, 512);
        for (var i = 0; i < sampleLength; i++)
        {
            if (bytes[i] != 0)
                continue;

            if ((i & 1) == 0) evenNulls++;
            else oddNulls++;
        }

        if (oddNulls > sampleLength / 8)
            return Encoding.Unicode.GetString(bytes);
        if (evenNulls > sampleLength / 8)
            return Encoding.BigEndianUnicode.GetString(bytes);

        var utf8 = Encoding.UTF8.GetString(bytes);
        if (!utf8.Contains('�'))
            return utf8;

        return Encoding.Latin1.GetString(bytes);
    }

    private static List<ClassIndEntry> ParseEntries(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count < 2)
            return new();

        // Alguns exports CSV incluem uma linha "sep=;" antes do cabeçalho.
        // Localiza o cabeçalho real em vez de assumir que ele sempre é rows[0].
        var headerRowIndex = rows
            .Take(Math.Min(10, rows.Count))
            .Select((row, index) => new
            {
                Index = index,
                Headers = row
                    .Select((value, column) => new HeaderEntry(column, NormalizeHeader(value)))
                    .ToList()
            })
            .FirstOrDefault(candidate =>
                FindHeader(candidate.Headers, "titulonobrasil", "titulobrasil", "titulonacional") >= 0);

        if (headerRowIndex is null)
            return new();

        var headers = headerRowIndex.Headers;
        var headerColumnCount = rows[headerRowIndex.Index].Count;

        // Esquema oficial de "DADOS DOS JOGOS ELETRÔNICOS" do ClassInd:
        // B = Título no Brasil; C = Título da Série; M = Classificação atribuída.
        // Usa os nomes primeiro e as posições oficiais como fallback, porque o portal
        // já alterou acentos/capitalização de cabeçalhos entre exportações.
        var titleBrazilIndex = FindHeader(headers,
            "titulonobrasil", "titulobrasil", "titulonacional");
        if (titleBrazilIndex < 0 && headerColumnCount > 1)
            titleBrazilIndex = 1;

        var titleSeriesIndex = FindHeader(headers,
            "titulodaserie", "tituloserie", "serie");
        if (titleSeriesIndex < 0 && headerColumnCount > 2)
            titleSeriesIndex = 2;

        var ratingIndex = FindHeader(headers,
            "classificacaoatribuida",
            "classificacaoindicativaatribuida",
            "classificacaoindicativa",
            "classificacao",
            "faixaetaria",
            "indicacaoetaria");
        if (ratingIndex < 0 && headerColumnCount > 12)
            ratingIndex = 12;

        if (titleBrazilIndex < 0 && titleSeriesIndex < 0)
            return new();
        if (ratingIndex < 0)
            return new();

        var entries = new List<ClassIndEntry>(Math.Max(0, rows.Count - headerRowIndex.Index - 1));

        foreach (var row in rows.Skip(headerRowIndex.Index + 1))
        {
            var titleBrazil = GetCell(row, titleBrazilIndex);
            var titleSeries = GetCell(row, titleSeriesIndex);
            var rating = GetCell(row, ratingIndex);

            if ((string.IsNullOrWhiteSpace(titleBrazil) &&
                 string.IsNullOrWhiteSpace(titleSeries)) ||
                string.IsNullOrWhiteSpace(rating))
            {
                continue;
            }

            entries.Add(new ClassIndEntry(
                titleBrazil?.Trim() ?? string.Empty,
                titleSeries?.Trim() ?? string.Empty,
                rating.Trim()));
        }

        return entries;
    }

    private static List<List<string>> ParseCsv(string csv)
    {
        var delimiter = DetectDelimiter(csv);

        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];

            if (ch == '"')
            {
                if (quoted && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (!quoted && ch == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (!quoted && (ch == '\r' || ch == '\n'))
            {
                if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                    i++;

                row.Add(field.ToString());
                field.Clear();

                if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
                    rows.Add(row);

                row = new List<string>();
                continue;
            }

            field.Append(ch);
        }

        row.Add(field.ToString());
        if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
            rows.Add(row);

        return rows;
    }

    private static char DetectDelimiter(string csv)
    {
        var candidates = new[] { ';', ',', '\t' };
        var lines = csv
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(12)
            .ToList();

        // Prefere o delimitador que revela os cabeçalhos oficiais. Isso é mais
        // confiável do que olhar apenas a primeira linha, que pode ser "sep=;"
        // ou conter metadados exportados pelo portal.
        foreach (var candidate in candidates)
        {
            foreach (var line in lines)
            {
                var fields = SplitHeaderLine(line, candidate);
                var normalized = fields.Select(NormalizeHeader).ToList();
                if (normalized.Any(value =>
                        value is "titulonobrasil" or "titulobrasil" or "titulonacional") &&
                    normalized.Any(value =>
                        value is "classificacaoatribuida" or
                                 "classificacaoindicativaatribuida" or
                                 "classificacaoindicativa" or
                                 "classificacao" or
                                 "faixaetaria" or
                                 "indicacaoetaria"))
                {
                    return candidate;
                }
            }
        }

        return candidates
            .OrderByDescending(candidate =>
                lines.Take(5).Sum(line => CountOutsideQuotes(line, candidate)))
            .First();
    }

    private static List<string> SplitHeaderLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (!quoted && ch == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }

            field.Append(ch);
        }

        fields.Add(field.ToString());
        return fields;
    }

    private static int CountOutsideQuotes(string line, char value)
    {
        var count = 0;
        var quoted = false;

        foreach (var ch in line)
        {
            if (ch == '"')
                quoted = !quoted;
            else if (!quoted && ch == value)
                count++;
        }

        return count;
    }

    private static int FindHeader(
        IEnumerable<HeaderEntry> headers,
        params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            foreach (var header in headers)
            {
                if (string.Equals(header.Name, candidate, StringComparison.Ordinal))
                    return header.Index;
            }
        }

        return -1;
    }

    private static string? GetCell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] : null;

    private static string NormalizeRating(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = WebUtility.HtmlDecode(value)
            .Trim()
            .ToUpperInvariant();

        if (text.Contains("LIVRE", StringComparison.Ordinal))
            return "L";

        var match = Regex.Match(text, @"\b(10|12|14|16|18)\b");
        return match.Success ? match.Value : value.Trim();
    }

    private static string NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return RemoveDiacritics(value)
            .Where(char.IsLetterOrDigit)
            .Aggregate(new StringBuilder(), (builder, ch) => builder.Append(char.ToLowerInvariant(ch)))
            .ToString();
    }

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = RemoveDiacritics(WebUtility.HtmlDecode(value)).ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\([^)]*\)", " ");
        normalized = Regex.Replace(normalized, @"[^a-z0-9]+", string.Empty);
        return normalized;
    }

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private bool TryLoadDiskCache(
        out List<ClassIndEntry> entries,
        out DateTime cachedAtUtc)
    {
        entries = new();
        cachedAtUtc = DateTime.MinValue;

        try
        {
            if (!File.Exists(_cacheFile))
                return false;

            var cache = JsonSerializer.Deserialize<ClassIndCache>(
                File.ReadAllText(_cacheFile));
            if (cache?.Entries is null || cache.Entries.Count == 0)
                return false;

            entries = cache.Entries;
            cachedAtUtc = cache.UpdatedAtUtc;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SaveDiskCache(
        List<ClassIndEntry> entries,
        DateTime updatedAtUtc)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            File.WriteAllText(
                _cacheFile,
                JsonSerializer.Serialize(
                    new ClassIndCache(updatedAtUtc, entries),
                    new JsonSerializerOptions { WriteIndented = false }));
        }
        catch
        {
            // Cache local não é obrigatório para o funcionamento.
        }
    }

    private sealed record HeaderEntry(int Index, string Name);

    private sealed record ClassIndEntry(
        string TitleBrazil,
        string TitleSeries,
        string Rating);

    private sealed record ClassIndCache(
        DateTime UpdatedAtUtc,
        List<ClassIndEntry> Entries);
}

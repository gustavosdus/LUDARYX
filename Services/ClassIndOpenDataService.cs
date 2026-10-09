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
    private const string ResourceId =
        "a59c1601-12d3-4e6d-a1d0-b036632fe00e";

    private const string ResourceApi =
        "https://dados.mj.gov.br/api/3/action/resource_show?id=" + ResourceId;

    private const string PackageApi =
        "https://dados.mj.gov.br/api/3/action/package_show?id=5138a6ca-8009-4ffb-b95a-052f76d62a33";

    // Fallback apenas para indisponibilidade temporária do endpoint CKAN.
    // A descoberta dinâmica acima continua sendo a rota principal.
    private const string CurrentCsvFallback =
        "https://dados.mj.gov.br/dataset/5138a6ca-8009-4ffb-b95a-052f76d62a33/resource/a59c1601-12d3-4e6d-a1d0-b036632fe00e/download/jogoeletronico202511141500.csv";

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(30);

    private readonly HttpClient _http = new(
        new HttpClientHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate |
                DecompressionMethods.Brotli
        })
    {
        Timeout = TimeSpan.FromSeconds(18)
    };
    private readonly string _cacheFile;
    private readonly string _bundledCsvPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<ClassIndEntry>? _entries;
    private DateTime _loadedAtUtc;
    private DateTime _networkUnavailableUntilUtc;

    public ClassIndOpenDataService()
    {
        AppDataService.EnsureMigrated();
        _cacheFile = Path.Combine(AppDataService.RootDirectory, "classind-open-data-v9.json");
        _bundledCsvPath = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "ClassInd",
            "ListaJogosDadosAbertos.csv");

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "LUDARYX/1.2.0 (+https://github.com/gustavosdus/LUDARYX)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/csv,text/plain;q=0.9,*/*;q=0.8");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9,en;q=0.7");
    }

    public async Task<string?> GetRatingAsync(
        string? gameName,
        CancellationToken token = default,
        bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(gameName))
            return null;

        var entries = await GetEntriesAsync(token, forceRefresh);
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
        {
            DiagnosticLogService.LogInfo(
                $"ClassInd lookup: no title match among {entries.Count} parsed records.");
            return null;
        }

        var normalizedRating = NormalizeRating(match.Rating);
        DiagnosticLogService.LogInfo(
            $"ClassInd lookup: match found; rating={(string.IsNullOrWhiteSpace(normalizedRating) ? "empty" : "present")}.");
        return normalizedRating;
    }

    private static ClassIndEntry? FindStrongUniquePartialMatch(
        IReadOnlyList<ClassIndEntry> entries,
        string normalizedGameTitle)
    {
        if (normalizedGameTitle.Length < 8)
            return null;

        var candidates = entries
            .Select(entry => new
            {
                Entry = entry,
                Brazil = NormalizeTitle(entry.TitleBrazil),
                Series = NormalizeTitle(entry.TitleSeries)
            })
            .Where(candidate =>
                IsStrongPartialTitleMatch(normalizedGameTitle, candidate.Brazil) ||
                IsStrongPartialTitleMatch(normalizedGameTitle, candidate.Series))
            .Select(candidate => candidate.Entry)
            .ToList();

        if (candidates.Count == 0)
            return null;

        // O mesmo jogo pode aparecer mais de uma vez na base oficial por plataforma,
        // mídia ou novo requerimento. Várias linhas não tornam o título ambíguo quando
        // todas elas chegam à mesma classificação atribuída.
        var distinctRatings = candidates
            .Select(candidate => NormalizeRating(candidate.Rating))
            .Where(rating => !string.IsNullOrWhiteSpace(rating))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctRatings.Count != 1)
            return null;

        return candidates
            .OrderByDescending(candidate =>
                Math.Max(
                    CommonTitleLength(normalizedGameTitle, NormalizeTitle(candidate.TitleBrazil)),
                    CommonTitleLength(normalizedGameTitle, NormalizeTitle(candidate.TitleSeries))))
            .FirstOrDefault();
    }

    private static int CommonTitleLength(string requested, string official)
    {
        if (string.IsNullOrEmpty(requested) || string.IsNullOrEmpty(official))
            return 0;

        if (requested.Contains(official, StringComparison.Ordinal))
            return official.Length;
        if (official.Contains(requested, StringComparison.Ordinal))
            return requested.Length;

        return 0;
    }

    private static bool IsStrongPartialTitleMatch(string requested, string official)
    {
        if (requested.Length < 8 || official.Length < 8)
            return false;

        var shorter = requested.Length <= official.Length ? requested : official;
        var longer = requested.Length > official.Length ? requested : official;

        if (!longer.Contains(shorter, StringComparison.Ordinal))
            return false;

        // Casos como "... Ultimate All-Stars Wii" normalmente mantêm quase todo
        // o título do jogo cadastrado no LUDARYX.
        var ratio = (double)shorter.Length / longer.Length;
        if (ratio >= 0.60)
            return true;

        // Também cobre nomes oficiais com subtítulo descritivo longo, como
        // "Castle of Illusion Starring Mickey Mouse". A exigência de título
        // suficientemente longo + candidato único reduz colisões entre jogos.
        return shorter.Length >= 12 &&
               longer.StartsWith(shorter, StringComparison.Ordinal);
    }

    private async Task<IReadOnlyList<ClassIndEntry>> GetEntriesAsync(
        CancellationToken token,
        bool forceRefresh)
    {
        if (!forceRefresh &&
            _entries is { Count: > 0 } &&
            DateTime.UtcNow - _loadedAtUtc < CacheLifetime)
        {
            return _entries;
        }

        await _gate.WaitAsync(token);
        try
        {
            if (!forceRefresh &&
                _entries is { Count: > 0 } &&
                DateTime.UtcNow - _loadedAtUtc < CacheLifetime)
            {
                return _entries;
            }

            if (!forceRefresh &&
                TryLoadDiskCache(out var diskEntries, out var cachedAtUtc) &&
                DateTime.UtcNow - cachedAtUtc < CacheLifetime)
            {
                _entries = diskEntries;
                _loadedAtUtc = cachedAtUtc;
                return _entries;
            }

            string? resourceUrl = null;
            if (!forceRefresh && DateTime.UtcNow < _networkUnavailableUntilUtc)
            {
                DiagnosticLogService.LogInfo(
                    "ClassInd network: previous DNS/network failure; skipping repeated online attempt in this session.");
            }
            else
            try
            {
                resourceUrl = await ResolveCurrentCsvUrlAsync(token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                _networkUnavailableUntilUtc = DateTime.UtcNow.AddMinutes(5);
                DiagnosticLogService.LogException("ClassInd network discovery", ex);
            }
            catch
            {
                // A API CKAN pode estar indisponível mesmo quando o arquivo CSV
                // continua acessível. Nesse caso, ainda tentamos o recurso oficial
                // conhecido em vez de abortar toda a atualização.
            }

            foreach (var candidateUrl in new[] { resourceUrl, CurrentCsvFallback }
                         .Where(url => !string.IsNullOrWhiteSpace(url))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!forceRefresh && DateTime.UtcNow < _networkUnavailableUntilUtc)
                    break;
                try
                {
                    var fresh = await DownloadEntriesAsync(candidateUrl!, token);
                    if (fresh.Count == 0)
                        continue;

                    _entries = fresh;
                    _loadedAtUtc = DateTime.UtcNow;
                    SaveDiskCache(_entries, _loadedAtUtc);
                    return _entries;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Tenta o próximo endereço oficial disponível.
                }
            }

            // O portal de dados do MJSP já apresentou indisponibilidade/erros ao servir
            // diretamente o CSV. Quando a build inclui um snapshot oficial atribuído,
            // usa essa cópia local como fallback sem depender da rede e sem hardcode por jogo.
            var bundledEntries = TryLoadBundledCsv();
            if (bundledEntries.Count > 0)
            {
                _entries = bundledEntries;
                _loadedAtUtc = DateTime.UtcNow;
                SaveDiskCache(_entries, _loadedAtUtc);
                DiagnosticLogService.LogInfo(
                    $"ClassInd bundled CSV: loaded {_entries.Count} records.");
                return _entries;
            }

            if (TryLoadDiskCache(out diskEntries, out cachedAtUtc))
            {
                _entries = diskEntries;
                _loadedAtUtc = cachedAtUtc;
                return _entries;
            }

            // Falha total: não transforma "zero registros" em cache válido.
            // A próxima atualização manual deve tentar a rede novamente.
            _entries = null;
            _loadedAtUtc = DateTime.MinValue;
            return Array.Empty<ClassIndEntry>();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> ResolveCurrentCsvUrlAsync(CancellationToken token)
    {
        // O recurso de Jogos Eletrônicos possui UUID estável no CKAN. Consultá-lo
        // diretamente evita depender da enumeração completa do dataset e reduz a
        // chance de falha quando outros recursos do conjunto mudam.
        try
        {
            using var resourceResponse = await _http.GetAsync(ResourceApi, token);
            DiagnosticLogService.LogInfo(
                $"ClassInd resource_show: HTTP {(int)resourceResponse.StatusCode} {resourceResponse.StatusCode}.");

            if (resourceResponse.IsSuccessStatusCode)
            {
                using var resourceDoc = JsonDocument.Parse(
                    await SafeHttpResponseService.ReadTextAsync(
                        resourceResponse,
                        maxBytes: 1024 * 1024,
                        cancellationToken: token));

                if (resourceDoc.RootElement.TryGetProperty("success", out var resourceSuccess) &&
                    resourceSuccess.GetBoolean() &&
                    resourceDoc.RootElement.TryGetProperty("result", out var resourceResult))
                {
                    var format = resourceResult.TryGetProperty("format", out var formatElement)
                        ? formatElement.GetString()
                        : null;
                    var url = resourceResult.TryGetProperty("url", out var urlElement)
                        ? urlElement.GetString()
                        : null;

                    if (!string.IsNullOrWhiteSpace(url) &&
                        (string.IsNullOrWhiteSpace(format) ||
                         string.Equals(format, "CSV", StringComparison.OrdinalIgnoreCase)))
                    {
                        DiagnosticLogService.LogInfo(
                            "ClassInd resource_show: current CSV URL discovered.");
                        return url;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException("ClassInd resource_show", ex);
        }

        // Compatibilidade: se resource_show estiver temporariamente indisponível,
        // ainda tenta localizar o mesmo recurso na descrição completa do dataset.
        try
        {
            using var response = await _http.GetAsync(PackageApi, token);
            DiagnosticLogService.LogInfo(
                $"ClassInd package_show: HTTP {(int)response.StatusCode} {response.StatusCode}.");
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
                var id = resource.TryGetProperty("id", out var idElement)
                    ? idElement.GetString()
                    : null;
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
                if (string.Equals(id, ResourceId, StringComparison.OrdinalIgnoreCase) ||
                    normalizedName.Contains("listajogosdadosabertos", StringComparison.Ordinal) ||
                    normalizedName.Contains("jogoseletronicos", StringComparison.Ordinal))
                {
                    DiagnosticLogService.LogInfo(
                        "ClassInd package_show: CSV resource discovered.");
                    return url;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException("ClassInd package_show", ex);
        }

        return null;
    }

    private async Task<List<ClassIndEntry>> DownloadEntriesAsync(
        string url,
        CancellationToken token)
    {
        using var response = await _http.GetAsync(url, token);
        DiagnosticLogService.LogInfo(
            $"ClassInd CSV: HTTP {(int)response.StatusCode} {response.StatusCode}.");
        if (!response.IsSuccessStatusCode)
            return new();

        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        DiagnosticLogService.LogInfo($"ClassInd CSV: received {bytes.Length} bytes.");
        if (bytes.Length == 0)
            return new();

        var text = DecodeCsv(bytes);
        var entries = ParseEntries(text);
        DiagnosticLogService.LogInfo($"ClassInd CSV: parsed {entries.Count} records.");
        return entries;
    }

    private List<ClassIndEntry> TryLoadBundledCsv()
    {
        try
        {
            if (!File.Exists(_bundledCsvPath))
            {
                DiagnosticLogService.LogInfo("ClassInd bundled CSV: file not present.");
                return new();
            }

            var bytes = File.ReadAllBytes(_bundledCsvPath);
            if (bytes.Length == 0)
                return new();

            var text = DecodeCsv(bytes);
            var entries = ParseEntries(text);
            DiagnosticLogService.LogInfo(
                $"ClassInd bundled CSV: parsed {entries.Count} records.");
            return entries;
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException("ClassInd bundled CSV", ex);
            return new();
        }
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
            .Take(Math.Min(100, rows.Count))
            .Select((row, index) => new
            {
                Index = index,
                Headers = row
                    .Select((value, column) => new HeaderEntry(column, NormalizeHeader(value)))
                    .ToList()
            })
            .FirstOrDefault(candidate =>
                FindHeader(candidate.Headers,
                    "titulonobrasil",
                    "titulobrasil",
                    "titulonacional",
                    "nomedojogo",
                    "titulojogo") >= 0);

        if (headerRowIndex is null)
        {
            DiagnosticLogService.LogInfo("ClassInd CSV: header row not found.");
            return new();
        }

        DiagnosticLogService.LogInfo(
            $"ClassInd CSV: header row detected at index {headerRowIndex.Index}.");

        var headers = headerRowIndex.Headers;
        var headerColumnCount = rows[headerRowIndex.Index].Count;

        // Esquema oficial de "DADOS DOS JOGOS ELETRÔNICOS" do ClassInd:
        // B = Título no Brasil; C = Título original; M = Classificação atribuída.
        // Usa os nomes primeiro e as posições oficiais como fallback, porque o portal
        // já alterou acentos/capitalização de cabeçalhos entre exportações.
        var titleBrazilIndex = FindHeader(headers,
            "titulonobrasil",
            "titulobrasil",
            "titulonacional",
            "nomedojogo",
            "titulojogo");
        if (titleBrazilIndex < 0 && headerColumnCount > 1)
            titleBrazilIndex = 1;

        var titleSeriesIndex = FindHeader(headers,
            "titulooriginal",
            "titulooriginal",
            "titulodaserie",
            "tituloserie",
            "serie");
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
            .Take(120)
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
                lines.Take(40).Sum(line => CountOutsideQuotes(line, candidate)))
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

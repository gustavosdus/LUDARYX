using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed class MetadataService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly string _cacheFile;
    private readonly string _coverDir;
    private Dictionary<string, GameMetadata> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheSync = new();
    private string? _igdbToken;
    private DateTime _igdbTokenExpires;
    private static readonly TimeSpan SteamArtworkMaxAge = TimeSpan.FromHours(24);

    // Alguns jogos já tiveram a Library Capsule oficial atualizada pelo desenvolvedor,
    // mas usuários podem manter uma cópia antiga em cache por bastante tempo. Estes
    // App IDs passam por revalidação obrigatória da arte oficial para garantir que a
    // capa vertical acompanhe a atualização publicada na Steam.
    private static readonly HashSet<int> KnownSteamArtworkRefreshAppIds = new()
    {
        1955380 // Sarissa and the Legendary Sword
    };

    // Fallbacks exatos para títulos cuja pontuação/nome costuma falhar nas buscas da Steam.
    // Mantidos em um único ponto para facilitar revisão e edição manual futura.
    private static readonly IReadOnlyDictionary<string, int> KnownSteamAppIds =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["gijoewrathofcobra"] = 2516170,
            ["killerinstinct"] = 577940,
            ["inexistence"] = 444720,
            ["sonicthehedgehog4episodei"] = 202530
        };

    private static readonly IReadOnlyDictionary<string, KnownMetadataCorrection> KnownMetadataCorrections =
        new Dictionary<string, KnownMetadataCorrection>(StringComparer.OrdinalIgnoreCase)
        {
            ["gijoewrathofcobra"] = new(
                Developer: "Maple Powered Games",
                Publisher: "indie.io",
                Description: "A side-scrolling beat 'em up based on G.I. Joe, with playable heroes battling Cobra forces using character-specific attacks and weapons.",
                AdditionalGenres: new[] { "Beat 'em up", "Action", "Adventure" }),

            // Casos residuais da Steam em que appdetails/página podem responder de forma
            // parcial dependendo da sessão/região. Os valores abaixo vêm da própria
            // página oficial da Steam e só completam/corrigem estes títulos conhecidos.
            ["thewalkingdead"] = new(
                Developer: "Telltale Games",
                Publisher: "Skybound Games",
                Description: null,
                AdditionalGenres: new[] { "Adventure" }),

            ["thewitcher2assassinsofkingsenhancededition"] = new(
                Developer: "CD PROJEKT RED",
                Publisher: "CD PROJEKT RED, 1C-SoftClub",
                Description: null,
                AdditionalGenres: new[] { "RPG" }),

            ["thewitcher3wildhuntcompleteedition"] = new(
                Developer: "CD PROJEKT RED",
                Publisher: "CD PROJEKT RED",
                Description: null,
                AdditionalGenres: new[] { "RPG" }),

            ["thewitcherenhancededitiondirectorscut"] = new(
                Developer: "CD PROJEKT RED",
                Publisher: "CD PROJEKT RED, 1C-SoftClub",
                Description: null,
                AdditionalGenres: new[] { "RPG" }),

            ["lifeisstrangeepisode1"] = new(
                Developer: "DONTNOD Entertainment",
                Publisher: "Square Enix",
                Description: null,
                AdditionalGenres: new[] { "Action", "Adventure" }),

            ["negligee"] = new(
                Developer: "Dharker Studios",
                Publisher: "Dharker Studios",
                Description: "A story-focused visual novel about managing a clothing shop and the relationships among its characters.",
                AdditionalGenres: new[] { "Adventure", "Casual", "Indie", "Visual Novel" })
        };
    private readonly SteamGridDbService _steamGridDb = new();
    private readonly PcGamingWikiService _pcGamingWiki = new();
    private readonly ClassIndOpenDataService _classInd = new();
    private readonly SemaphoreSlim _steamAppListGate = new(1, 1);
    private readonly SemaphoreSlim _steamStoreGate = new(2, 2);
    private Dictionary<string, List<int>>? _steamAppIdsByNormalizedName;

    public MetadataService()
    {
        AppDataService.EnsureMigrated();
        var root = AppDataService.RootDirectory;
        Directory.CreateDirectory(root);
        _cacheFile = Path.Combine(root, "metadata.json");
        _coverDir = Path.Combine(root, "covers");
        Directory.CreateDirectory(_coverDir);

        // Steam pode responder de forma diferente a clientes HTTP sem User-Agent.
        // Mantemos um identificador estável para as consultas de metadados.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 LUDARYX/1.1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/html;q=0.9,*/*;q=0.8");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        LoadCache();
    }

    public void ApplyCachedMetadata(IEnumerable<Game> games)
    {
        foreach (var game in games)
        {
            if (_cache.TryGetValue(game.ProviderId, out var cached))
                Apply(game, cached);
        }
    }

    public async Task EnrichAsync(
        IEnumerable<Game> games,
        LauncherSettings settings,
        bool forceArtworkRefresh = false,
        CancellationToken token = default)
    {
        foreach (var game in games)
        {
            token.ThrowIfCancellationRequested();
            await EnrichGameAsync(game, settings, forceArtworkRefresh, token);
        }

        SaveCache();
    }

    /// <summary>
    /// Enriquece um único jogo. Esta entrada separada permite ao chamador priorizar
    /// jogos de outros launchers e usar concorrência limitada sem bloquear a biblioteca.
    /// </summary>
    public async Task EnrichGameAsync(
        Game game,
        LauncherSettings settings,
        bool forceArtworkRefresh = false,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var key = game.ProviderId;

        GameMetadata? cached = null;
        lock (_cacheSync)
        {
            if (_cache.TryGetValue(key, out var value))
                cached = value;
        }

        if (cached is not null &&
            game.Platform != GamePlatform.Steam &&
            !string.IsNullOrWhiteSpace(cached.CanonicalName) &&
            !string.Equals(cached.Source, "Local", StringComparison.OrdinalIgnoreCase) &&
            !TitlesMatch(game.Name, cached.CanonicalName))
        {
            // Um cache antigo pode ter sido associado ao produto errado (por exemplo,
            // um Product ID GOG legado). Preserva apenas as artes já válidas e força
            // uma nova identificação textual pelo nome do jogo detectado.
            cached = BuildArtworkOnlyCache(game, cached);
        }

        if (cached is not null)
            Apply(game, cached);

        var cacheIsFresh = cached is not null && cached.UpdatedAtUtc > DateTime.UtcNow.AddDays(-30);
        var cacheIsOnlyLocal = cached is not null && string.Equals(cached.Source, "Local", StringComparison.OrdinalIgnoreCase);
        var missingAutomaticArtwork = cached is null ||
                                     string.IsNullOrWhiteSpace(cached.HorizontalCoverLocalPath) ||
                                     string.IsNullOrWhiteSpace(cached.VerticalCoverLocalPath);
        var missingUsefulMetadata = cached is null ||
                                  IsLowQualityDescription(cached.Description) ||
                                  cached.Genres.Count == 0 ||
                                  string.IsNullOrWhiteSpace(cached.Developer) ||
                                  string.IsNullOrWhiteSpace(cached.Publisher) ||
                                  !cached.ReleaseYear.HasValue;

        // Jogos não-Steam já existentes em versões antigas costumavam ter apenas
        // Source=Local. Eles precisam ser reconsiderados mesmo com cache recente.
        var shouldRetryCrossPlatform = game.Platform != GamePlatform.Steam &&
                                       (cacheIsOnlyLocal || missingAutomaticArtwork || missingUsefulMetadata);

        var steamCacheSources = NormalizeMetadataSources(cached?.Source)?.Split(
            " + ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
        var steamCacheIsAuthoritative = game.Platform != GamePlatform.Steam ||
                                        !settings.UseSteamStoreMetadata ||
                                        (cached is not null &&
                                         steamCacheSources.Length > 0 &&
                                         steamCacheSources[0].StartsWith("Steam Store", StringComparison.OrdinalIgnoreCase) &&
                                         string.Equals(cached.ExternalId, game.Id, StringComparison.OrdinalIgnoreCase));

        if (cacheIsFresh && steamCacheIsAuthoritative && !cacheIsOnlyLocal &&
            !missingAutomaticArtwork && !missingUsefulMetadata &&
            !shouldRetryCrossPlatform && !forceArtworkRefresh)
        {
            // Metadados textuais frescos não significam que a classificação regional
            // preferida já exista. Em pt-BR, por exemplo, ClassInd pode ser preenchida
            // depois a partir da base oficial do MJSP.
            if (!HasPreferredAgeRating(game.Metadata, settings.Language))
                await EnsureLocalizedAgeRatingAsync(game, settings, token);
            return;
        }

        GameMetadata? metadata = null;
        var authoritativeSteamMetadataLoaded = false;

        // Fontes podem retornar dados parciais. A ordem é importante: dados de loja
        // ligados ao título exato (Steam/GOG) são mais completos e específicos que
        // descrições genéricas do Wikidata. Fontes enciclopédicas ficam como fallback.
        if (game.Platform == GamePlatform.Steam)
        {
            // Para um jogo detectado pela Steam, o App ID instalado é a identidade mais
            // confiável possível. A Steam é autoritativa para nome, descrição, gêneros,
            // lançamento e artes. Outras fontes só podem preencher campos ausentes.
            var steamMetadata = settings.UseSteamStoreMetadata
                ? await GetSteamMetadataAsync(game, token)
                : null;
            if (steamMetadata is not null)
            {
                metadata = steamMetadata;
                authoritativeSteamMetadataLoaded = true;
            }

            if (!HasCompleteTextMetadata(metadata))
            {
                var pcgw = await _pcGamingWiki.GetMetadataByExactTitleAsync(game.Name, token);
                metadata = MergeSupplementalMetadata(
                    metadata,
                    pcgw,
                    allowDescription: IsLowQualityDescription(metadata?.Description));
            }

            if (!HasCompleteTextMetadata(metadata))
            {
                var wiki = await GetWikidataMetadataByExactNameAsync(game, token);
                metadata = MergeSupplementalMetadata(
                    metadata,
                    wiki,
                    allowDescription: IsLowQualityDescription(metadata?.Description));
            }

            // Nunca usa GOG como fonte automática para um jogo que sabemos ser Steam.
            // Isso evita que descrições/edições diferentes substituam dados do App ID.
        }
        else if (game.Platform == GamePlatform.GOG)
        {
            metadata = MergeMetadata(metadata, await GetGogMetadataAsync(game, token));

            if (!HasCompleteTextMetadata(metadata))
                metadata = await MergePcGamingWikiAsync(game, metadata, token);

            if (!HasCompleteTextMetadata(metadata))
                metadata = MergeMetadata(metadata, await GetWikidataMetadataByExactNameAsync(game, token));
        }
        else if (game.Platform == GamePlatform.Manual)
        {
            // Jogos adicionados manualmente não possuem uma loja de origem confiável.
            // Prioriza fontes enciclopédicas/PC antes de tentar catálogos de lojas,
            // evitando gastar todo o timeout em Steam/GOG para títulos de console.
            metadata = await MergePcGamingWikiAsync(game, metadata, token);

            if (!HasCompleteTextMetadata(metadata))
                metadata = MergeMetadata(metadata, await GetWikidataMetadataByExactNameAsync(game, token));

            if (!HasCompleteTextMetadata(metadata))
                metadata = MergeMetadata(metadata, await GetSteamMetadataByExactNameAsync(game, token));

            if (!HasCompleteTextMetadata(metadata))
            {
                var gogFallback = await FindGogCatalogMetadataByExactNameAsync(game, token);
                metadata = MergeMetadata(metadata, gogFallback.Metadata);
            }
        }
        else
        {
            // Para Epic/EA/Ubisoft/Battle.net/Riot/Xbox, tenta primeiro uma edição
            // exata na Steam, depois PCGamingWiki/Wikidata. GOG é apenas último recurso.
            metadata = MergeMetadata(metadata, await GetSteamMetadataByExactNameAsync(game, token));

            if (!HasCompleteTextMetadata(metadata))
                metadata = await MergePcGamingWikiAsync(game, metadata, token);

            if (!HasCompleteTextMetadata(metadata))
                metadata = MergeMetadata(metadata, await GetWikidataMetadataByExactNameAsync(game, token));

            if (!HasCompleteTextMetadata(metadata))
            {
                var gogFallback = await FindGogCatalogMetadataByExactNameAsync(game, token);
                metadata = MergeSupplementalMetadata(metadata, gogFallback.Metadata, allowDescription: IsLowQualityDescription(metadata?.Description));

                if (!string.IsNullOrWhiteSpace(gogFallback.ProductId) && !HasCompleteTextMetadata(metadata))
                {
                    var gogById = await GetGogMetadataByProductIdAsync(gogFallback.ProductId, game, token);
                    metadata = MergeSupplementalMetadata(metadata, gogById, allowDescription: IsLowQualityDescription(metadata?.Description));
                }
            }
        }

        if (settings.UseIgdbMetadata && !HasCompleteTextMetadata(metadata))
            metadata = MergeMetadata(metadata, await GetIgdbMetadataAsync(game, settings, token));

        // Uma atualização nunca pode piorar os dados já salvos. Para jogos Steam,
        // uma resposta parcial do App ID não deve apagar campos completos obtidos em
        // uma execução anterior. O cache só pode preencher lacunas quando pertence ao
        // mesmo App ID instalado; ele nunca substitui um valor novo fornecido pela Steam.
        if (game.Platform == GamePlatform.Steam && authoritativeSteamMetadataLoaded)
            metadata = MergeSteamCacheFallback(metadata, cached, game.Id);
        else
            metadata = MergeMetadata(metadata, cached);

        metadata ??= BuildFallbackMetadata(game);

        // Correções conhecidas são aplicadas no fim do merge para que também
        // funcionem quando uma fonte externa estiver temporariamente indisponível.
        ApplyKnownMetadataCorrection(game, metadata);
        CompleteStructuredMetadataFromDescription(metadata);
        SanitizeCompanyFields(metadata);

        // Preserva as artes locais já obtidas anteriormente. A fonte de metadados
        // textual não deve provocar um novo download se a arte válida já existe.
        PreserveExistingArtwork(metadata, cached, game.Metadata);

        // Metadados remotos nunca devem apagar artes escolhidas pelo usuário.
        metadata.CustomHorizontalCoverLocalPath = game.Metadata.CustomHorizontalCoverLocalPath;
        metadata.CustomVerticalCoverLocalPath = game.Metadata.CustomVerticalCoverLocalPath;

        if (game.Platform == GamePlatform.Steam)
            await EnsureSteamVerticalCoverAsync(metadata, game, token);

        await EnsureCoverAsync(metadata, game, token, forceArtworkRefresh);

        // Steam usa primeiro a Library Capsule oficial. Alguns jogos antigos nunca
        // receberam essa arte vertical; nesses casos o SteamGridDB preenche apenas
        // a orientação ausente. "Restaurar capa padrão" volta para esta cadeia
        // automática em vez de manter a orientação permanentemente vazia.
        if (!string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
            await EnsureSteamGridDbArtworkAsync(metadata, game, settings, forceArtworkRefresh, token);

        metadata.Source = NormalizeMetadataSources(metadata.Source);
        metadata.UpdatedAtUtc = DateTime.UtcNow;

        lock (_cacheSync)
            _cache[key] = metadata;

        Apply(game, metadata);

        // A atualização geral também deve preencher classificação indicativa para
        // jogos fora da Steam. Só faz a consulta complementar quando nenhuma fonte
        // já forneceu classificação, evitando rede desnecessária na biblioteca.
        if (!HasPreferredAgeRating(game.Metadata, settings.Language))
            await EnsureLocalizedAgeRatingAsync(game, settings, token);
    }

    public void SaveCacheSnapshot() => SaveCache();

    /// <summary>
    /// Retorna a descrição já armazenada para o idioma atual do programa.
    /// Descrições editadas manualmente pelo usuário sempre têm prioridade e não são
    /// traduzidas/substituídas automaticamente.
    /// </summary>
    public string? GetDisplayDescription(Game game, LauncherSettings settings)
    {
        if (settings.ManualMetadata.TryGetValue(game.ProviderId, out var manual) &&
            !string.IsNullOrWhiteSpace(manual.Description))
        {
            return manual.Description;
        }

        var language = NormalizeDescriptionLanguage(settings.Language);
        var descriptions = game.Metadata.LocalizedDescriptions ??= new(StringComparer.OrdinalIgnoreCase);
        if (descriptions.TryGetValue(language, out var exact) && !IsLowQualityDescription(exact))
            return exact;

        // Se uma variante regional ainda não foi buscada, uma variante do mesmo idioma
        // é melhor do que misturar inglês/português/espanhol sem necessidade.
        foreach (var fallback in GetSameLanguageFallbacks(language))
        {
            if (descriptions.TryGetValue(fallback, out var value) && !IsLowQualityDescription(value))
                return value;
        }

        // Se não houver uma descrição no idioma selecionado, mantém uma descrição
        // válida em inglês em vez de ocultar o texto. Isso é especialmente importante
        // para fontes como GOG e alguns fallbacks enciclopédicos, que podem não oferecer
        // conteúdo em português/espanhol. A descrição inglesa nunca é gravada como se
        // pertencesse ao idioma atual; portanto, uma tradução futura ainda pode substituí-la.
        var englishFallback = GetCachedEnglishDescription(game.Metadata);
        return !IsLowQualityDescription(englishFallback) ? englishFallback : null;
    }

    /// <summary>
    /// Busca somente a descrição no idioma selecionado, sem refazer os demais
    /// metadados do jogo. O resultado fica em cache por idioma para que trocar o
    /// idioma do programa não misture descrições de sessões anteriores.
    /// </summary>
    public async Task<string?> EnsureLocalizedDescriptionAsync(
        Game game,
        LauncherSettings settings,
        CancellationToken token = default)
    {
        if (settings.ManualMetadata.TryGetValue(game.ProviderId, out var manual) &&
            !string.IsNullOrWhiteSpace(manual.Description))
        {
            return manual.Description;
        }

        var language = NormalizeDescriptionLanguage(settings.Language);
        var descriptions = game.Metadata.LocalizedDescriptions ??= new(StringComparer.OrdinalIgnoreCase);
        if (descriptions.TryGetValue(language, out var existing) && !IsLowQualityDescription(existing))
            return existing;

        string? localized = null;

        // Para jogos Steam, o App ID instalado é a fonte mais confiável. Para jogos de
        // outros launchers que já foram relacionados à Steam, reutiliza o ExternalId.
        if (game.Platform == GamePlatform.Steam && int.TryParse(game.Id, out var installedAppId))
        {
            localized = await GetSteamLocalizedDescriptionAsync(installedAppId, language, token);
        }
        else if (game.Metadata.Source?.Contains("Steam Store", StringComparison.OrdinalIgnoreCase) == true &&
                 int.TryParse(game.Metadata.ExternalId, out var relatedSteamAppId))
        {
            localized = await GetSteamLocalizedDescriptionAsync(relatedSteamAppId, language, token);
        }

        // Steam nem sempre possui tradução para todos os idiomas. Wikipedia é usada
        // apenas como fallback de descrição, nunca para sobrescrever empresas/gêneros.
        if (IsLowQualityDescription(localized))
            localized = await GetLocalizedWikipediaDescriptionAsync(game.Name, language, token);

        if (!IsLowQualityDescription(localized))
        {
            localized = SanitizeDescription(localized);
            descriptions[language] = localized!;

            lock (_cacheSync)
            {
                if (_cache.TryGetValue(game.ProviderId, out var cached))
                {
                    cached.LocalizedDescriptions ??= new(StringComparer.OrdinalIgnoreCase);
                    cached.LocalizedDescriptions[language] = localized!;
                }
                else
                {
                    _cache[game.ProviderId] = game.Metadata;
                }
            }

            SaveCache();
            return localized;
        }

        foreach (var fallback in GetSameLanguageFallbacks(language))
        {
            if (descriptions.TryGetValue(fallback, out var value) && !IsLowQualityDescription(value))
                return value;
        }

        // Regra global de fallback: se a fonte não disponibilizar a descrição no idioma
        // selecionado, exibe a versão inglesa. Primeiro reaproveita o inglês já existente
        // no cache/metadados; somente consulta a rede se realmente não houver uma versão
        // inglesa disponível localmente.
        var englishFallback = GetCachedEnglishDescription(game.Metadata);
        if (IsLowQualityDescription(englishFallback) &&
            !language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            englishFallback = await FetchEnglishFallbackDescriptionAsync(game, token);
            if (!IsLowQualityDescription(englishFallback))
            {
                englishFallback = SanitizeDescription(englishFallback);
                descriptions["en-US"] = englishFallback!;

                lock (_cacheSync)
                {
                    if (_cache.TryGetValue(game.ProviderId, out var cached))
                    {
                        cached.LocalizedDescriptions ??= new(StringComparer.OrdinalIgnoreCase);
                        cached.LocalizedDescriptions["en-US"] = englishFallback!;
                    }
                    else
                    {
                        _cache[game.ProviderId] = game.Metadata;
                    }
                }

                SaveCache();
            }
        }

        return !IsLowQualityDescription(englishFallback) ? englishFallback : null;
    }

    public async Task EnsureLocalizedAgeRatingAsync(
        Game game,
        LauncherSettings settings,
        CancellationToken token = default)
    {
        // ClassInd brasileira vem exclusivamente da base oficial aberta do MJSP.
        // Não usamos scraping do portal público nem convertemos notas de outros órgãos.
        if (AgeRatingService.GetPreferredSystemKey(settings.Language)
                .Equals("dejus", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureOfficialClassIndRatingAsync(game, token);
        }

        var appId = 0;
        if (game.Platform == GamePlatform.Steam)
            int.TryParse(game.Id, out appId);
        else if (game.Metadata.Source?.Contains("Steam Store", StringComparison.OrdinalIgnoreCase) == true)
            int.TryParse(game.Metadata.ExternalId, out appId);

        if (appId <= 0)
        {
            if (!HasPreferredAgeRating(game.Metadata, settings.Language))
                await EnsureWikidataAgeRatingsAsync(game, settings, token);
            return;
        }

        var changed = false;
        var locales = GetSteamAgeRatingLocales(settings.Language);

        await _steamStoreGate.WaitAsync(token);
        try
        {
            // Classificações da Steam podem variar conforme o país consultado.
            // Consulta um conjunto pequeno e estável de regiões para reunir ESRB,
            // PEGI e ClassInd quando a própria Steam realmente os publica.
            foreach (var (steamLanguage, countryCode, ludaryxLanguage) in locales)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    var apiUrl = $"https://store.steampowered.com/api/appdetails?appids={appId}" +
                                 $"&l={Uri.EscapeDataString(steamLanguage)}&cc={Uri.EscapeDataString(countryCode)}";
                    using var apiResponse = await _http.GetAsync(apiUrl, token);
                    if (apiResponse.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(
                            await SafeHttpResponseService.ReadTextAsync(apiResponse, cancellationToken: token));

                        var appIdText = appId.ToString(CultureInfo.InvariantCulture);
                        if (doc.RootElement.TryGetProperty(appIdText, out var entry) &&
                            entry.TryGetProperty("success", out var success) &&
                            success.GetBoolean() &&
                            entry.TryGetProperty("data", out var data))
                        {
                            if (data.TryGetProperty("ratings", out var ratings) &&
                                ratings.ValueKind == JsonValueKind.Object)
                            {
                                game.Metadata.AgeRatings ??= new(StringComparer.OrdinalIgnoreCase);
                                foreach (var ratingProperty in ratings.EnumerateObject())
                                {
                                    var ratingValue = ReadSteamAgeRatingValue(ratingProperty.Value);
                                    if (string.IsNullOrWhiteSpace(ratingValue))
                                        continue;

                                    var key = AgeRatingService.NormalizeSystem(ratingProperty.Name);
                                    if (string.IsNullOrWhiteSpace(key))
                                        key = ratingProperty.Name;

                                    if (!game.Metadata.AgeRatings.TryGetValue(key, out var existing) ||
                                        string.IsNullOrWhiteSpace(existing))
                                    {
                                        game.Metadata.AgeRatings[key] = ratingValue;
                                        changed = true;
                                    }
                                }
                            }

                            if (!game.Metadata.ReleaseYear.HasValue &&
                                data.TryGetProperty("release_date", out var releaseDate) &&
                                releaseDate.TryGetProperty("date", out var date))
                            {
                                var year = TryExtractYear(date.GetString());
                                if (year.HasValue)
                                {
                                    game.Metadata.ReleaseYear = year.Value;
                                    changed = true;
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Continua para a página regional da própria Steam.
                }

                try
                {
                    var pageUrl = $"https://store.steampowered.com/app/{appId}/" +
                                  $"?l={Uri.EscapeDataString(steamLanguage)}&cc={Uri.EscapeDataString(countryCode)}";
                    using var pageResponse = await _http.GetAsync(pageUrl, token);
                    if (!pageResponse.IsSuccessStatusCode)
                        continue;

                    var html = await SafeHttpResponseService.ReadTextAsync(
                        pageResponse, maxBytes: 4 * 1024 * 1024, cancellationToken: token);

                    game.Metadata.AgeRatings ??= new(StringComparer.OrdinalIgnoreCase);
                    changed |= ExtractSteamAgeRatingsFromHtml(
                        html,
                        ludaryxLanguage,
                        game.Metadata.AgeRatings);

                    if (!game.Metadata.ReleaseYear.HasValue)
                    {
                        var year = TryExtractSteamReleaseYearFromHtml(html);
                        if (year.HasValue)
                        {
                            game.Metadata.ReleaseYear = year.Value;
                            changed = true;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Uma região indisponível não impede as demais.
                }
            }

            if (changed)
            {
                lock (_cacheSync)
                    _cache[game.ProviderId] = game.Metadata;
                SaveCache();
            }
        }
        finally
        {
            _steamStoreGate.Release();
        }

        // Wikidata continua sendo apenas complemento estruturado quando a classificação
        // preferida ainda não foi encontrada em uma fonte de loja válida.
        if (!HasPreferredAgeRating(game.Metadata, settings.Language))
            await EnsureWikidataAgeRatingsAsync(game, settings, token);
    }

    private async Task EnsureOfficialClassIndRatingAsync(
        Game game,
        CancellationToken token)
    {
        game.Metadata.AgeRatings ??= new(StringComparer.OrdinalIgnoreCase);

        if (game.Metadata.AgeRatings.Any(pair =>
                AgeRatingService.NormalizeSystem(pair.Key)
                    .Equals("dejus", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pair.Value)))
        {
            return;
        }

        try
        {
            var rating = await _classInd.GetRatingAsync(game.Name, token);
            if (string.IsNullOrWhiteSpace(rating))
                return;

            game.Metadata.AgeRatings["dejus"] = rating;
            game.Metadata.Source = CombineMetadataSources(
                game.Metadata.Source,
                "ClassInd/MJSP (dados abertos)");
            game.Metadata.UpdatedAtUtc = DateTime.UtcNow;

            lock (_cacheSync)
                _cache[game.ProviderId] = game.Metadata;
            SaveCache();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A classificação brasileira é complementar. Mantém os dados existentes
            // se o catálogo oficial estiver temporariamente indisponível.
        }
    }

    private static bool HasPreferredAgeRating(GameMetadata metadata, string language)
    {
        metadata.AgeRatings ??= new(StringComparer.OrdinalIgnoreCase);
        var preferred = AgeRatingService.GetPreferredSystemKey(language);
        return metadata.AgeRatings.Any(pair =>
            AgeRatingService.NormalizeSystem(pair.Key)
                .Equals(preferred, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(pair.Value));
    }

    private async Task EnsureWikidataAgeRatingsAsync(
        Game game,
        LauncherSettings settings,
        CancellationToken token)
    {
        try
        {
            var wikidata = await GetWikidataMetadataByExactNameAsync(game, token);
            if (wikidata?.AgeRatings is null || wikidata.AgeRatings.Count == 0)
                return;

            game.Metadata.AgeRatings ??= new(StringComparer.OrdinalIgnoreCase);
            var changed = false;

            foreach (var pair in wikidata.AgeRatings)
            {
                var key = AgeRatingService.NormalizeSystem(pair.Key);
                if (string.IsNullOrWhiteSpace(key) ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    game.Metadata.AgeRatings.ContainsKey(key))
                {
                    continue;
                }

                game.Metadata.AgeRatings[key] = pair.Value.Trim();
                changed = true;
            }

            if (!changed)
                return;

            game.Metadata.Source = CombineMetadataSources(game.Metadata.Source, "Wikidata");
            game.Metadata.UpdatedAtUtc = DateTime.UtcNow;

            lock (_cacheSync)
                _cache[game.ProviderId] = game.Metadata;
            SaveCache();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Mantém classificações já armazenadas se o fallback estiver indisponível.
        }
    }

    private static bool ExtractSteamAgeRatingsFromHtml(
        string html,
        string language,
        Dictionary<string, string> ratings)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        var changed = false;
        var plain = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
        plain = Regex.Replace(plain, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        plain = Regex.Replace(plain, @"<[^>]+>", " ");
        plain = WebUtility.HtmlDecode(plain);
        plain = Regex.Replace(plain, @"\s+", " ").Trim();

        // ClassInd / DEJUS. A página brasileira usa textos como
        // "Classificação Indicativa: 16 ANOS".
        var classInd = Regex.Match(
            plain,
            @"Classifica(?:ção|cao)\s+Indicativa\s*:\s*(?<value>LIVRE|10\s*ANOS?|12\s*ANOS?|14\s*ANOS?|16\s*ANOS?|18\s*ANOS?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (classInd.Success)
        {
            var value = Regex.Replace(classInd.Groups["value"].Value.ToUpperInvariant(), @"\s*ANOS?", string.Empty).Trim();
            ratings["dejus"] = value;
            changed = true;
        }

        // PEGI pode aparecer tanto em texto/alt quanto no nome da imagem.
        var pegi = Regex.Match(
            html,
            @"(?:PEGI|pegi)[^0-9]{0,80}(?<value>3|7|12|16|18)(?!\d)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (pegi.Success)
        {
            ratings["pegi"] = pegi.Groups["value"].Value;
            changed = true;
        }

        // ESRB: captura os nomes públicos comuns sem tentar traduzi-los/converter.
        var esrb = Regex.Match(
            plain,
            @"(?<value>Adults\s+Only(?:\s*18\+)?|Mature(?:\s*17\+)?|Teen|Everyone\s*10\+|Everyone|Rating\s+Pending)\s*(?:\([^)]*\))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (esrb.Success &&
            (plain.Contains("ESRB", StringComparison.OrdinalIgnoreCase) ||
             language.Equals("en-US", StringComparison.OrdinalIgnoreCase)))
        {
            ratings["esrb"] = esrb.Groups["value"].Value.Trim();
            changed = true;
        }

        return changed;
    }

    private static int? TryExtractSteamReleaseYearFromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        foreach (var pattern in new[]
        {
            @"class=[""'][^""']*release_date[^""']*[""'][^>]*>[\s\S]*?class=[""'][^""']*date[^""']*[""'][^>]*>(?<value>[\s\S]*?)</div>",
            @"(?:Release\s+Date|Data\s+de\s+lançamento|Data\s+de\s+lançamento|Fecha\s+de\s+lanzamiento)\s*:?[\s\S]{0,160}?(?<value>19[7-9]\d|20\d{2})"
        })
        {
            var match = Regex.Match(
                html,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (!match.Success)
                continue;

            var value = WebUtility.HtmlDecode(Regex.Replace(match.Groups["value"].Value, @"<[^>]+>", " "));
            var year = TryExtractYear(value);
            if (year.HasValue)
                return year;
        }

        return null;
    }

    private static int? TryExtractYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = Regex.Match(value, @"\b(19[7-9]\d|20\d{2})\b", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Value, out var year) ? year : null;
    }

    private static string? GetCachedEnglishDescription(GameMetadata metadata)
    {
        metadata.LocalizedDescriptions ??= new(StringComparer.OrdinalIgnoreCase);

        foreach (var englishLanguage in new[] { "en-US", "en-GB" })
        {
            if (metadata.LocalizedDescriptions.TryGetValue(englishLanguage, out var localized) &&
                !IsLowQualityDescription(localized))
            {
                return localized;
            }
        }

        return !IsLowQualityDescription(metadata.Description)
            ? metadata.Description
            : null;
    }

    private async Task<string?> FetchEnglishFallbackDescriptionAsync(
        Game game,
        CancellationToken token)
    {
        string? english = null;

        if (game.Platform == GamePlatform.Steam && int.TryParse(game.Id, out var installedAppId))
        {
            english = await GetSteamLocalizedDescriptionAsync(installedAppId, "en-US", token);
        }
        else if (game.Metadata.Source?.Contains("Steam Store", StringComparison.OrdinalIgnoreCase) == true &&
                 int.TryParse(game.Metadata.ExternalId, out var relatedSteamAppId))
        {
            english = await GetSteamLocalizedDescriptionAsync(relatedSteamAppId, "en-US", token);
        }

        if (IsLowQualityDescription(english))
            english = await GetLocalizedWikipediaDescriptionAsync(game.Name, "en-US", token);

        return english;
    }

    private async Task<string?> GetSteamLocalizedDescriptionAsync(
        int appId,
        string language,
        CancellationToken token)
    {
        var (steamLanguage, countryCode) = GetSteamLocale(language);

        await _steamStoreGate.WaitAsync(token);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var url = $"https://store.steampowered.com/api/appdetails?appids={appId}" +
                              $"&l={Uri.EscapeDataString(steamLanguage)}&cc={Uri.EscapeDataString(countryCode)}";
                    using var response = await _http.GetAsync(url, token);
                    if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                    {
                        if (attempt == 0)
                        {
                            await Task.Delay(600, token);
                            continue;
                        }
                        break;
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(
                            response, maxBytes: 2 * 1024 * 1024, cancellationToken: token));
                        var appIdText = appId.ToString(CultureInfo.InvariantCulture);
                        if (doc.RootElement.TryGetProperty(appIdText, out var entry) &&
                            entry.TryGetProperty("success", out var success) && success.GetBoolean() &&
                            entry.TryGetProperty("data", out var data) &&
                            data.TryGetProperty("short_description", out var description))
                        {
                            var text = SanitizeDescription(description.GetString());
                            if (!IsLowQualityDescription(text))
                                return text;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch when (attempt == 0)
                {
                    await Task.Delay(500, token);
                }
                catch
                {
                    break;
                }
            }

            // Fallback para layouts em que appdetails não retorna short_description.
            try
            {
                var pageUrl = $"https://store.steampowered.com/app/{appId}/" +
                              $"?l={Uri.EscapeDataString(steamLanguage)}&cc={Uri.EscapeDataString(countryCode)}";
                using var response = await _http.GetAsync(pageUrl, token);
                if (!response.IsSuccessStatusCode)
                    return null;

                var html = await SafeHttpResponseService.ReadTextAsync(
                    response, maxBytes: 4 * 1024 * 1024, cancellationToken: token);

                var snippet = Regex.Match(
                    html,
                    @"<div[^>]+class=[""'][^""']*game_description_snippet[^""']*[""'][^>]*>(?<value>.*?)</div>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
                if (snippet.Success)
                {
                    var text = Regex.Replace(snippet.Groups["value"].Value, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
                    text = Regex.Replace(text, @"<[^>]+>", " ");
                    text = WebUtility.HtmlDecode(text);
                    text = Regex.Replace(text, @"\s+", " ").Trim();
                    text = SanitizeDescription(text);
                    if (!IsLowQualityDescription(text))
                        return text;
                }

                foreach (var pattern in new[]
                {
                    @"<meta[^>]+property=[""']og:description[""'][^>]+content=[""'](?<value>.*?)[""']",
                    @"<meta[^>]+content=[""'](?<value>.*?)[""'][^>]+property=[""']og:description[""']"
                })
                {
                    var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
                    if (!match.Success) continue;
                    var text = SanitizeDescription(WebUtility.HtmlDecode(match.Groups["value"].Value));
                    if (!IsLowQualityDescription(text))
                        return text;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }
        finally
        {
            _steamStoreGate.Release();
        }

        return null;
    }

    private async Task<string?> GetLocalizedWikipediaDescriptionAsync(
        string? gameName,
        string language,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(gameName))
            return null;

        var wikiLanguage = GetWikipediaLanguage(language);
        var title = gameName.Trim();
        var qualifiers = wikiLanguage switch
        {
            "pt" => new[] { title, $"{title} (jogo eletrônico)", $"{title} (videojogo)" },
            "es" => new[] { title, $"{title} (videojuego)" },
            _ => new[] { title, $"{title} (video game)" }
        };

        foreach (var candidate in qualifiers)
        {
            var summary = await GetWikipediaSummaryAsync(candidate, token, wikiLanguage);
            if (!IsLowQualityDescription(summary) && LooksLikeVideoGameSummary(summary, wikiLanguage))
                return summary;
        }

        try
        {
            var gameTerm = wikiLanguage switch
            {
                "pt" => "jogo eletrônico",
                "es" => "videojuego",
                _ => "video game"
            };
            var query = Uri.EscapeDataString($"\"{title}\" {gameTerm}");
            var url = $"https://{wikiLanguage}.wikipedia.org/w/api.php?action=query&list=search" +
                      $"&srsearch={query}&srlimit=6&format=json&utf8=1";
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(
                response, maxBytes: 1024 * 1024, cancellationToken: token));
            if (!doc.RootElement.TryGetProperty("query", out var q) ||
                !q.TryGetProperty("search", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in results.EnumerateArray())
            {
                var resultTitle = item.TryGetProperty("title", out var titleElement)
                    ? titleElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(resultTitle))
                    continue;

                var normalizedResult = NormalizeTitle(Regex.Replace(resultTitle, @"\s*\([^)]*\)\s*$", ""));
                if (normalizedResult != NormalizeTitle(title))
                    continue;

                var summary = await GetWikipediaSummaryAsync(resultTitle, token, wikiLanguage);
                if (!IsLowQualityDescription(summary) && LooksLikeVideoGameSummary(summary, wikiLanguage))
                    return summary;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        return null;
    }

    private static bool LooksLikeVideoGameSummary(string? summary, string wikiLanguage)
    {
        if (IsLowQualityDescription(summary))
            return false;

        var text = summary!.ToLowerInvariant();
        return wikiLanguage switch
        {
            "pt" => text.Contains("jogo", StringComparison.Ordinal) ||
                    text.Contains("videojogo", StringComparison.Ordinal) ||
                    text.Contains("videogame", StringComparison.Ordinal),
            "es" => text.Contains("videojuego", StringComparison.Ordinal) ||
                    text.Contains("juego", StringComparison.Ordinal),
            _ => text.Contains("game", StringComparison.Ordinal)
        };
    }

    private static string NormalizeDescriptionLanguage(string? language) => language switch
    {
        "pt-PT" => "pt-PT",
        "en-US" => "en-US",
        "en-GB" => "en-GB",
        "es-419" => "es-419",
        "es-ES" => "es-ES",
        _ => "pt-BR"
    };

    private static IEnumerable<string> GetSameLanguageFallbacks(string language) => language switch
    {
        "pt-BR" => new[] { "pt-PT" },
        "pt-PT" => new[] { "pt-BR" },
        "en-US" => new[] { "en-GB" },
        "en-GB" => new[] { "en-US" },
        "es-419" => new[] { "es-ES" },
        "es-ES" => new[] { "es-419" },
        _ => Array.Empty<string>()
    };

    private static (string Language, string Country) GetSteamLocale(string language) => language switch
    {
        "pt-BR" => ("brazilian", "BR"),
        "pt-PT" => ("portuguese", "PT"),
        "es-419" => ("latam", "MX"),
        "es-ES" => ("spanish", "ES"),
        "en-GB" => ("english", "GB"),
        _ => ("english", "US")
    };

    private static string GetWikipediaLanguage(string language) => language switch
    {
        "pt-BR" or "pt-PT" => "pt",
        "es-419" or "es-ES" => "es",
        _ => "en"
    };

    private static void Apply(Game game, GameMetadata metadata)
    {
        // Limpa caches antigos e normaliza os gêneros antes de qualquer exibição.
        metadata.Description = SanitizeDescription(metadata.Description);
        metadata.LocalizedDescriptions ??= new(StringComparer.OrdinalIgnoreCase);
        metadata.AgeRatings ??= new(StringComparer.OrdinalIgnoreCase);
        metadata.Source = NormalizeMetadataSources(metadata.Source);
        GenreService.NormalizeInPlace(metadata);
        game.Metadata = metadata;
        if (!string.IsNullOrWhiteSpace(metadata.CanonicalName) &&
            (game.Platform == GamePlatform.Steam || TitlesMatch(game.Name, metadata.CanonicalName)))
        {
            game.Name = metadata.CanonicalName;
        }
        // Não troca uma capa já selecionada pela UI durante enriquecimento.
        // MainWindow.ApplyCoverMode() decide entre vertical/horizontal. Sobrescrever
        // aqui fazia o modo Vertical mostrar temporariamente CoverLocalPath (horizontal)
        // enquanto uma atualização de metadados/arte ainda estava em andamento.
        if (string.IsNullOrWhiteSpace(game.CoverImage))
            game.CoverImage = metadata.CoverLocalPath;
    }

    /// <summary>
    /// Revalida artes oficiais da Steam sem bloquear a publicação da biblioteca.
    /// Em uso normal, somente arquivos com mais de 24 horas são atualizados;
    /// uma atualização manual pode forçar a consulta imediatamente.
    /// </summary>
    public async Task RefreshSteamArtworkAsync(
        IEnumerable<Game> games,
        bool forceRefresh,
        CancellationToken token = default)
    {
        using var gate = new SemaphoreSlim(6);
        var tasks = games
            .Where(game => game.Platform == GamePlatform.Steam && int.TryParse(game.Id, out _))
            .Select(async game =>
            {
                await gate.WaitAsync(token);
                try
                {
                    var metadata = game.Metadata;
                    var changed = false;
                    var shouldForceOfficialArtworkRefresh = forceRefresh || ShouldForceSteamArtworkRefresh(game);

                    var verticalUrl = BuildSteamLibraryCapsuleUrl(game);
                    metadata.VerticalCoverUrl = verticalUrl;

                    if (shouldForceOfficialArtworkRefresh ||
                        IsCoverStale(metadata.VerticalCoverLocalPath, SteamArtworkMaxAge) ||
                        !IsExpectedImageSize(metadata.VerticalCoverLocalPath, 600, 900))
                    {
                        var refreshed = await DownloadCoverAsync(
                            AddCacheBuster(verticalUrl),
                            game.ProviderId + "_vertical",
                            token,
                            force: true);

                        if (!string.IsNullOrWhiteSpace(refreshed))
                        {
                            metadata.VerticalCoverLocalPath = refreshed;
                            changed = true;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(metadata.HorizontalCoverUrl) &&
                        (shouldForceOfficialArtworkRefresh || IsCoverStale(metadata.HorizontalCoverLocalPath, SteamArtworkMaxAge)))
                    {
                        var refreshed = await DownloadCoverAsync(
                            AddCacheBuster(metadata.HorizontalCoverUrl),
                            game.ProviderId + "_horizontal",
                            token,
                            force: true);

                        if (!string.IsNullOrWhiteSpace(refreshed))
                        {
                            metadata.HorizontalCoverLocalPath = refreshed;
                            changed = true;
                        }
                    }

                    metadata.CoverLocalPath = metadata.HorizontalCoverLocalPath ?? metadata.VerticalCoverLocalPath;

                    if (changed)
                    {
                        lock (_cacheSync)
                            _cache[game.ProviderId] = metadata;
                        Apply(game, metadata);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Falha ao atualizar uma arte não pode impedir as demais.
                }
                finally
                {
                    gate.Release();
                }
            });

        await Task.WhenAll(tasks);
        SaveCache();
    }

    private static bool ShouldForceSteamArtworkRefresh(Game game)
        => int.TryParse(game.Id, out var appId) && KnownSteamArtworkRefreshAppIds.Contains(appId);

    private static string AddCacheBuster(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return url + separator + "pcg_refresh=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static bool IsCoverStale(string? path, TimeSpan maxAge)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return true;

        try
        {
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= maxAge;
        }
        catch
        {
            return true;
        }
    }

    private async Task<GameMetadata?> GetSteamMetadataAsync(Game game, CancellationToken token)
    {
        var candidateIds = new List<int>();
        if (int.TryParse(game.Id, out var appId))
            candidateIds.Add(appId);

        if (KnownSteamAppIds.TryGetValue(NormalizeTitle(game.Name), out var knownAppId) &&
            !candidateIds.Contains(knownAppId))
        {
            candidateIds.Add(knownAppId);
        }

        foreach (var candidateId in candidateIds)
        {
            // Quando o App ID veio diretamente do manifesto instalado da Steam,
            // ele é a identidade autoritativa do jogo. Não rejeitamos a resposta
            // apenas porque o nome do manifesto difere do nome comercial atual
            // (por exemplo, edições Complete/Director's Cut ou mudanças de título).
            var isInstalledAppId = int.TryParse(game.Id, out var installedAppId) &&
                                   candidateId == installedAppId;

            var metadata = await GetSteamMetadataByAppIdAsync(candidateId, game, "Steam Store", token);

            // A API appdetails nem sempre entrega todos os campos em títulos antigos,
            // delistados ou com respostas regionais incompletas. Antes de recorrer a
            // qualquer fonte externa, consulta a própria página oficial da Steam pelo
            // mesmo App ID e usa-a apenas para completar o que estiver faltando.
            if (metadata is null || !HasCompleteTextMetadata(metadata))
            {
                var pageMetadata = await GetSteamStorePageMetadataByAppIdAsync(
                    candidateId,
                    game,
                    token,
                    trustAppId: isInstalledAppId);
                metadata = MergeMetadata(metadata, pageMetadata);
            }

            if (metadata is not null && (isInstalledAppId || TitlesMatch(game.Name, metadata.CanonicalName)))
                return metadata;

            // Uma falha transitória da Store API não deve fazer o cache antigo de
            // Wikipedia/GOG vencer por mais uma execução. Tenta uma segunda vez.
            await Task.Delay(250, token);
            metadata = await GetSteamMetadataByAppIdAsync(candidateId, game, "Steam Store", token);
            if (metadata is null || !HasCompleteTextMetadata(metadata))
            {
                var pageMetadata = await GetSteamStorePageMetadataByAppIdAsync(
                    candidateId,
                    game,
                    token,
                    trustAppId: isInstalledAppId);
                metadata = MergeMetadata(metadata, pageMetadata);
            }
            if (metadata is not null && (isInstalledAppId || TitlesMatch(game.Name, metadata.CanonicalName)))
                return metadata;
        }

        return null;
    }

    private async Task<GameMetadata?> GetSteamMetadataByAppIdAsync(
        int appId,
        Game game,
        string source,
        CancellationToken token)
    {
        await _steamStoreGate.WaitAsync(token);
        try
        {
            // A Store API começa a responder com 429/5xx quando muitas entradas são
            // consultadas de uma vez. Isso fazia apenas alguns jogos serem corrigidos
            // por clique. Mantemos no máximo duas consultas Steam simultâneas e fazemos
            // retries curtos dentro da própria tentativa do jogo.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english&cc=US";
                    using var response = await _http.GetAsync(url, token);

                    if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                    {
                        if (attempt < 2)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(700 * (attempt + 1)), token);
                            continue;
                        }

                        return null;
                    }

                    if (!response.IsSuccessStatusCode)
                        return null;

                    using var doc = JsonDocument.Parse(
                        await SafeHttpResponseService.ReadTextAsync(response, cancellationToken: token));
                    var appIdText = appId.ToString(CultureInfo.InvariantCulture);
                    if (!doc.RootElement.TryGetProperty(appIdText, out var entry) ||
                        !entry.TryGetProperty("success", out var ok) || !ok.GetBoolean())
                    {
                        if (attempt < 2)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), token);
                            continue;
                        }

                        return null;
                    }

                    var data = entry.GetProperty("data");
                    var metadata = new GameMetadata
                    {
                        CanonicalName = data.TryGetProperty("name", out var name)
                            ? name.GetString() ?? game.Name
                            : game.Name,
                        Description = data.TryGetProperty("short_description", out var description)
                            ? SanitizeDescription(description.GetString())
                            : null,
                        CoverUrl = data.TryGetProperty("header_image", out var cover) ? cover.GetString() : null,
                        HorizontalCoverUrl = data.TryGetProperty("header_image", out var horizontal) ? horizontal.GetString() : null,
                        VerticalCoverUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
                        Source = source,
                        ExternalId = appIdText,
                        Developer = data.TryGetProperty("developers", out var developers) && developers.ValueKind == JsonValueKind.Array && developers.GetArrayLength() > 0
                            ? developers[0].GetString()
                            : null,
                        Publisher = data.TryGetProperty("publishers", out var publishers) && publishers.ValueKind == JsonValueKind.Array && publishers.GetArrayLength() > 0
                            ? publishers[0].GetString()
                            : null
                    };

                    if (data.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var genre in genres.EnumerateArray())
                        {
                            if (genre.TryGetProperty("description", out var value))
                                AddGenre(value.GetString(), metadata.Genres);
                        }
                    }

                    if (data.TryGetProperty("ratings", out var ratings) && ratings.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var ratingProperty in ratings.EnumerateObject())
                        {
                            var ratingValue = ReadSteamAgeRatingValue(ratingProperty.Value);
                            if (!string.IsNullOrWhiteSpace(ratingValue))
                                metadata.AgeRatings[ratingProperty.Name] = ratingValue;
                        }
                    }

                    if (data.TryGetProperty("release_date", out var releaseDate) &&
                        releaseDate.TryGetProperty("date", out var date))
                    {
                        var text = date.GetString();
                        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedDate) ||
                            DateTime.TryParse(text, out parsedDate))
                        {
                            metadata.ReleaseYear = parsedDate.Year;
                        }
                    }

                    ApplyKnownMetadataCorrection(game, metadata);
                    GenreService.NormalizeInPlace(metadata);
                    return metadata;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch when (attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(600 * (attempt + 1)), token);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
        finally
        {
            _steamStoreGate.Release();
        }
    }


    private async Task<GameMetadata?> GetSteamStorePageMetadataByAppIdAsync(
        int appId,
        Game game,
        CancellationToken token,
        bool trustAppId = false)
    {
        await _steamStoreGate.WaitAsync(token);
        try
        {
            var url = $"https://store.steampowered.com/app/{appId}/?l=english&cc=US";
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
                return null;

            var html = await SafeHttpResponseService.ReadTextAsync(
                response, maxBytes: 4 * 1024 * 1024, cancellationToken: token);
            if (string.IsNullOrWhiteSpace(html))
                return null;

            static string? Extract(string htmlText, string pattern)
            {
                var match = Regex.Match(
                    htmlText, pattern,
                    RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
                if (!match.Success) return null;
                var raw = match.Groups["value"].Value;
                raw = Regex.Replace(raw, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
                raw = Regex.Replace(raw, @"<[^>]+>", " ");
                raw = WebUtility.HtmlDecode(raw);
                raw = Regex.Replace(raw, @"\s+", " ").Trim();
                return string.IsNullOrWhiteSpace(raw) ? null : raw;
            }

            static string? ExtractMetaContent(string htmlText, string property)
            {
                var escaped = Regex.Escape(property);
                foreach (var pattern in new[]
                {
                    $@"<meta[^>]+property=[""']{escaped}[""'][^>]+content=[""'](?<value>.*?)[""']",
                    $@"<meta[^>]+content=[""'](?<value>.*?)[""'][^>]+property=[""']{escaped}[""']"
                })
                {
                    var match = Regex.Match(
                        htmlText,
                        pattern,
                        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
                    if (!match.Success)
                        continue;

                    var value = WebUtility.HtmlDecode(match.Groups["value"].Value);
                    value = Regex.Replace(value, @"\s+", " ").Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }

                return null;
            }

            var name = Extract(html, @"<div[^>]+class=[""'][^""']*apphub_AppName[^""']*[""'][^>]*>(?<value>.*?)</div>") ?? game.Name;
            if (!trustAppId && !TitlesMatch(game.Name, name))
                return null;

            var metadata = new GameMetadata
            {
                CanonicalName = name,
                Source = "Steam Store",
                ExternalId = appId.ToString(CultureInfo.InvariantCulture),
                VerticalCoverUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg"
            };

            metadata.Description = SanitizeDescription(Extract(
                html, @"<div[^>]+class=[""'][^""']*game_description_snippet[^""']*[""'][^>]*>(?<value>.*?)</div>"));

            // Alguns layouts da loja não renderizam game_description_snippet,
            // mas ainda expõem uma descrição oficial nos metadados Open Graph.
            if (IsLowQualityDescription(metadata.Description))
            {
                metadata.Description = SanitizeDescription(ExtractMetaContent(
                    html,
                    "og:description"));
            }

            metadata.Developer = SanitizeCompanyField(Extract(
                html, @"<div[^>]+class=[""'][^""']*subtitle\s+column[^""']*[""'][^>]*>\s*Developer:\s*</div>\s*<div[^>]+class=[""'][^""']*summary\s+column[^""']*[""'][^>]*>(?<value>.*?)</div>"));
            metadata.Publisher = SanitizeCompanyField(Extract(
                html, @"<div[^>]+class=[""'][^""']*subtitle\s+column[^""']*[""'][^>]*>\s*Publisher:\s*</div>\s*<div[^>]+class=[""'][^""']*summary\s+column[^""']*[""'][^>]*>(?<value>.*?)</div>"));

            var release = Extract(html, @"<div[^>]+class=[""'][^""']*release_date[^""']*[""'][^>]*>.*?<div[^>]+class=[""'][^""']*date[^""']*[""'][^>]*>(?<value>.*?)</div>");
            metadata.ReleaseYear = TryExtractYear(release) ?? TryExtractSteamReleaseYearFromHtml(html);

            var genreText = Extract(html, @"<b>\s*Genre:\s*</b>\s*(?<value>.*?)(?:<br\s*/?>|</div>)");
            if (!string.IsNullOrWhiteSpace(genreText))
            {
                foreach (var genre in genreText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    AddGenre(genre, metadata.Genres);
            }

            // Alguns layouts antigos não exibem o bloco Genre no mesmo formato.
            // Nesse caso, usa apenas tags reconhecidas pelo nosso normalizador,
            // descartando automaticamente tags que não são gêneros reais.
            if (metadata.Genres.Count == 0)
            {
                foreach (Match tag in Regex.Matches(
                    html,
                    @"<a[^>]+class=[""'][^""']*app_tag[^""']*[""'][^>]*>(?<value>.*?)</a>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
                {
                    var value = Regex.Replace(tag.Groups["value"].Value, @"<[^>]+>", " ");
                    value = WebUtility.HtmlDecode(value);
                    AddGenre(value, metadata.Genres);
                }
            }

            ExtractSteamAgeRatingsFromHtml(html, "en-US", metadata.AgeRatings);

            var header = Regex.Match(
                html,
                @"<img[^>]+class=[""'][^""']*game_header_image_full[^""']*[""'][^>]+src=[""'](?<value>[^""']+)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (header.Success)
            {
                metadata.HorizontalCoverUrl = WebUtility.HtmlDecode(header.Groups["value"].Value);
                metadata.CoverUrl = metadata.HorizontalCoverUrl;
            }

            CompleteStructuredMetadataFromDescription(metadata);
            SanitizeCompanyFields(metadata);
            GenreService.NormalizeInPlace(metadata);
            return metadata;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            _steamStoreGate.Release();
        }
    }

    /// <summary>
    /// Para jogos de outros launchers, tenta localizar o mesmo título na Steam Store.
    /// Primeiro usa a API leve de busca. Se ela falhar ou retornar uma estrutura
    /// inesperada, usa a página de busca da própria loja como fallback.
    /// Somente correspondências exatas pelo nome normalizado são aceitas.
    /// </summary>
    private async Task<GameMetadata?> GetSteamMetadataByExactNameAsync(Game game, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(game.Name))
            return null;

        // Alguns títulos usam pontuação que nem sempre é tratada de forma consistente
        // pelos endpoints de pesquisa da Steam. Quando há um App ID conhecido e
        // verificado, consulta appdetails diretamente e ainda valida o nome canônico.
        var normalizedName = NormalizeTitle(game.Name);
        if (KnownSteamAppIds.TryGetValue(normalizedName, out var knownAppId))
        {
            var knownMetadata = await GetSteamMetadataByAppIdAsync(
                knownAppId,
                game,
                "Steam Store (App ID conhecido)",
                token);

            if (knownMetadata is not null && TitlesMatch(game.Name, knownMetadata.CanonicalName))
            {
                ApplyKnownMetadataCorrection(game, knownMetadata);
                return knownMetadata;
            }
        }

        // Caminho rápido: estas pesquisas já validam o nome retornado pela própria
        // Steam. Elas existiam no projeto, mas não eram usadas pelo fluxo principal.
        // Consultá-las primeiro evita carregar a lista global de apps para títulos
        // comuns de Epic, EA e Ubisoft que também possuem uma página na Steam.
        var exactLookupTasks = new Task<int?>[]
        {
            FindSteamAppIdByStoreSearchApiAsync(game.Name, token),
            FindSteamAppIdBySuggestAsync(game.Name, token),
            FindSteamAppIdBySearchPageAsync(game.Name, token)
        };

        var exactAppIds = (await Task.WhenAll(exactLookupTasks))
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        foreach (var appId in exactAppIds)
        {
            var exactMetadata = await GetSteamMetadataByAppIdAsync(
                appId,
                game,
                "Steam Store (nome exato)",
                token);

            if (exactMetadata is not null && TitlesMatch(game.Name, exactMetadata.CanonicalName))
                return exactMetadata;
        }

        // Fallback amplo: a pesquisa da Steam pode variar conforme região, pontuação
        // do título e pequenas mudanças no HTML. Coletamos alguns App IDs candidatos
        // e validamos o nome canônico retornado por appdetails.
        var candidates = await FindSteamCandidateAppIdsAsync(game.Name, token);
        if (candidates.Count == 0)
            return null;

        using var gate = new SemaphoreSlim(4);
        var tasks = candidates.Take(12).Select(async appId =>
        {
            await gate.WaitAsync(token);
            try
            {
                var candidate = await GetSteamMetadataByAppIdAsync(
                    appId,
                    game,
                    "Steam Store (correspondência exata)",
                    token);

                return candidate is not null && TitlesMatch(game.Name, candidate.CanonicalName)
                    ? candidate
                    : null;
            }
            finally
            {
                gate.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.FirstOrDefault(metadata => metadata is not null);
    }

    private async Task<List<int>> FindSteamCandidateAppIdsAsync(string gameName, CancellationToken token)
    {
        var queries = BuildSteamSearchQueries(gameName);
        var tasks = queries.SelectMany(query => new[]
        {
            FindSteamCandidatesByStoreSearchApiAsync(query, token),
            FindSteamCandidatesBySuggestAsync(query, token),
            FindSteamCandidatesBySearchPageAsync(query, token),
            FindSteamCandidatesBySearchResultsAsync(query, token)
        }).ToArray();

        try
        {
            var results = await Task.WhenAll(tasks);
            var candidateIds = results
                .SelectMany(ids => ids)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            // A lista oficial da Steam é muito grande. Ela só deve ser carregada quando
            // as buscas leves não produziram nenhum candidato; antes, aguardávamos essa
            // lista mesmo já tendo IDs válidos, consumindo quase todo o timeout do jogo
            // e impedindo o appdetails de ser consultado em Epic/EA.
            if (candidateIds.Count == 0)
            {
                foreach (var appId in await FindSteamCandidatesByOfficialAppListAsync(gameName, token))
                {
                    if (!candidateIds.Contains(appId))
                        candidateIds.Add(appId);
                }
            }

            return candidateIds.Take(20).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new List<int>();
        }
    }

    private static IReadOnlyList<string> BuildSteamSearchQueries(string gameName)
    {
        var original = gameName.Trim();
        var simplified = Regex.Replace(original, @"[^\p{L}\p{N}]+", " ").Trim();

        return new[] { original, simplified }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<List<int>> FindSteamCandidatesByOfficialAppListAsync(
        string gameName,
        CancellationToken token)
    {
        var normalized = NormalizeTitle(gameName);
        if (string.IsNullOrWhiteSpace(normalized))
            return new List<int>();

        await _steamAppListGate.WaitAsync(token);
        try
        {
            if (_steamAppIdsByNormalizedName is null)
            {
                var index = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                using var response = await _http.GetAsync(
                    "https://api.steampowered.com/ISteamApps/GetAppList/v2/?format=json",
                    token);

                if (!response.IsSuccessStatusCode)
                    return new List<int>();

                var json = await SafeHttpResponseService.ReadTextAsync(
                    response,
                    maxBytes: 32 * 1024 * 1024,
                    cancellationToken: token);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("applist", out var appList) &&
                    appList.TryGetProperty("apps", out var apps) &&
                    apps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var app in apps.EnumerateArray())
                    {
                        if (!app.TryGetProperty("appid", out var appIdElement) ||
                            !appIdElement.TryGetInt32(out var appId) ||
                            !app.TryGetProperty("name", out var nameElement))
                        {
                            continue;
                        }

                        var name = nameElement.GetString();
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        var key = NormalizeTitle(name);
                        if (string.IsNullOrWhiteSpace(key))
                            continue;

                        if (!index.TryGetValue(key, out var ids))
                        {
                            ids = new List<int>();
                            index[key] = ids;
                        }

                        if (ids.Count < 8)
                            ids.Add(appId);
                    }
                }

                _steamAppIdsByNormalizedName = index;
            }

            return _steamAppIdsByNormalizedName.TryGetValue(normalized, out var matches)
                ? matches.Take(8).ToList()
                : new List<int>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new List<int>();
        }
        finally
        {
            _steamAppListGate.Release();
        }
    }

    private async Task<List<int>> FindSteamCandidatesByStoreSearchApiAsync(
        string gameName,
        CancellationToken token)
    {
        var result = new List<int>();
        try
        {
            var term = Uri.EscapeDataString(gameName);
            var url = $"https://store.steampowered.com/api/storesearch/?term={term}&l=english&cc=US";
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
                return result;

            using var doc = JsonDocument.Parse(
                await SafeHttpResponseService.ReadTextAsync(response, cancellationToken: token));
            if (!doc.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in items.EnumerateArray().Take(12))
            {
                if (item.TryGetProperty("id", out var idElement) &&
                    idElement.TryGetInt32(out var appId))
                    result.Add(appId);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        return result;
    }

    private async Task<List<int>> FindSteamCandidatesBySuggestAsync(
        string gameName,
        CancellationToken token)
    {
        var result = new List<int>();
        try
        {
            var term = Uri.EscapeDataString(gameName);
            var url = $"https://store.steampowered.com/search/suggest?term={term}&f=games&cc=US&l=english";
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
                return result;

            var html = await SafeHttpResponseService.ReadTextAsync(
                response,
                maxBytes: 1024 * 1024,
                cancellationToken: token);

            foreach (Match match in Regex.Matches(
                         html,
                         @"(?:data-ds-appid=[""'](?<id>\d+)[""']|/app/(?<id2>\d+)/)",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                var idText = match.Groups["id"].Success
                    ? match.Groups["id"].Value
                    : match.Groups["id2"].Value;
                if (int.TryParse(idText, out var appId))
                    result.Add(appId);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        return result.Distinct().Take(12).ToList();
    }

    private async Task<List<int>> FindSteamCandidatesBySearchResultsAsync(
        string gameName,
        CancellationToken token)
    {
        var result = new List<int>();
        try
        {
            var term = Uri.EscapeDataString(gameName);
            var url =
                $"https://store.steampowered.com/search/results/?term={term}" +
                "&start=0&count=30&dynamic_data=&sort_by=_ASC&category1=998" +
                "&infinite=1&l=english&cc=US";

            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
                return result;

            var payload = await SafeHttpResponseService.ReadTextAsync(
                response,
                maxBytes: 2 * 1024 * 1024,
                cancellationToken: token);

            // O endpoint normalmente retorna JSON com results_html. Se a Steam
            // responder HTML diretamente, o mesmo extrator continua funcionando.
            var html = payload;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("results_html", out var htmlElement) &&
                    htmlElement.ValueKind == JsonValueKind.String)
                {
                    html = htmlElement.GetString() ?? payload;
                }
            }
            catch (JsonException)
            {
                // Resposta HTML direta: segue para a extração abaixo.
            }

            foreach (Match match in Regex.Matches(
                         html,
                         @"(?:data-ds-appid=[""'](?<id>\d+)[""']|/app/(?<id2>\d+)/)",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                var idText = match.Groups["id"].Success
                    ? match.Groups["id"].Value
                    : match.Groups["id2"].Value;

                if (int.TryParse(idText, out var appId))
                    result.Add(appId);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Metadados externos são opcionais.
        }

        return result.Distinct().Take(20).ToList();
    }

    private async Task<List<int>> FindSteamCandidatesBySearchPageAsync(
        string gameName,
        CancellationToken token)
    {
        var result = new List<int>();
        try
        {
            var term = Uri.EscapeDataString(gameName);
            var url = $"https://store.steampowered.com/search/?term={term}&category1=998&l=english&cc=US";
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
                return result;

            var html = await SafeHttpResponseService.ReadTextAsync(
                response,
                maxBytes: 2 * 1024 * 1024,
                cancellationToken: token);

            foreach (Match match in Regex.Matches(
                         html,
                         @"(?:data-ds-appid=[""'](?<id>\d+)[""']|/app/(?<id2>\d+)/)",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                var idText = match.Groups["id"].Success
                    ? match.Groups["id"].Value
                    : match.Groups["id2"].Value;
                if (int.TryParse(idText, out var appId))
                    result.Add(appId);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        return result.Distinct().Take(12).ToList();
    }

    private async Task<int?> FindSteamAppIdByStoreSearchApiAsync(string gameName, CancellationToken token)
    {
        try
        {
            var term = Uri.EscapeDataString(gameName.Trim());
            var url = $"https://store.steampowered.com/api/storesearch/?term={term}&l=english&cc=US";
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(response, cancellationToken: token));
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return null;

            var normalizedTarget = NormalizeTitle(gameName);
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var candidateId))
                    continue;
                if (!item.TryGetProperty("name", out var nameElement))
                    continue;

                var candidateName = nameElement.GetString();
                if (!string.IsNullOrWhiteSpace(candidateName) && NormalizeTitle(candidateName) == normalizedTarget)
                    return candidateId;
            }
        }
        catch
        {
            // O fallback HTML abaixo ainda pode resolver o App ID.
        }

        return null;
    }

    private async Task<int?> FindSteamAppIdBySuggestAsync(string gameName, CancellationToken token)
    {
        try
        {
            var term = Uri.EscapeDataString(gameName.Trim());
            var url = $"https://store.steampowered.com/search/suggest?term={term}&f=games&cc=US&l=english";
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
                return null;

            var html = await SafeHttpResponseService.ReadTextAsync(
                response,
                maxBytes: 1024 * 1024,
                cancellationToken: token);

            var target = NormalizeTitle(gameName);
            var rows = Regex.Matches(
                html,
                "<a[^>]+(?:data-ds-appid=\"(?<id>\\d+)\"|href=\"https?://store\\.steampowered\\.com/app/(?<id2>\\d+)/)[^>]*>[\\s\\S]*?<div[^>]+class=\"match_name\"[^>]*>(?<name>[\\s\\S]*?)</div>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            foreach (Match row in rows)
            {
                var idText = row.Groups["id"].Success ? row.Groups["id"].Value : row.Groups["id2"].Value;
                if (!int.TryParse(idText, out var appId))
                    continue;

                var candidateName = WebUtility.HtmlDecode(Regex.Replace(row.Groups["name"].Value, "<[^>]+>", string.Empty)).Trim();
                if (NormalizeTitle(candidateName) == target)
                    return appId;
            }
        }
        catch
        {
            // O próximo fallback ainda pode resolver o App ID.
        }

        return null;
    }

    private async Task<int?> FindSteamAppIdBySearchPageAsync(string gameName, CancellationToken token)
    {
        try
        {
            var term = Uri.EscapeDataString(gameName.Trim());
            var url = $"https://store.steampowered.com/search/?term={term}&category1=998&l=english&cc=US";
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
                return null;

            var html = await SafeHttpResponseService.ReadTextAsync(
                response,
                maxBytes: 2 * 1024 * 1024,
                cancellationToken: token);

            var target = NormalizeTitle(gameName);
            var rows = Regex.Matches(
                html,
                @"<a[^>]+href=""https?://store\.steampowered\.com/app/(?<id>\d+)/[^""]*""[^>]*>[\s\S]*?<span[^>]+class=""title""[^>]*>(?<name>[\s\S]*?)</span>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            foreach (Match row in rows)
            {
                if (!int.TryParse(row.Groups["id"].Value, out var appId))
                    continue;

                var rawName = Regex.Replace(row.Groups["name"].Value, "<[^>]+>", string.Empty);
                var candidateName = WebUtility.HtmlDecode(rawName).Trim();
                if (NormalizeTitle(candidateName) == target)
                    return appId;
            }
        }
        catch
        {
            // Metadados externos são opcionais.
        }

        return null;
    }

    private async Task<GameMetadata?> MergePcGamingWikiAsync(
        Game game,
        GameMetadata? current,
        CancellationToken token)
    {
        try
        {
            var pcgw = await _pcGamingWiki.GetMetadataByExactTitleAsync(game.Name, token);
            current = MergeMetadata(current, pcgw);

            // PCGamingWiki costuma registrar o Steam App ID no infobox. Se a busca
            // textual da Steam falhou, esse ID permite uma consulta exata ao appdetails.
            if (pcgw is not null &&
                int.TryParse(pcgw.ExternalId, out var steamAppId) &&
                !HasCompleteTextMetadata(current))
            {
                var steamByPcgw = await GetSteamMetadataByAppIdAsync(
                    steamAppId,
                    game,
                    "Steam Store (ID via PCGamingWiki)",
                    token);

                if (steamByPcgw is not null && TitlesMatch(game.Name, steamByPcgw.CanonicalName))
                    current = MergeMetadata(current, steamByPcgw);
            }

            return current;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return current;
        }
    }

    /// <summary>
    /// Busca metadados públicos diretamente no GOG usando o Product ID detectado
    /// pelo Galaxy/manifesto local. O endpoint v2 é preferido; o endpoint antigo
    /// fica apenas como fallback para instalações/produtos legados.
    /// </summary>
    private async Task<GameMetadata?> GetGogMetadataAsync(Game game, CancellationToken token)
    {
        GameMetadata? combined = null;
        string? productId = null;

        // Primeiro tenta o Product ID detectado localmente. Só aceita a resposta se
        // o título corresponder ao jogo, evitando IDs incorretos/legados do Galaxy.
        if (!string.IsNullOrWhiteSpace(game.Id) && game.Id.All(char.IsDigit))
        {
            var byId = await GetGogMetadataByProductIdAsync(game.Id, game, token);
            if (byId is not null && TitlesMatch(game.Name, byId.CanonicalName))
            {
                combined = MergeMetadata(combined, byId);
                productId = game.Id;
            }
        }

        // O catálogo GOG expõe campos que nem sempre aparecem nos endpoints de
        // produto (principalmente gêneros). Por isso ele é combinado mesmo quando
        // o Product ID local já foi validado.
        var catalogMatch = await FindGogCatalogMetadataByExactNameAsync(game, token);
        combined = MergeMetadata(combined, catalogMatch.Metadata);
        productId ??= catalogMatch.ProductId;

        // Se o ID local não serviu, usa o ID correto encontrado pelo catálogo e
        // combina novamente com os endpoints detalhados do produto.
        if (!string.IsNullOrWhiteSpace(productId) &&
            (combined is null || !HasCompleteTextMetadata(combined)))
        {
            combined = MergeMetadata(
                combined,
                await GetGogMetadataByProductIdAsync(productId, game, token));
        }

        return combined;
    }

    private async Task<GameMetadata?> GetGogMetadataByProductIdAsync(
        string productId,
        Game game,
        CancellationToken token)
    {
        GameMetadata? combined = null;

        // Os endpoints GOG não têm exatamente o mesmo conjunto de campos. O v2
        // costuma fornecer a descrição completa, enquanto o endpoint de produto pode
        // complementar empresa, gênero e lançamento. Portanto nunca paramos na
        // primeira resposta parcial: mesclamos todas as respostas válidas.
        foreach (var url in new[]
        {
            $"https://api.gog.com/v2/games/{Uri.EscapeDataString(productId)}?locale=en-US",
            $"https://api.gog.com/products/{Uri.EscapeDataString(productId)}?locale=en_US&expand=description"
        })
        {
            try
            {
                using var response = await _http.GetAsync(url, token);
                if (!response.IsSuccessStatusCode)
                    continue;

                using var doc = JsonDocument.Parse(
                    await SafeHttpResponseService.ReadTextAsync(response, cancellationToken: token));
                var metadata = BuildGogMetadata(doc.RootElement, game);
                if (metadata is not null)
                    combined = MergeMetadata(combined, metadata);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Uma resposta parcial/indisponível não impede o próximo endpoint.
            }
        }

        if (combined is not null)
            combined.ExternalId = productId;

        return combined;
    }

    private async Task<(string? ProductId, GameMetadata? Metadata)> FindGogCatalogMetadataByExactNameAsync(
        Game game,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(game.Name))
            return (null, null);

        try
        {
            var query = Uri.EscapeDataString($"like:{game.Name.Trim()}");
            var url = $"https://catalog.gog.com/v1/catalog?query={query}&limit=24&productType=in%3Agame&page=1&countryCode=US&locale=en-US&currencyCode=USD";
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
                return (null, null);

            using var doc = JsonDocument.Parse(
                await SafeHttpResponseService.ReadTextAsync(response, cancellationToken: token));
            if (!doc.RootElement.TryGetProperty("products", out var products) ||
                products.ValueKind != JsonValueKind.Array)
            {
                return (null, null);
            }

            foreach (var product in products.EnumerateArray())
            {
                var title = ReadJsonString(product, "title") ?? ReadJsonString(product, "name");
                if (!TitlesMatch(game.Name, title))
                    continue;

                string? productId = null;
                if (product.TryGetProperty("id", out var id))
                {
                    productId = id.ValueKind == JsonValueKind.String
                        ? id.GetString()
                        : id.ToString();
                }

                var metadata = BuildGogMetadata(product, game);
                if (metadata is not null)
                    metadata.ExternalId = productId;

                return (productId, metadata);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Pesquisa GOG é opcional.
        }

        return (null, null);
    }

    private static GameMetadata? BuildGogMetadata(JsonElement root, Game game)
    {
        var metadata = new GameMetadata
        {
            CanonicalName = ReadJsonString(root, "title")
                            ?? FindStringPropertyRecursive(root, "title", "name")
                            ?? game.Name,
            Description = ReadNestedDescription(root)
                          ?? SanitizeDescription(FindStringPropertyRecursive(
                              root,
                              "description",
                              "lead",
                              "summary")),
            Developer = ReadFirstName(root, "developers")
                        ?? ReadFirstName(root, "developer")
                        ?? FindCompanyNameRecursive(root, "developers", "developer"),
            Publisher = ReadFirstName(root, "publishers")
                        ?? ReadFirstName(root, "publisher")
                        ?? FindCompanyNameRecursive(root, "publishers", "publisher"),
            Source = "GOG",
            ExternalId = game.Id
        };

        metadata.ReleaseYear = ReadReleaseYear(root) ?? FindReleaseYearRecursive(root);
        ReadGenres(root, metadata.Genres);
        ReadGenresFromProperties(root, metadata.Genres);
        ReadGenresRecursive(root, metadata.Genres);

        var hasUsefulData = !string.IsNullOrWhiteSpace(metadata.Description) ||
                            !string.IsNullOrWhiteSpace(metadata.Developer) ||
                            !string.IsNullOrWhiteSpace(metadata.Publisher) ||
                            metadata.ReleaseYear.HasValue ||
                            metadata.Genres.Count > 0;

        return hasUsefulData ? metadata : null;
    }

    private static string? ReadJsonString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? ReadNestedDescription(JsonElement root)
    {
        if (!root.TryGetProperty("description", out var description))
            return null;

        if (description.ValueKind == JsonValueKind.String)
            return SanitizeDescription(description.GetString());

        if (description.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "full", "lead", "text", "description" })
            {
                if (description.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var text = SanitizeDescription(value.GetString());
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
        }

        return null;
    }

    private static string? ReadFirstName(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var values))
            return null;

        if (values.ValueKind == JsonValueKind.String)
            return values.GetString();

        if (values.ValueKind == JsonValueKind.Object)
        {
            if (values.TryGetProperty("name", out var directName) && directName.ValueKind == JsonValueKind.String)
                return directName.GetString();
            if (values.TryGetProperty("title", out var directTitle) && directTitle.ValueKind == JsonValueKind.String)
                return directTitle.GetString();
        }

        if (values.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                return item.GetString();
            if (item.ValueKind == JsonValueKind.Object)
            {
                if (item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    return name.GetString();
                if (item.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                    return title.GetString();
            }
        }

        return null;
    }

    private static int? ReadReleaseYear(JsonElement root)
    {
        foreach (var key in new[] { "release_date", "releaseDate", "globalReleaseDate" })
        {
            if (!root.TryGetProperty(key, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var epoch))
            {
                try
                {
                    return epoch > 10_000_000_000
                        ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).Year
                        : DateTimeOffset.FromUnixTimeSeconds(epoch).Year;
                }
                catch { }
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date) ||
                    DateTime.TryParse(text, out date))
                {
                    return date.Year;
                }
            }
        }

        return null;
    }

    private static void ReadGenres(JsonElement root, List<string> target)
    {
        if (!root.TryGetProperty("genres", out var genres) || genres.ValueKind != JsonValueKind.Array)
            return;

        foreach (var genre in genres.EnumerateArray())
        {
            string? name = null;
            if (genre.ValueKind == JsonValueKind.String)
                name = genre.GetString();
            else if (genre.ValueKind == JsonValueKind.Object)
            {
                if (genre.TryGetProperty("name", out var value))
                    name = value.GetString();
                else if (genre.TryGetProperty("title", out var title))
                    name = title.GetString();
            }

            AddGenre(name, target);
        }
    }

    private static void ReadGenresFromProperties(JsonElement root, List<string> target)
    {
        if (!root.TryGetProperty("properties", out var properties))
            return;

        IEnumerable<JsonElement> values = properties.ValueKind switch
        {
            JsonValueKind.Array => properties.EnumerateArray(),
            _ => Array.Empty<JsonElement>()
        };

        foreach (var property in values)
        {
            string? name = null;
            if (property.ValueKind == JsonValueKind.String)
                name = property.GetString();
            else if (property.ValueKind == JsonValueKind.Object)
            {
                if (property.TryGetProperty("name", out var value) && value.ValueKind == JsonValueKind.String)
                    name = value.GetString();
                else if (property.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                    name = title.GetString();
            }

            AddGenre(name, target);
        }
    }

    private static string? FindStringPropertyRecursive(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }

                var nested = FindStringPropertyRecursive(property.Value, propertyNames);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindStringPropertyRecursive(item, propertyNames);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return null;
    }

    private static string? FindCompanyNameRecursive(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    var value = ReadNameFromElement(property.Value);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }

                var nested = FindCompanyNameRecursive(property.Value, propertyNames);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindCompanyNameRecursive(item, propertyNames);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return null;
    }

    private static string? ReadNameFromElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "name", "title", "label" })
            {
                if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var name = ReadNameFromElement(item);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }

        return null;
    }

    private static int? FindReleaseYearRecursive(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("release_date", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("releaseDate", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("globalReleaseDate", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("originalReleaseDate", StringComparison.OrdinalIgnoreCase))
                {
                    var year = ParseReleaseYearValue(property.Value);
                    if (year.HasValue)
                        return year;
                }

                var nested = FindReleaseYearRecursive(property.Value);
                if (nested.HasValue)
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindReleaseYearRecursive(item);
                if (nested.HasValue)
                    return nested;
            }
        }

        return null;
    }

    private static int? ParseReleaseYearValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var epoch))
        {
            try
            {
                return epoch > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).Year
                    : DateTimeOffset.FromUnixTimeSeconds(epoch).Year;
            }
            catch
            {
            }
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var date) ||
                DateTimeOffset.TryParse(text, out date))
            {
                return date.Year;
            }
        }

        return null;
    }

    private static void ReadGenresRecursive(JsonElement element, List<string> target)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("genres", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("genre", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("genreNames", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("categories", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("category", StringComparison.OrdinalIgnoreCase))
                {
                    AddGenreValues(property.Value, target);
                }

                ReadGenresRecursive(property.Value, target);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                ReadGenresRecursive(item, target);
        }
    }

    private static void AddGenreValues(JsonElement element, List<string> target)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            AddGenre(element.GetString(), target);
            return;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var name = ReadNameFromElement(element)
                       ?? FindStringPropertyRecursive(element, "label", "value");
            AddGenre(name, target);

            // Alguns retornos do catálogo GOG agrupam gêneros em objetos internos.
            // Se o objeto principal não tiver nome próprio, percorremos seus valores.
            if (string.IsNullOrWhiteSpace(name))
            {
                foreach (var property in element.EnumerateObject())
                    AddGenreValues(property.Value, target);
            }
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                AddGenreValues(item, target);
        }
    }

    private static void AddGenre(string? value, List<string> target)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var genre in GenreService.NormalizeMany(new[] { value }))
        {
            if (!target.Contains(genre, StringComparer.OrdinalIgnoreCase))
                target.Add(genre);
        }
    }

    private static GameMetadata BuildArtworkOnlyCache(Game game, GameMetadata source) => new()
    {
        CanonicalName = game.Name,
        CoverUrl = source.CoverUrl,
        CoverLocalPath = source.CoverLocalPath,
        HorizontalCoverUrl = source.HorizontalCoverUrl,
        HorizontalCoverLocalPath = source.HorizontalCoverLocalPath,
        VerticalCoverUrl = source.VerticalCoverUrl,
        VerticalCoverLocalPath = source.VerticalCoverLocalPath,
        CustomHorizontalCoverLocalPath = source.CustomHorizontalCoverLocalPath,
        CustomVerticalCoverLocalPath = source.CustomVerticalCoverLocalPath,
        Source = "Local",
        UpdatedAtUtc = DateTime.MinValue
    };

    private static string? ReadSteamAgeRatingValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString();

        if (element.ValueKind == JsonValueKind.Number)
            return element.ToString();

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var propertyName in new[] { "rating", "display_online_notice", "age" })
        {
            if (!element.TryGetProperty(propertyName, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
            {
                var textValue = value.GetString();
                if (!string.IsNullOrWhiteSpace(textValue) && textValue.Length <= 32)
                    return textValue.Trim();
            }

            if (value.ValueKind == JsonValueKind.Number)
                return value.ToString();
        }

        return null;
    }

    private static GameMetadata? MergeMetadata(GameMetadata? primary, GameMetadata? secondary)
    {
        if (primary is null) return secondary;
        if (secondary is null) return primary;

        primary.CanonicalName = string.IsNullOrWhiteSpace(primary.CanonicalName) ? secondary.CanonicalName : primary.CanonicalName;
        if (IsLowQualityDescription(primary.Description) && !IsLowQualityDescription(secondary.Description))
            primary.Description = secondary.Description;
        primary.Developer = string.IsNullOrWhiteSpace(primary.Developer) ? secondary.Developer : primary.Developer;
        primary.Publisher = string.IsNullOrWhiteSpace(primary.Publisher) ? secondary.Publisher : primary.Publisher;
        primary.ReleaseYear ??= secondary.ReleaseYear;
        primary.CoverUrl ??= secondary.CoverUrl;
        primary.HorizontalCoverUrl ??= secondary.HorizontalCoverUrl;
        primary.VerticalCoverUrl ??= secondary.VerticalCoverUrl;
        primary.ExternalId ??= secondary.ExternalId;

        foreach (var genre in secondary.Genres)
            if (!primary.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
                primary.Genres.Add(genre);
        foreach (var platform in secondary.Platforms)
            if (!primary.Platforms.Contains(platform, StringComparer.OrdinalIgnoreCase))
                primary.Platforms.Add(platform);

        primary.AgeRatings ??= new(StringComparer.OrdinalIgnoreCase);
        if (secondary.AgeRatings is not null)
        {
            foreach (var pair in secondary.AgeRatings)
                if (!primary.AgeRatings.ContainsKey(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    primary.AgeRatings[pair.Key] = pair.Value;
        }

        MergeLocalizedDescriptions(primary, secondary);
        primary.Source = CombineMetadataSources(primary.Source, secondary.Source);

        primary.Description = SanitizeDescription(primary.Description);
        GenreService.NormalizeInPlace(primary);
        return primary;
    }


    private static GameMetadata? MergeSteamCacheFallback(
        GameMetadata? current,
        GameMetadata? cached,
        string installedAppId)
    {
        if (current is null)
            return cached;
        if (cached is null)
            return current;

        // Um cache Steam só é reutilizável quando pertence exatamente ao App ID
        // instalado. Isso impede que correspondências antigas por nome contaminem o jogo.
        if (!string.Equals(cached.ExternalId, installedAppId, StringComparison.OrdinalIgnoreCase))
            return current;

        var cachedSources = NormalizeMetadataSources(cached.Source);
        if (string.IsNullOrWhiteSpace(cachedSources) ||
            !cachedSources.Split(" + ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(source => source.StartsWith("Steam Store", StringComparison.OrdinalIgnoreCase)))
            return current;

        var usedCacheText = false;

        if (IsLowQualityDescription(current.Description) && !IsLowQualityDescription(cached.Description))
        {
            current.Description = cached.Description;
            usedCacheText = true;
        }

        if (string.IsNullOrWhiteSpace(current.Developer) && !string.IsNullOrWhiteSpace(cached.Developer))
        {
            current.Developer = cached.Developer;
            usedCacheText = true;
        }

        if (string.IsNullOrWhiteSpace(current.Publisher) && !string.IsNullOrWhiteSpace(cached.Publisher))
        {
            current.Publisher = cached.Publisher;
            usedCacheText = true;
        }

        if (!current.ReleaseYear.HasValue && cached.ReleaseYear.HasValue)
        {
            current.ReleaseYear = cached.ReleaseYear;
            usedCacheText = true;
        }

        // Se a Steam retornou gêneros nesta execução, eles são autoritativos. O cache
        // só entra quando a resposta atual veio sem nenhum gênero.
        if (current.Genres.Count == 0 && cached.Genres.Count > 0)
        {
            foreach (var genre in cached.Genres)
                if (!current.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
                    current.Genres.Add(genre);
            usedCacheText = true;
        }

        current.AgeRatings ??= new(StringComparer.OrdinalIgnoreCase);
        if (cached.AgeRatings is not null)
        {
            foreach (var pair in cached.AgeRatings)
                if (!current.AgeRatings.ContainsKey(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    current.AgeRatings[pair.Key] = pair.Value;
        }

        MergeLocalizedDescriptions(current, cached);

        // Artes já obtidas continuam válidas independentemente da resposta textual.
        current.CoverUrl ??= cached.CoverUrl;
        current.HorizontalCoverUrl ??= cached.HorizontalCoverUrl;
        current.VerticalCoverUrl ??= cached.VerticalCoverUrl;
        current.CoverLocalPath ??= cached.CoverLocalPath;
        current.HorizontalCoverLocalPath ??= cached.HorizontalCoverLocalPath;
        current.VerticalCoverLocalPath ??= cached.VerticalCoverLocalPath;

        // Se algum campo textual precisou ser recuperado, preserva também a proveniência
        // daquele cache. Caso contrário, mantém somente as fontes da resposta atual.
        if (usedCacheText)
            current.Source = CombineMetadataSources(current.Source, cachedSources);

        current.Description = SanitizeDescription(current.Description);
        GenreService.NormalizeInPlace(current);
        return current;
    }

    private static GameMetadata? MergeSupplementalMetadata(
        GameMetadata? primary,
        GameMetadata? secondary,
        bool allowDescription)
    {
        if (primary is null)
        {
            if (secondary is null) return null;
            if (!allowDescription) secondary.Description = null;
            return secondary;
        }
        if (secondary is null) return primary;

        // Fontes suplementares nunca alteram identidade nem artes do provedor principal.
        if (allowDescription && IsLowQualityDescription(primary.Description) &&
            !IsLowQualityDescription(secondary.Description))
            primary.Description = secondary.Description;

        primary.Developer = string.IsNullOrWhiteSpace(primary.Developer) ? secondary.Developer : primary.Developer;
        primary.Publisher = string.IsNullOrWhiteSpace(primary.Publisher) ? secondary.Publisher : primary.Publisher;
        primary.ReleaseYear ??= secondary.ReleaseYear;

        foreach (var genre in secondary.Genres)
            if (!primary.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
                primary.Genres.Add(genre);

        primary.AgeRatings ??= new(StringComparer.OrdinalIgnoreCase);
        if (secondary.AgeRatings is not null)
        {
            foreach (var pair in secondary.AgeRatings)
                if (!primary.AgeRatings.ContainsKey(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    primary.AgeRatings[pair.Key] = pair.Value;
        }

        MergeLocalizedDescriptions(primary, secondary);
        primary.Source = CombineMetadataSources(primary.Source, secondary.Source);

        GenreService.NormalizeInPlace(primary);
        return primary;
    }

    private static void MergeLocalizedDescriptions(GameMetadata target, GameMetadata source)
    {
        target.LocalizedDescriptions ??= new(StringComparer.OrdinalIgnoreCase);
        if (source.LocalizedDescriptions is null)
            return;

        foreach (var pair in source.LocalizedDescriptions)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) &&
                !IsLowQualityDescription(pair.Value) &&
                !target.LocalizedDescriptions.ContainsKey(pair.Key))
            {
                target.LocalizedDescriptions[pair.Key] = pair.Value;
            }
        }
    }

    private static string? CombineMetadataSources(string? left, string? right)
    {
        var sources = new List<string>();
        AddSources(sources, left);
        AddSources(sources, right);
        return sources.Count == 0 ? null : string.Join(" + ", sources);
    }

    private static string? NormalizeMetadataSources(string? value)
        => CombineMetadataSources(null, value);

    private static void AddSources(List<string> target, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var part in value.Split(" + ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!target.Contains(part, StringComparer.OrdinalIgnoreCase))
                target.Add(part);
        }
    }

    private static bool HasCompleteTextMetadata(GameMetadata? metadata) =>
        metadata is not null &&
        !IsLowQualityDescription(metadata.Description) &&
        !string.IsNullOrWhiteSpace(metadata.Developer) &&
        !string.IsNullOrWhiteSpace(metadata.Publisher) &&
        metadata.ReleaseYear.HasValue &&
        metadata.Genres.Count > 0;

    private static IEnumerable<string> BuildMetadataSearchTerms(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            yield break;

        var original = title.Trim();
        yield return original;

        // APIs de busca tratam pontuação de maneira diferente. Uma variante apenas
        // com palavras permite localizar títulos oficiais com ":"/"™"/hífens diferentes
        // sem afrouxar a validação final, que continua usando TitlesMatch.
        var punctuationNeutral = Regex.Replace(original, @"[^\p{L}\p{N}]+", " ").Trim();
        punctuationNeutral = Regex.Replace(punctuationNeutral, @"\s{2,}", " ");

        if (!string.Equals(original, punctuationNeutral, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(punctuationNeutral))
        {
            yield return punctuationNeutral;
        }
    }

    private static bool TitlesMatch(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        NormalizeTitle(left) == NormalizeTitle(right);

    private static bool IsLowQualityDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var text = SanitizeDescription(value);
        if (string.IsNullOrWhiteSpace(text))
            return true;

        // Descrições de busca do Wikidata como "2024 video game" não descrevem
        // o jogo e não devem impedir uma fonte posterior de fornecer texto real.
        if (Regex.IsMatch(text, @"^\d{4}\s+(?:video\s+)?game$", RegexOptions.IgnoreCase))
            return true;
        if (Regex.IsMatch(text, @"^(?:video\s+)?game$", RegexOptions.IgnoreCase))
            return true;

        return false;
    }

    private static string? SanitizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value;
        text = Regex.Replace(text, @"<script[\s\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<style[\s\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<img[^>]*>", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</p\s*>", "\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</li\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?(?:ul|ol|p|div|section|article|strong|b|em|i)[^>]*>", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @" *\n *", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();

        const int maxDescriptionLength = 1_200;
        if (text.Length > maxDescriptionLength)
            text = text[..maxDescriptionLength].TrimEnd() + "…";

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void PreserveExistingArtwork(GameMetadata target, GameMetadata? cached, GameMetadata current)
    {
        var source = cached ?? current;
        target.HorizontalCoverLocalPath ??= source.HorizontalCoverLocalPath;
        target.VerticalCoverLocalPath ??= source.VerticalCoverLocalPath;
        target.CoverLocalPath ??= source.CoverLocalPath;
        target.HorizontalCoverUrl ??= source.HorizontalCoverUrl;
        target.VerticalCoverUrl ??= source.VerticalCoverUrl;
        target.CoverUrl ??= source.CoverUrl;
    }

    /// <summary>
    /// Fallback sem chave de API para metadados textuais. O Wikidata fornece
    /// desenvolvedora, publicadora, gênero e lançamento; a Wikipedia é usada apenas
    /// para a descrição quando existe uma página em inglês associada à mesma entidade.
    /// </summary>
    private async Task<GameMetadata?> GetWikidataMetadataByExactNameAsync(Game game, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(game.Name))
            return null;

        try
        {
            string? entityId = null;
            string? canonicalName = null;

            foreach (var searchTerm in BuildMetadataSearchTerms(game.Name))
            {
                var searchUrl = "https://www.wikidata.org/w/api.php?action=wbsearchentities" +
                                $"&search={Uri.EscapeDataString(searchTerm)}" +
                                "&language=en&uselang=en&type=item&limit=10&format=json";
                using var searchResponse = await _http.GetAsync(searchUrl, token);
                if (!searchResponse.IsSuccessStatusCode)
                    continue;

                using var searchDoc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(
                    searchResponse, maxBytes: 1024 * 1024, cancellationToken: token));
                if (!searchDoc.RootElement.TryGetProperty("search", out var searchResults) ||
                    searchResults.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var result in searchResults.EnumerateArray())
                {
                    var label = result.TryGetProperty("label", out var labelElement) ? labelElement.GetString() : null;
                    if (!TitlesMatch(game.Name, label))
                        continue;

                    var description = result.TryGetProperty("description", out var descriptionElement)
                        ? descriptionElement.GetString()
                        : null;

                    // Reduz falsos positivos com filmes, álbuns e outros itens homônimos.
                    if (!string.IsNullOrWhiteSpace(description) &&
                        !description.Contains("game", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    entityId = result.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    canonicalName = label;
                    if (!string.IsNullOrWhiteSpace(entityId))
                        break;
                }

                if (!string.IsNullOrWhiteSpace(entityId))
                    break;
            }

            if (string.IsNullOrWhiteSpace(entityId))
                return null;

            var entityUrl = $"https://www.wikidata.org/wiki/Special:EntityData/{Uri.EscapeDataString(entityId)}.json";
            using var entityResponse = await _http.GetAsync(entityUrl, token);
            if (!entityResponse.IsSuccessStatusCode)
                return null;

            using var entityDoc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(
                entityResponse, maxBytes: 4 * 1024 * 1024, cancellationToken: token));
            if (!entityDoc.RootElement.TryGetProperty("entities", out var entities) ||
                !entities.TryGetProperty(entityId, out var entity))
            {
                return null;
            }

            var metadata = new GameMetadata
            {
                CanonicalName = canonicalName ?? game.Name,
                // O texto curto do resultado de busca costuma ser apenas algo como
                // "2024 video game". Ele serve para validar o tipo da entidade, não
                // como descrição do jogo. A descrição só vem do artigo da Wikipedia.
                Description = null,
                Source = "Wikidata/Wikipedia",
                ExternalId = entityId
            };

            if (entity.TryGetProperty("claims", out var claims) && claims.ValueKind == JsonValueKind.Object)
            {
                metadata.ReleaseYear = ReadWikidataReleaseYear(claims);

                var developerIds = ReadWikidataEntityIds(claims, "P178");
                var publisherIds = ReadWikidataEntityIds(claims, "P123");
                var genreIds = ReadWikidataEntityIds(claims, "P136");

                // Classificações oficiais estruturadas no Wikidata. Essas propriedades
                // representam diretamente os sistemas de cada região; não fazemos
                // conversão entre ESRB/PEGI/ClassInd ou qualquer outro órgão.
                var ratingIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["esrb"] = ReadWikidataEntityIds(claims, "P852"),
                    ["pegi"] = ReadWikidataEntityIds(claims, "P908"),
                    ["usk"] = ReadWikidataEntityIds(claims, "P914"),
                    ["cero"] = ReadWikidataEntityIds(claims, "P853"),
                    ["acb"] = ReadWikidataEntityIds(claims, "P3156"),
                    ["bbfc"] = ReadWikidataEntityIds(claims, "P2629")
                };

                var allIds = developerIds
                    .Concat(publisherIds)
                    .Concat(genreIds)
                    .Concat(ratingIds.Values.SelectMany(ids => ids))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var labels = await GetWikidataLabelsAsync(allIds, token);

                metadata.Developer = JoinWikidataLabels(developerIds, labels);
                metadata.Publisher = JoinWikidataLabels(publisherIds, labels);
                foreach (var id in genreIds)
                {
                    if (labels.TryGetValue(id, out var genre))
                        metadata.Genres.Add(genre);
                }

                foreach (var pair in ratingIds)
                {
                    foreach (var id in pair.Value)
                    {
                        if (!labels.TryGetValue(id, out var label) || string.IsNullOrWhiteSpace(label))
                            continue;

                        metadata.AgeRatings[pair.Key] = NormalizeWikidataAgeRatingLabel(pair.Key, label);
                        break;
                    }
                }
            }

            // Se houver uma página em inglês ligada à entidade, usa o resumo como descrição.
            if (entity.TryGetProperty("sitelinks", out var sitelinks) &&
                sitelinks.ValueKind == JsonValueKind.Object &&
                sitelinks.TryGetProperty("enwiki", out var enwiki) &&
                enwiki.TryGetProperty("title", out var titleElement))
            {
                var wikiTitle = titleElement.GetString();
                var summary = await GetWikipediaSummaryAsync(wikiTitle, token);
                if (!IsLowQualityDescription(summary))
                    metadata.Description = summary;
            }

            // Alguns itens do Wikidata não expõem um sitelink em inglês mesmo quando
            // existe uma página válida. Faz uma busca exata na Wikipedia como fallback.
            if (IsLowQualityDescription(metadata.Description))
            {
                var summary = await GetWikipediaSummaryByExactGameNameAsync(game.Name, token);
                if (!IsLowQualityDescription(summary))
                    metadata.Description = summary;
            }

            // O parágrafo inicial costuma separar melhor o estúdio principal e a
            // publicadora do que listas amplas de claims do Wikidata. Nunca inventa
            // empresas: só aplica quando a frase contém explicitamente developed/published.
            ApplyCompaniesFromDescription(metadata);

            GenreService.NormalizeInPlace(metadata);
            return HasAnyUsefulTextMetadata(metadata) ? metadata : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<Dictionary<string, string>> GetWikidataLabelsAsync(
        IReadOnlyCollection<string> ids,
        CancellationToken token)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
            return labels;

        foreach (var chunk in ids.Chunk(40))
        {
            var url = "https://www.wikidata.org/w/api.php?action=wbgetentities" +
                      $"&ids={Uri.EscapeDataString(string.Join('|', chunk))}" +
                      "&props=labels&languages=en&format=json";
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
                continue;

            using var doc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(
                response, maxBytes: 2 * 1024 * 1024, cancellationToken: token));
            if (!doc.RootElement.TryGetProperty("entities", out var entities))
                continue;

            foreach (var entity in entities.EnumerateObject())
            {
                if (entity.Value.TryGetProperty("labels", out var labelSet) &&
                    labelSet.TryGetProperty("en", out var english) &&
                    english.TryGetProperty("value", out var value))
                {
                    var label = value.GetString();
                    if (!string.IsNullOrWhiteSpace(label))
                        labels[entity.Name] = label;
                }
            }
        }

        return labels;
    }

    private async Task<string?> GetWikipediaSummaryByExactGameNameAsync(string? gameName, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(gameName))
            return null;

        // Tenta primeiro os títulos mais prováveis, sem depender da pesquisa geral.
        foreach (var title in new[] { gameName.Trim(), $"{gameName.Trim()} (video game)" })
        {
            var summary = await GetWikipediaSummaryAsync(title, token);
            if (!IsLowQualityDescription(summary) && SummaryMatchesGame(summary, gameName))
                return summary;
        }

        try
        {
            foreach (var searchTerm in BuildMetadataSearchTerms(gameName))
            {
                var query = Uri.EscapeDataString($"\"{searchTerm}\" video game");
                var url = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={query}&srlimit=8&format=json&utf8=1";
                using var response = await _http.GetAsync(url, token);
                if (!response.IsSuccessStatusCode)
                    continue;

                using var doc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(
                    response, maxBytes: 1024 * 1024, cancellationToken: token));
                if (!doc.RootElement.TryGetProperty("query", out var q) ||
                    !q.TryGetProperty("search", out var results) ||
                    results.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in results.EnumerateArray())
                {
                    var title = item.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    var titleWithoutQualifier = Regex.Replace(title, @"\s*\([^)]*video game[^)]*\)\s*$", "", RegexOptions.IgnoreCase).Trim();
                    if (!TitlesMatch(gameName, titleWithoutQualifier))
                        continue;

                    var summary = await GetWikipediaSummaryAsync(title, token);
                    if (!IsLowQualityDescription(summary) && SummaryMatchesGame(summary, gameName))
                        return summary;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        return null;
    }

    private static bool SummaryMatchesGame(string? summary, string gameName)
    {
        if (IsLowQualityDescription(summary))
            return false;

        var normalizedSummary = NormalizeTitle(summary!);
        var normalizedName = NormalizeTitle(gameName);
        return normalizedSummary.Contains(normalizedName, StringComparison.Ordinal) &&
               summary!.Contains("game", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyCompaniesFromDescription(GameMetadata metadata)
    {
        if (IsLowQualityDescription(metadata.Description))
            return;

        var text = metadata.Description!;
        var developed = Regex.Match(
            text,
            @"\bdeveloped\s+by\s+(?<value>.+?)(?=\s+(?:and\s+)?(?:co-)?published\s+by\b|[.;])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var published = Regex.Match(
            text,
            @"\b(?:co-)?published\s+by\s+(?<value>.+?)(?=,\s*(?:who|which|that|with|for)\b|\s+and\s+(?:co-)?developed\s+by\b|[.;])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        string? developer = developed.Success ? CleanCompanyPhrase(developed.Groups["value"].Value) : null;
        string? publisher = published.Success ? CleanCompanyPhrase(published.Groups["value"].Value) : null;

        if (!string.IsNullOrWhiteSpace(developer))
            metadata.Developer = developer;

        if (!string.IsNullOrWhiteSpace(publisher))
        {
            if (!string.IsNullOrWhiteSpace(developer))
            {
                publisher = Regex.Replace(publisher, @"\bthem\b", developer, RegexOptions.IgnoreCase).Trim();
                publisher = Regex.Replace(publisher, @"\band\b", " / ", RegexOptions.IgnoreCase);
                publisher = Regex.Replace(publisher, @"\s*/\s*", " / ");
            }
            metadata.Publisher = publisher;
        }
    }

    private static string? CleanCompanyPhrase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = Regex.Replace(value.Trim(), @"\s+", " ");
        cleaned = Regex.Replace(cleaned, @"^(?:the\s+)?(?:video\s+game\s+)?", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Split(cleaned, @",\s*(?:who|which|that|with|for)\b", RegexOptions.IgnoreCase)[0].Trim();
        cleaned = Regex.Split(cleaned, @"\s+and\s+(?:co-)?developed\s+by\b", RegexOptions.IgnoreCase)[0].Trim();
        cleaned = Regex.Split(cleaned, @"\s+for\s+(?:PlayStation|Xbox|Windows|Nintendo|PC|Mac|Linux)\b", RegexOptions.IgnoreCase)[0].Trim();
        cleaned = Regex.Split(cleaned, @"\s+(?:featuring|including|released|which|who|that)\b", RegexOptions.IgnoreCase)[0].Trim();
        return cleaned.Length is > 0 and <= 120 ? cleaned : null;
    }

    private async Task<string?> GetWikipediaSummaryAsync(string? title, CancellationToken token, string wikiLanguage = "en")
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        // O endpoint REST de resumo ocasionalmente não retorna páginas que a API
        // MediaWiki normal resolve (redirecionamentos/títulos com pontuação). Tenta
        // REST primeiro e usa action=query + extracts como fallback.
        try
        {
            var url = $"https://{wikiLanguage}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(title.Replace(' ', '_'))}";
            using var response = await _http.GetAsync(url, token);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(
                    response, maxBytes: 1024 * 1024, cancellationToken: token));
                var summary = doc.RootElement.TryGetProperty("extract", out var extract)
                    ? SanitizeDescription(extract.GetString())
                    : null;
                if (!IsLowQualityDescription(summary))
                    return summary;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        try
        {
            var queryUrl = $"https://{wikiLanguage}.wikipedia.org/w/api.php?action=query&prop=extracts" +
                           "&exintro=1&explaintext=1&redirects=1&format=json" +
                           $"&titles={Uri.EscapeDataString(title)}";
            using var response = await _http.GetAsync(queryUrl, token);
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(
                response, maxBytes: 2 * 1024 * 1024, cancellationToken: token));
            if (!doc.RootElement.TryGetProperty("query", out var query) ||
                !query.TryGetProperty("pages", out var pages) ||
                pages.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var page in pages.EnumerateObject())
            {
                if (!page.Value.TryGetProperty("extract", out var extract))
                    continue;

                var summary = SanitizeDescription(extract.GetString());
                if (!IsLowQualityDescription(summary))
                    return summary;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        return null;
    }

    private static string NormalizeWikidataAgeRatingLabel(string system, string label)
    {
        var value = WebUtility.HtmlDecode(label).Trim();
        return AgeRatingService.NormalizeSystem(system) switch
        {
            "pegi" => Regex.Replace(value, @"^PEGI\s*", string.Empty, RegexOptions.IgnoreCase).Trim(),
            "dejus" => Regex.Replace(
                value,
                @"^(?:ClassInd|Classifica(?:ção|cao)\s+Indicativa)\s*",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim(),
            "usk" => Regex.Replace(value, @"^USK\s*", string.Empty, RegexOptions.IgnoreCase).Trim(),
            "cero" => Regex.Replace(value, @"^CERO\s*", string.Empty, RegexOptions.IgnoreCase).Trim(),
            "bbfc" => Regex.Replace(value, @"^BBFC\s*", string.Empty, RegexOptions.IgnoreCase).Trim(),
            _ => value
        };
    }

    private static List<string> ReadWikidataEntityIds(JsonElement claims, string propertyId)
    {
        var result = new List<string>();
        if (!claims.TryGetProperty(propertyId, out var claimArray) || claimArray.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var claim in claimArray.EnumerateArray())
        {
            if (!claim.TryGetProperty("mainsnak", out var mainSnak) ||
                !mainSnak.TryGetProperty("datavalue", out var dataValue) ||
                !dataValue.TryGetProperty("value", out var value) ||
                value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty("id", out var idElement))
            {
                continue;
            }

            var id = idElement.GetString();
            if (!string.IsNullOrWhiteSpace(id) && !result.Contains(id, StringComparer.OrdinalIgnoreCase))
                result.Add(id);
        }

        return result;
    }

    private static int? ReadWikidataReleaseYear(JsonElement claims)
    {
        if (!claims.TryGetProperty("P577", out var dates) || dates.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var claim in dates.EnumerateArray())
        {
            if (!claim.TryGetProperty("mainsnak", out var mainSnak) ||
                !mainSnak.TryGetProperty("datavalue", out var dataValue) ||
                !dataValue.TryGetProperty("value", out var value) ||
                !value.TryGetProperty("time", out var timeElement))
            {
                continue;
            }

            var time = timeElement.GetString()?.TrimStart('+');
            if (!string.IsNullOrWhiteSpace(time) && time.Length >= 4 && int.TryParse(time[..4], out var year))
                return year;
        }

        return null;
    }

    private static string? JoinWikidataLabels(
        IEnumerable<string> ids,
        IReadOnlyDictionary<string, string> labels)
    {
        var names = ids.Where(labels.ContainsKey).Select(id => labels[id]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return names.Count == 0 ? null : string.Join(" / ", names);
    }

    private static bool HasAnyUsefulTextMetadata(GameMetadata metadata) =>
        !string.IsNullOrWhiteSpace(metadata.Description) ||
        !string.IsNullOrWhiteSpace(metadata.Developer) ||
        !string.IsNullOrWhiteSpace(metadata.Publisher) ||
        metadata.ReleaseYear.HasValue ||
        metadata.Genres.Count > 0;

    private async Task<GameMetadata?> GetIgdbMetadataAsync(Game game, LauncherSettings settings, CancellationToken token)
    {
        var clientId = settings.IgdbClientId ?? Environment.GetEnvironmentVariable("IGDB_CLIENT_ID");
        var clientSecret = settings.IgdbClientSecret ?? Environment.GetEnvironmentVariable("IGDB_CLIENT_SECRET");
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)) return null;
        try
        {
            await EnsureIgdbTokenAsync(clientId, clientSecret, token);
            if (string.IsNullOrWhiteSpace(_igdbToken)) return null;
            var query = $"search \"{EscapeApicalypse(game.Name)}\"; fields name,summary,cover.image_id,genres.name,platforms.name,involved_companies.company.name,first_release_date; limit 1;";
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
            req.Headers.Add("Client-ID", clientId);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _igdbToken);
            req.Content = new StringContent(query, Encoding.UTF8, "text/plain");
            using var res = await _http.SendAsync(req, token);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(res, cancellationToken: token));
            if (doc.RootElement.GetArrayLength() == 0) return null;
            var g = doc.RootElement[0];
            var m = new GameMetadata
            {
                CanonicalName = g.TryGetProperty("name", out var n) ? n.GetString() ?? game.Name : game.Name,
                Description = g.TryGetProperty("summary", out var s) ? SanitizeDescription(s.GetString()) : null,
                Source = "IGDB",
                ExternalId = g.TryGetProperty("id", out var id) ? id.GetInt32().ToString() : null
            };
            if (g.TryGetProperty("genres", out var genres)) foreach (var x in genres.EnumerateArray()) if (x.TryGetProperty("name", out var n2)) m.Genres.Add(n2.GetString() ?? "");
            if (g.TryGetProperty("platforms", out var platforms)) foreach (var x in platforms.EnumerateArray()) if (x.TryGetProperty("name", out var n2)) m.Platforms.Add(n2.GetString() ?? "");
            if (g.TryGetProperty("cover", out var cover) && cover.TryGetProperty("image_id", out var imageId))
            {
                m.CoverUrl = $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId.GetString()}.jpg";
                m.VerticalCoverUrl = m.CoverUrl;
            }
            if (g.TryGetProperty("first_release_date", out var epoch) && epoch.ValueKind == JsonValueKind.Number)
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(epoch.GetInt64()).UtcDateTime;
                m.ReleaseYear = dt.Year;
            }
            if (g.TryGetProperty("involved_companies", out var companies) && companies.GetArrayLength() > 0 && companies[0].TryGetProperty("company", out var company) && company.TryGetProperty("name", out var cn))
                m.Developer = cn.GetString();
            return m;
        }
        catch { return null; }
    }

    private async Task EnsureIgdbTokenAsync(string clientId, string clientSecret, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(_igdbToken) && DateTime.UtcNow < _igdbTokenExpires) return;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "client_credentials"
        });
        using var res = await _http.PostAsync("https://id.twitch.tv/oauth2/token", form, token);
        if (!res.IsSuccessStatusCode) return;
        using var doc = JsonDocument.Parse(await SafeHttpResponseService.ReadTextAsync(res, cancellationToken: token));
        _igdbToken = doc.RootElement.GetProperty("access_token").GetString();
        var seconds = doc.RootElement.GetProperty("expires_in").GetInt32();
        _igdbTokenExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 60));
    }

    private async Task EnsureSteamVerticalCoverAsync(GameMetadata metadata, Game game, CancellationToken token)
    {
        if (game.Platform != GamePlatform.Steam || !int.TryParse(game.Id, out _)) return;
        var url = BuildSteamLibraryCapsuleUrl(game);
        metadata.VerticalCoverUrl = url;

        // Não reutiliza o header_image: a vertical do Steam é a Library Capsule 600x900.
        // Importante: uma tentativa de revalidar/atualizar a Library Capsule nunca pode
        // apagar a arte anterior se a CDN falhar ou retornar um arquivo inválido.
        if (!IsExpectedImageSize(metadata.VerticalCoverLocalPath, 600, 900))
        {
            var previousPath = metadata.VerticalCoverLocalPath;
            var refreshed = await DownloadCoverAsync(
                url, game.ProviderId + "_vertical", token, force: true);

            if (!string.IsNullOrWhiteSpace(refreshed) &&
                File.Exists(refreshed) &&
                IsExpectedImageSize(refreshed, 600, 900))
            {
                metadata.VerticalCoverLocalPath = refreshed;
            }
            else
            {
                metadata.VerticalCoverLocalPath = previousPath;
            }
        }
    }

    private static string BuildSteamLibraryCapsuleUrl(Game game) =>
        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{game.Id}/library_600x900.jpg";

    private static bool IsExpectedImageSize(string? path, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault();
            return frame is not null && frame.PixelWidth == width && frame.PixelHeight == height;
        }
        catch { return false; }
    }

    private static void CompleteStructuredMetadataFromDescription(GameMetadata metadata)
    {
        if (IsLowQualityDescription(metadata.Description))
            return;

        var text = metadata.Description!;

        if (!metadata.ReleaseYear.HasValue)
        {
            // A primeira frase de descrições enciclopédicas normalmente começa com
            // "X is a 2011 fighting game...". Só usa anos plausíveis de lançamento.
            var firstSentence = Regex.Split(text, @"(?<=[.!?])\s+").FirstOrDefault() ?? text;
            var yearMatch = Regex.Match(firstSentence, @"\b(19[7-9]\d|20\d{2})\b");
            if (yearMatch.Success && int.TryParse(yearMatch.Value, out var year) && year <= DateTime.UtcNow.Year + 1)
                metadata.ReleaseYear = year;
        }

        if (metadata.Genres.Count == 0)
        {
            var firstSentence = Regex.Split(text, @"(?<=[.!?])\s+").FirstOrDefault() ?? text;
            var knownGenres = new (string Pattern, string Genre)[]
            {
                (@"\bbeat[ -]?['’]?em[ -]?up\b", "Beat 'em up"),
                (@"\bfighting(?:\s+video)?\s+game\b", "Fighting"),
                (@"\baction[- ]adventure\b", "Action-Adventure"),
                (@"\baction(?:\s+video)?\s+game\b", "Action"),
                (@"\badventure(?:\s+video)?\s+game\b", "Adventure"),
                (@"\brole[- ]playing(?:\s+video)?\s+game\b|\bRPG\b", "RPG"),
                (@"\bfirst[- ]person shooter\b|\bFPS\b", "FPS"),
                (@"\bthird[- ]person shooter\b", "Third-person shooter"),
                (@"\bplatform(?:er)?(?:\s+video)?\s+game\b", "Platformer"),
                (@"\bracing(?:\s+video)?\s+game\b", "Racing"),
                (@"\bsports(?:\s+video)?\s+game\b", "Sports"),
                (@"\bstrategy(?:\s+video)?\s+game\b", "Strategy"),
                (@"\bsimulation(?:\s+video)?\s+game\b", "Simulation"),
                (@"\bpuzzle(?:\s+video)?\s+game\b", "Puzzle"),
                (@"\bsurvival horror\b", "Survival horror"),
                (@"\bstealth(?:\s+video)?\s+game\b", "Stealth")
            };

            foreach (var (pattern, genre) in knownGenres)
                if (Regex.IsMatch(firstSentence, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    metadata.Genres.Add(genre);
        }

        GenreService.NormalizeInPlace(metadata);
    }

    private static void SanitizeCompanyFields(GameMetadata metadata)
    {
        metadata.Developer = SanitizeCompanyField(metadata.Developer);
        metadata.Publisher = SanitizeCompanyField(metadata.Publisher);
    }

    private static string? SanitizeCompanyField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var companies = value.Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanCompanyPhrase)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return companies.Count == 0 ? null : string.Join(" / ", companies!);
    }

    private async Task EnsureCoverAsync(
        GameMetadata metadata,
        Game game,
        CancellationToken token,
        bool forceRefresh = false)
    {
        // Atualizar metadados também pode forçar uma nova tentativa de baixar arte.
        // Porém, uma falha de rede/CDN nunca deve apagar a capa que já estava válida.
        if (!string.IsNullOrWhiteSpace(metadata.HorizontalCoverUrl))
        {
            var refreshed = await DownloadCoverAsync(
                metadata.HorizontalCoverUrl,
                game.ProviderId + "_horizontal",
                token,
                forceRefresh);

            if (!string.IsNullOrWhiteSpace(refreshed) && File.Exists(refreshed))
                metadata.HorizontalCoverLocalPath = refreshed;
        }

        if (!string.IsNullOrWhiteSpace(metadata.VerticalCoverUrl))
        {
            var refreshed = await DownloadCoverAsync(
                metadata.VerticalCoverUrl,
                game.ProviderId + "_vertical",
                token,
                forceRefresh);

            if (!string.IsNullOrWhiteSpace(refreshed) && File.Exists(refreshed))
                metadata.VerticalCoverLocalPath = refreshed;
        }

        // O header/cover genérico costuma ser horizontal. Nunca o reutiliza como capa
        // vertical da Steam; a Steam possui uma Library Capsule 600x900 específica.
        if (game.Platform != GamePlatform.Steam &&
            string.IsNullOrWhiteSpace(metadata.VerticalCoverUrl) &&
            !string.IsNullOrWhiteSpace(metadata.CoverUrl))
        {
            var refreshed = await DownloadCoverAsync(
                metadata.CoverUrl,
                game.ProviderId + "_vertical",
                token,
                forceRefresh);

            if (!string.IsNullOrWhiteSpace(refreshed) && File.Exists(refreshed))
                metadata.VerticalCoverLocalPath = refreshed;
        }

        if (string.IsNullOrWhiteSpace(metadata.HorizontalCoverUrl) &&
            !string.IsNullOrWhiteSpace(metadata.CoverUrl))
        {
            var refreshed = await DownloadCoverAsync(
                metadata.CoverUrl,
                game.ProviderId + "_horizontal",
                token,
                forceRefresh);

            if (!string.IsNullOrWhiteSpace(refreshed) && File.Exists(refreshed))
                metadata.HorizontalCoverLocalPath = refreshed;
        }

        // Mantém o caminho genérico somente apontando para uma arte realmente existente.
        if (!string.IsNullOrWhiteSpace(metadata.HorizontalCoverLocalPath) &&
            File.Exists(metadata.HorizontalCoverLocalPath))
        {
            metadata.CoverLocalPath = metadata.HorizontalCoverLocalPath;
        }
        else if (!string.IsNullOrWhiteSpace(metadata.VerticalCoverLocalPath) &&
                 File.Exists(metadata.VerticalCoverLocalPath))
        {
            metadata.CoverLocalPath = metadata.VerticalCoverLocalPath;
        }
    }

    /// <summary>
    /// Usa o SteamGridDB como fonte automática de artes quando a API Key estiver
    /// configurada. Em jogos Steam ele funciona apenas como fallback para a capa
    /// vertical oficial ausente; a arte horizontal oficial nunca é substituída
    /// automaticamente. Um ID escolhido manualmente em SteamGridDbGameIds sempre
    /// tem prioridade.
    /// </summary>
    private async Task EnsureSteamGridDbArtworkAsync(
        GameMetadata metadata,
        Game game,
        LauncherSettings settings,
        bool forceRefresh,
        CancellationToken token)
    {
        var apiKey = settings.SteamGridDbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        // Desde v120, "Restaurar capa padrão" volta para a cadeia automática de
        // arte. Flags antigas de bloqueio do SteamGridDB são ignoradas para que
        // upgrades de v117-v119 não deixem capas permanentemente vazias.
        var disableVertical = false;
        var disableHorizontal = false;

        var isSteam = game.Platform == GamePlatform.Steam;
        var hasVertical = !string.IsNullOrWhiteSpace(metadata.VerticalCoverLocalPath) &&
                          File.Exists(metadata.VerticalCoverLocalPath);

        // Para Steam, horizontal nunca recebe fallback automático do SteamGridDB.
        // O SteamGridDB entra somente quando a Library Capsule 600x900 oficial não
        // existe. Para outras plataformas mantém o comportamento anterior.
        var hasHorizontal = isSteam ||
                            (!string.IsNullOrWhiteSpace(metadata.HorizontalCoverLocalPath) &&
                             File.Exists(metadata.HorizontalCoverLocalPath));

        if ((!forceRefresh || isSteam) && hasVertical && hasHorizontal)
            return;

        try
        {
            int? gameId = null;
            if (settings.SteamGridDbGameIds.TryGetValue(game.ProviderId, out var mappedId) && mappedId > 0)
            {
                gameId = mappedId;
            }
            else if (isSteam)
            {
                // Para Steam, resolve pelo App ID real. Isso evita associar jogos
                // homônimos ou versões diferentes somente pelo nome.
                gameId = await _steamGridDb.ResolveGameIdAsync(game, apiKey, token);
            }
            else
            {
                var results = await _steamGridDb.SearchGamesAsync(game.Name, apiKey, token);
                var normalizedTarget = NormalizeTitle(game.Name);
                var exact = results
                    .Where(result => NormalizeTitle(result.Name) == normalizedTarget)
                    .OrderByDescending(result => result.Verified)
                    .FirstOrDefault();
                gameId = exact?.Id;
            }

            if (!gameId.HasValue)
                return;

            var verticalMissing = string.IsNullOrWhiteSpace(metadata.VerticalCoverLocalPath) ||
                                  !File.Exists(metadata.VerticalCoverLocalPath);
            if (!disableVertical && (verticalMissing || (!isSteam && forceRefresh)))
            {
                var vertical = (await _steamGridDb.GetArtworkOptionsAsync(gameId.Value, apiKey, vertical: true, cancellationToken: token)).FirstOrDefault();
                if (vertical is not null)
                {
                    metadata.VerticalCoverUrl = vertical.Url;
                    var refreshedVertical = await DownloadCoverAsync(
                        vertical.Url,
                        game.ProviderId + "_steamgriddb_vertical",
                        token,
                        forceRefresh);
                    if (!string.IsNullOrWhiteSpace(refreshedVertical) && File.Exists(refreshedVertical))
                        metadata.VerticalCoverLocalPath = refreshedVertical;
                }
            }

            if (!isSteam && !disableHorizontal && (forceRefresh || string.IsNullOrWhiteSpace(metadata.HorizontalCoverLocalPath) || !File.Exists(metadata.HorizontalCoverLocalPath)))
            {
                var horizontal = (await _steamGridDb.GetArtworkOptionsAsync(gameId.Value, apiKey, vertical: false, cancellationToken: token)).FirstOrDefault();
                if (horizontal is not null)
                {
                    metadata.HorizontalCoverUrl = horizontal.Url;
                    var refreshedHorizontal = await DownloadCoverAsync(
                        horizontal.Url,
                        game.ProviderId + "_steamgriddb_horizontal",
                        token,
                        forceRefresh);
                    if (!string.IsNullOrWhiteSpace(refreshedHorizontal) && File.Exists(refreshedHorizontal))
                        metadata.HorizontalCoverLocalPath = refreshedHorizontal;
                }
            }

            metadata.CoverLocalPath = metadata.HorizontalCoverLocalPath ?? metadata.VerticalCoverLocalPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Arte automática é opcional; uma falha não pode impedir o jogo de aparecer.
        }
    }

    private static void ApplyKnownMetadataCorrection(Game game, GameMetadata metadata)
    {
        var key = NormalizeTitle(game.Name);
        if (!KnownMetadataCorrections.TryGetValue(key, out var correction))
            return;

        if (!string.IsNullOrWhiteSpace(correction.Developer))
            metadata.Developer = correction.Developer;
        if (!string.IsNullOrWhiteSpace(correction.Publisher))
            metadata.Publisher = correction.Publisher;
        if (!string.IsNullOrWhiteSpace(correction.Description) &&
            IsLowQualityDescription(metadata.Description))
        {
            metadata.Description = correction.Description;
        }

        foreach (var genre in correction.AdditionalGenres)
        {
            if (!metadata.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
                metadata.Genres.Add(genre);
        }

        GenreService.NormalizeInPlace(metadata);
    }

    private sealed record KnownMetadataCorrection(
        string? Developer,
        string? Publisher,
        string? Description,
        IReadOnlyList<string> AdditionalGenres);

    private static string NormalizeTitle(string value)
        => new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private async Task<string?> DownloadCoverAsync(string url, string key, CancellationToken token, bool force = false)
    {
        try
        {
            var safe = string.Concat(key.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            var ext = url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
            var file = Path.Combine(_coverDir, safe + ext);
            if (force || !File.Exists(file))
            {
                var bytes = await SafeImageDownloadService.DownloadAsync(_http, url, token);
                await File.WriteAllBytesAsync(file, bytes, token);
            }
            return file;
        }
        catch { return null; }
    }

    private static GameMetadata BuildFallbackMetadata(Game game)
    {
        var name = game.Name.Trim();
        var genres = new List<string>();
        var n = name.ToLowerInvariant();
        if (n.Contains("racing") || n.Contains("forza") || n.Contains("need for speed")) genres.Add("Corrida");
        if (n.Contains("football") || n.Contains("fifa") || n.Contains("soccer")) genres.Add("Esportes");
        if (n.Contains("strategy") || n.Contains("civilization") || n.Contains("age of")) genres.Add("Estratégia");
        return new GameMetadata { CanonicalName = name, Genres = genres, Source = "Local" };
    }

    private void LoadCache()
    {
        try
        {
            if (File.Exists(_cacheFile)) _cache = JsonSerializer.Deserialize<Dictionary<string, GameMetadata>>(SafeFileReadService.ReadAllText(_cacheFile, SafeFileReadService.MetadataCacheMaxBytes, "cache de metadados")) ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { _cache = new(StringComparer.OrdinalIgnoreCase); }
    }

    private void SaveCache()
    {
        try { File.WriteAllText(_cacheFile, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }

    private static string EscapeApicalypse(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

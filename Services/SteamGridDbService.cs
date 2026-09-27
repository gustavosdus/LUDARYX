using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Integração com a API v2 do SteamGridDB para pesquisa, seleção e download de artes.
/// </summary>
public sealed class SteamGridDbService
{
    private const string BaseUrl = "https://www.steamgriddb.com/api/v2/";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;
    private readonly string _directory;

    public SteamGridDbService()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _directory = Path.Combine(AppDataService.RootDirectory, "custom-covers");
        Directory.CreateDirectory(_directory);
    }

    public async Task<List<SteamGridDbGameResult>> SearchGamesAsync(string query, string apiKey, CancellationToken cancellationToken = default)
    {
        EnsureApiKey(apiKey);
        var encodedName = Uri.EscapeDataString(query.Trim());
        using var request = CreateRequest($"search/autocomplete/{encodedName}", apiKey);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, "pesquisar jogos");

        var json = await SafeHttpResponseService.ReadTextAsync(response, cancellationToken: cancellationToken);
        var wrapper = JsonSerializer.Deserialize<ResponseWrapper<SteamGridDbGame>>(json, JsonOptions);
        return (wrapper?.Data ?? new List<SteamGridDbGame>())
            .Where(x => x.Id > 0 && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new SteamGridDbGameResult(x.Id, x.Name!, x.Types ?? new List<string>(), x.Verified))
            .ToList();
    }

    public async Task<List<SteamGridDbArtworkOption>> GetArtworkOptionsAsync(int gameId, string apiKey, bool vertical, CancellationToken cancellationToken = default)
    {
        EnsureApiKey(apiKey);

        // Primeiro pede os tamanhos padrão. Alguns jogos antigos não possuem grid
        // exatamente 600x900, embora tenham arte vertical válida no SteamGridDB.
        var dimensions = vertical ? "600x900" : "920x430,460x215";
        var exact = await GetArtworkOptionsCoreAsync(
            $"grids/game/{gameId}?dimensions={Uri.EscapeDataString(dimensions)}&types=static&nsfw=false&humor=false",
            apiKey,
            vertical,
            exactDimensionsOnly: true,
            cancellationToken: cancellationToken);

        if (exact.Count > 0)
            return exact;

        // Fallback por proporção: evita deixar a capa vazia só porque a comunidade
        // publicou 342x482, 512x768, 600x800 etc. Continua separando vertical de
        // horizontal e nunca encaixa uma orientação na outra.
        return await GetArtworkOptionsCoreAsync(
            $"grids/game/{gameId}?types=static&nsfw=false&humor=false",
            apiKey,
            vertical,
            exactDimensionsOnly: false,
            cancellationToken: cancellationToken);
    }

    private async Task<List<SteamGridDbArtworkOption>> GetArtworkOptionsCoreAsync(
        string requestUrl,
        string apiKey,
        bool vertical,
        bool exactDimensionsOnly,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(requestUrl, apiKey);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, "buscar artes");

        var json = await SafeHttpResponseService.ReadTextAsync(response, cancellationToken: cancellationToken);
        var wrapper = JsonSerializer.Deserialize<ResponseWrapper<SteamGridDbGrid>>(json, JsonOptions);
        var data = wrapper?.Data ?? new List<SteamGridDbGrid>();

        return data
            .Where(x => !string.IsNullOrWhiteSpace(x.Url) && x.Width > 0 && x.Height > 0)
            .Where(x => IsArtworkOrientationMatch(x.Width, x.Height, vertical, exactDimensionsOnly))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Width * x.Height)
            .Select(x => new SteamGridDbArtworkOption(
                x.Id, x.Url!, x.Thumb ?? x.Url!, x.Width, x.Height, x.Score, x.Style, x.Author?.Name))
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();
    }

    private static bool IsArtworkOrientationMatch(int width, int height, bool vertical, bool exactDimensionsOnly)
    {
        if (exactDimensionsOnly)
        {
            return vertical
                ? width == 600 && height == 900
                : (width == 920 && height == 430) || (width == 460 && height == 215);
        }

        var ratio = (double)width / height;
        return vertical
            ? height > width && ratio >= 0.58 && ratio <= 0.78
            : width > height && ratio >= 1.75 && ratio <= 2.35;
    }

    public async Task<int?> ResolveGameIdAsync(Game game, string apiKey, CancellationToken cancellationToken = default)
    {
        EnsureApiKey(apiKey);

        // Para jogos Steam, usa o App ID real em vez de depender somente do nome.
        // Isso evita casos como Mortal Kombat 11 ser associado a outro título.
        if (game.Platform == GamePlatform.Steam && int.TryParse(game.Id, out var appId))
        {
            try
            {
                using var request = CreateRequest($"games/steam/{appId}", apiKey);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await SafeHttpResponseService.ReadTextAsync(response, cancellationToken: cancellationToken);
                    var wrapper = JsonSerializer.Deserialize<ResponseWrapper<SteamGridDbGame>>(json, JsonOptions);
                    if (wrapper?.Data is { Count: > 0 })
                        return wrapper.Data[0].Id;
                }
            }
            catch { }
        }

        return await FindGameIdAsync(game.Name, apiKey, cancellationToken);
    }

    public async Task<int?> ResolveGameIdAsync(string gameName, string apiKey, CancellationToken cancellationToken = default)
    {
        return await FindGameIdAsync(gameName, apiKey, cancellationToken);
    }

    public async Task<SteamGridDbArtworkResult> DownloadBestArtworkAsync(Game game, string apiKey, bool vertical, int? selectedGameId = null, CancellationToken cancellationToken = default)
    {
        EnsureApiKey(apiKey);
        var gameId = selectedGameId ?? await FindGameIdAsync(game.Name, apiKey, cancellationToken);
        if (gameId is null)
            throw new InvalidOperationException($"O SteamGridDB não encontrou um jogo correspondente a \"{game.Name}\".");

        var options = await GetArtworkOptionsAsync(gameId.Value, apiKey, vertical, cancellationToken);
        var grid = options.FirstOrDefault();
        if (grid is null)
            throw new InvalidOperationException(vertical
                ? "Nenhuma capa vertical 600×900 foi encontrada no SteamGridDB para este jogo."
                : "Nenhuma arte horizontal 920×430 ou 460×215 foi encontrada no SteamGridDB para este jogo.");

        return await DownloadArtworkAsync(game, apiKey, grid, vertical, cancellationToken);
    }

    public async Task<SteamGridDbArtworkResult> DownloadArtworkAsync(
        Game game,
        string apiKey,
        SteamGridDbArtworkOption grid,
        bool vertical,
        CancellationToken cancellationToken = default)
    {
        EnsureApiKey(apiKey);
        var imageBytes = await SafeImageDownloadService.DownloadAsync(_http, grid.Url, cancellationToken);

        var extension = GetExtension(grid.Url);
        var safeId = string.Concat(game.ProviderId.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

        // Nunca sobrescreve uma arte que pode estar aberta pelo WPF. Isso evita
        // IOException quando o usuário troca rapidamente de uma arte para outra.
        // Cada seleção recebe seu próprio arquivo local.
        var unique = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";
        var target = Path.Combine(
            _directory,
            $"{safeId}_{(vertical ? "vertical" : "horizontal")}_steamgriddb_{grid.Id}_{unique}{extension}");
        await File.WriteAllBytesAsync(target, imageBytes, cancellationToken);

        if (vertical)
            game.Metadata.CustomVerticalCoverLocalPath = target;
        else
            game.Metadata.CustomHorizontalCoverLocalPath = target;

        return new SteamGridDbArtworkResult(grid.Url, grid.Score, grid.Style, grid.Author, target, grid.Width, grid.Height, grid.Id);
    }

    private async Task<int?> FindGameIdAsync(string gameName, string apiKey, CancellationToken cancellationToken)
    {
        var games = await SearchGamesAsync(gameName, apiKey, cancellationToken);
        if (games.Count == 0) return null;

        var exact = games.FirstOrDefault(x => string.Equals(x.Name.Trim(), gameName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Id;

        var normalized = Normalize(gameName);
        var normalizedMatch = games.FirstOrDefault(x => Normalize(x.Name) == normalized);
        if (normalizedMatch is not null) return normalizedMatch.Id;

        var verifiedSteam = games.FirstOrDefault(x => x.Verified && x.Types.Any(t => t.Equals("steam", StringComparison.OrdinalIgnoreCase)));
        return (verifiedSteam ?? games[0]).Id;
    }

    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static HttpRequestMessage CreateRequest(string url, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("A API Key do SteamGridDB foi recusada. Gere uma nova chave nas preferências da sua conta SteamGridDB.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"SteamGridDB retornou HTTP {(int)response.StatusCode} ao {operation}.");
        await Task.CompletedTask;
    }

    private static void EnsureApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Configure a API Key do SteamGridDB em Configurações antes de buscar artes.");
    }

    private static string GetExtension(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var ext = Path.GetExtension(path);
            if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
                return ext.ToLowerInvariant();
        }
        catch { }
        return ".png";
    }

    private sealed class ResponseWrapper<T>
    {
        [JsonPropertyName("data")]
        public List<T>? Data { get; set; }
    }

    private sealed class SteamGridDbGame
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public List<string>? Types { get; set; }
        public bool Verified { get; set; }
    }

    private sealed class SteamGridDbGrid
    {
        public int Id { get; set; }
        public int Score { get; set; }
        public string? Style { get; set; }
        public string? Url { get; set; }
        public string? Thumb { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public SteamGridDbAuthor? Author { get; set; }
    }

    private sealed class SteamGridDbAuthor
    {
        public string? Name { get; set; }
    }
}

public sealed record SteamGridDbGameResult(
    int Id,
    string Name,
    IReadOnlyList<string> Types,
    bool Verified);

public sealed record SteamGridDbArtworkOption(
    int Id,
    string Url,
    string Thumb,
    int Width,
    int Height,
    int Score,
    string? Style,
    string? Author);

public sealed record SteamGridDbArtworkResult(
    string Url,
    int Score,
    string? Style,
    string? Author,
    string LocalPath,
    int Width,
    int Height,
    int GridId);

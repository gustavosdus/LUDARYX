using System.Net.Http;
using System.Text.Json;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed class CoverService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly string _cache;

    public CoverService()
    {
        _cache = Path.Combine(AppDataService.RootDirectory, "covers");
        Directory.CreateDirectory(_cache);
    }

    public async Task<string?> GetCoverAsync(Game game, CancellationToken token = default, bool forceRefresh = false)
    {
        // Steam: use CDN header art directly; no authentication required for public artwork.
        if (game.Platform == GamePlatform.Steam && int.TryParse(game.Id, out var appId))
        {
            var url = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";
            var file = Path.Combine(_cache, $"steam_{appId}.jpg");
            var stale = !File.Exists(file) || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) >= TimeSpan.FromHours(24);
            if (forceRefresh || stale)
            {
                try
                {
                    var requestUrl = url + "?pcg_refresh=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var bytes = await SafeImageDownloadService.DownloadAsync(_http, requestUrl, token);
                    await File.WriteAllBytesAsync(file, bytes, token);
                }
                catch
                {
                    if (!File.Exists(file)) return null;
                }
            }
            return file;
        }
        return null;
    }
}

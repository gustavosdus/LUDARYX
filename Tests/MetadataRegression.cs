using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

internal static class MetadataRegression
{
    private const string English = "An adventure game where players explore a mysterious world, discover ancient secrets and challenge powerful enemies with their friends.";
    private const string Japanese = "これは広大な世界を探索する冒険ゲームです。プレイヤーは仲間と力を合わせ、古代の秘密を解き明かし、強力な敵と戦います。物語の中で新しい場所を発見し、さまざまな挑戦を楽しむことができます。";

    internal static async Task RunAsync()
    {
        foreach (var input in new[] { "e10", "E10", "E10+", "Everyone 10+", "ESRB E10" })
        {
            var metadata = new GameMetadata { AgeRatings = new() { ["esrb"] = input } };
            Require(AgeRatingService.GetDisplay(metadata, "en-US") == "ESRB E10+", $"Noncanonical ESRB label: {input}");
            Require(!ParentalControlService.IsGameAllowed(metadata, ParentalControlService.Age3To9), "E10 was allowed below its age group.");
            Require(ParentalControlService.IsGameAllowed(metadata, ParentalControlService.Age10To13), "E10 was blocked in its age group.");
        }

        var folder = Path.Combine(Path.GetTempPath(), "LUDARYX-metadata-tests-" + Guid.NewGuid().ToString("N"));
        using var http = new HttpClient(new FixtureHandler());
        try
        {
            var service = new MetadataService(http, folder);
            var game = new Game { Id = "123", Platform = GamePlatform.Steam, Name = "Stable installed name" };
            game.Metadata.Description = "Descrição antiga sem idioma que não pode ser tratada como inglês.";
            game.Metadata.LocalizedDescriptions["ja-JP"] = "Old incorrectly tagged English fallback.";
            var settings = new LauncherSettings { Language = "ja-JP" };
            Require(service.GetDisplayDescription(game, settings) is null, "Untagged legacy text leaked into the UI.");
            await service.EnsureLocalizedDescriptionAsync(game, settings);
            Require(service.GetDisplayDescription(game, settings) == Japanese, "Requested Japanese description was not selected.");
            Require(LocalizedMetadataText.GetName(game.Metadata, "ja-JP") == "日本語のタイトル", "Japanese title was not stored.");
            Require(game.Name == "Stable installed name", "Localization changed the discovery identity.");

            settings.Language = "pt-BR";
            await service.EnsureLocalizedDescriptionAsync(game, settings);
            Require(service.GetDisplayDescription(game, settings) == English, "Missing Portuguese text did not fall back to English.");
            Require(!game.Metadata.LocalizedDescriptions.ContainsKey("pt-BR"), "English fallback was cached as Portuguese.");
            Require(LocalizedMetadataText.GetName(game.Metadata, "es-ES") == "English title", "Title fallback leaked another locale.");

            settings.ManualMetadata[game.ProviderId] = new ManualGameMetadata
            {
                Description = "Texto global antigo",
                LocalizedDescriptions = new() { ["en-US"] = "Explicit English override" }
            };
            settings.Language = "ja-JP";
            Require(service.GetDisplayDescription(game, settings) == Japanese, "Manual English fallback overrode the requested language.");
            settings.ManualMetadata[game.ProviderId].LocalizedDescriptions["ja-JP"] = "手動の説明";
            Require(service.GetDisplayDescription(game, settings) == "手動の説明", "Locale-specific manual override was ignored.");

            using var reloadedHttp = new HttpClient(new FixtureHandler());
            var reloaded = new MetadataService(reloadedHttp, folder);
            var restored = new Game { Id = "123", Platform = GamePlatform.Steam, Name = "Stable installed name" };
            reloaded.ApplyCachedMetadata(new[] { restored });
            Require(LocalizedMetadataText.GetName(restored.Metadata, "ja-JP") == "日本語のタイトル", "Localized title was lost after cache reload.");
            Require(LocalizedMetadataText.GetDescription(restored.Metadata, "ja-JP") == Japanese, "Localized description was lost after cache reload.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "store.steampowered.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var japanese = request.RequestUri.Query.Contains("l=japanese", StringComparison.Ordinal);
            var data = new
            {
                name = japanese ? "日本語のタイトル" : "English title",
                short_description = japanese ? Japanese : English
            };
            var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["123"] = new { success = true, data } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}

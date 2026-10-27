using System.IO;
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
            using (var timeoutHttp = new HttpClient(new TimeoutHandler()))
            {
                var timeoutService = new MetadataService(timeoutHttp, Path.Combine(folder, "timeouts"));
                var timeoutGame = new Game { Id = "123", Platform = GamePlatform.Steam, Name = "Timeout game" };
                await timeoutService.EnsureLocalizedDescriptionAsync(timeoutGame, new LauncherSettings { Language = "ja-JP" });
                Require(LocalizedMetadataText.GetDescription(timeoutGame.Metadata, "ja-JP") == Japanese,
                    "An English source timeout prevented the Japanese source from loading.");
                Require(!timeoutGame.Metadata.LocalizedDescriptions.ContainsKey("en-US"),
                    "A failed English lookup was incorrectly cached as successful.");
            }
            using (var cancellation = new CancellationTokenSource())
            using (var canceledHttp = new HttpClient(new CancelHandler(cancellation)))
            {
                var canceledService = new MetadataService(canceledHttp, Path.Combine(folder, "canceled"));
                var canceledGame = new Game { Id = "123", Platform = GamePlatform.Steam, Name = "Canceled game" };
                var propagated = false;
                try
                {
                    await canceledService.EnsureLocalizedDescriptionAsync(canceledGame,
                        new LauncherSettings { Language = "en-US" }, cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    propagated = true;
                }
                Require(propagated, "Caller cancellation was swallowed as an optional source failure.");
            }
            var retryHandler = new RetryHandler();
            using (var retryHttp = new HttpClient(retryHandler))
            {
                var retryService = new MetadataService(retryHttp, Path.Combine(folder, "retry"));
                var retryGame = new Game { Id = "123", Platform = GamePlatform.Steam, Name = "Retry game" };
                var retrySettings = new LauncherSettings { Language = "ja-JP" };
                await retryService.EnsureLocalizedDescriptionAsync(retryGame, retrySettings);
                Require(!retryGame.Metadata.LocalizedDescriptions.ContainsKey("ja-JP"), "Fixture unexpectedly returned Japanese.");
                retryHandler.Available = true;
                await retryService.EnsureLocalizedDescriptionAsync(retryGame, retrySettings);
                Require(!retryGame.Metadata.LocalizedDescriptions.ContainsKey("ja-JP"), "Automatic retry ignored the cooldown.");
                var notified = false;
                retryGame.PropertyChanged += (_, args) => notified |= args.PropertyName == nameof(Game.DisplayName);
                await retryService.EnsureLocalizedDescriptionAsync(retryGame, retrySettings, forceRefresh: true);
                Require(LocalizedMetadataText.GetDescription(retryGame.Metadata, "ja-JP") == Japanese,
                    "Explicit refresh failed to bypass the recent unsuccessful attempt.");
                Require(notified, "Localized title update did not notify UI bindings.");
                retryGame.Metadata.LocalizedNames["ja-JP"] = "Old cached title";
                await retryService.EnsureLocalizedDescriptionAsync(retryGame, retrySettings, forceRefresh: true);
                Require(LocalizedMetadataText.GetName(retryGame.Metadata, "ja-JP") == "日本語のタイトル",
                    "Explicit refresh reused a complete but stale locale cache.");

                retryGame.IsDuplicate = true;
                retryGame.CanonicalGameId = "same-game";
                var preferred = new Game { Id = "456", Platform = GamePlatform.Steam, Name = "Preferred game",
                    IsDuplicate = true, CanonicalGameId = "same-game" };
                LocalizedMetadataText.Prepare(preferred.Metadata);
                preferred.Metadata.LocalizedNames["ja-JP"] = "Older duplicate title";
                preferred.Metadata.LocalizedDescriptions["en-US"] = English;
                retrySettings.PreferredDuplicateProviders["same-game"] = preferred.ProviderId;
                DuplicateMetadataService.Synchronize(new[] { preferred, retryGame }, retrySettings);
                Require(LocalizedMetadataText.GetName(retryGame.Metadata, "ja-JP") == "日本語のタイトル",
                    "Duplicate synchronization overwrote the freshly localized title.");
                Require(LocalizedMetadataText.GetDescription(retryGame.Metadata, "ja-JP") == Japanese,
                    "Duplicate synchronization erased the freshly localized description.");
            }
            await CheckDescriptionRecoveryAsync(folder);
            await CheckManualPrecedenceAsync(folder);
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

            var previousLanguage = LocalizationService.CurrentLanguage;
            try
            {
                var state = new GameStateService();
                settings.ManualMetadata[game.ProviderId] = new ManualGameMetadata
                {
                    LocalizedNames = new() { ["ja-JP"] = "私のタイトル", ["pt-BR"] = "Meu título" }
                };
                state.Apply(game, settings);
                LocalizationService.SetLanguage("ja-JP");
                Require(game.DisplayName == "私のタイトル", "Japanese manual title was ignored.");
                LocalizationService.SetLanguage("pt-BR");
                Require(game.DisplayName == "Meu título", "Portuguese manual title was ignored.");
                LocalizationService.SetLanguage("en-US");
                Require(game.DisplayName == "English title", "Manual title leaked into another language.");
                Require(game.Metadata.LocalizedNames["ja-JP"] == "日本語のタイトル", "Manual title overwrote the automatic title.");
                await service.EnsureLocalizedDescriptionAsync(game, settings, forceRefresh: true);
                LocalizationService.SetLanguage("ja-JP");
                Require(game.DisplayName == "私のタイトル", "Metadata refresh erased the manual title.");
                var restoredSettings = JsonSerializer.Deserialize<LauncherSettings>(JsonSerializer.Serialize(settings))!;
                restoredSettings.ManualMetadata[game.ProviderId].LocalizedNames.Remove("ja-JP");
                state.Apply(game, restoredSettings);
                Require(game.DisplayName == "日本語のタイトル", "Removing the override did not restore the automatic title.");
                LocalizationService.SetLanguage("pt-BR");
                Require(game.DisplayName == "Meu título", "Removing one locale erased another manual title.");
                settings.ManualMetadata.Clear();
                state.Apply(game, settings);
            }
            finally
            {
                LocalizationService.SetLanguage(previousLanguage);
            }

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

            settings.ManualMetadata[game.ProviderId].LocalizedDescriptions["pt-BR"] = "Minha descrição";
            new GameStateService().Apply(game, settings);
            await service.EnsureLocalizedDescriptionAsync(game, settings, forceRefresh: true);
            Require(service.GetDisplayDescription(game, settings) == "手動の説明", "Refresh erased the Japanese manual description.");
            Require(game.Metadata.LocalizedDescriptions["ja-JP"] == Japanese, "Manual description overwrote the automatic cache.");
            settings.Language = "pt-BR";
            Require(service.GetDisplayDescription(game, settings) == "Minha descrição", "Portuguese manual description was not selected.");
            settings.Language = "es-ES";
            Require(service.GetDisplayDescription(game, settings) == English, "Manual English description leaked into another locale.");
            settings = JsonSerializer.Deserialize<LauncherSettings>(JsonSerializer.Serialize(settings))!;
            settings.ManualMetadata[game.ProviderId].LocalizedDescriptions.Remove("ja-JP");
            settings.Language = "ja-JP";
            new GameStateService().Apply(game, settings);
            Require(service.GetDisplayDescription(game, settings) == Japanese, "Clearing the override did not restore automatic Japanese.");
            Require(settings.ManualMetadata[game.ProviderId].LocalizedDescriptions["pt-BR"] == "Minha descrição",
                "Clearing Japanese removed the Portuguese description.");

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

    private static async Task CheckDescriptionRecoveryAsync(string folder)
    {
        var legacy = new GameMetadata { LocalizedTextVersion = 1 };
        legacy.LocalizedDescriptions["en-US"] = English;
        legacy.LocalizedDescriptions["ja-JP"] = Japanese;
        LocalizedMetadataText.Prepare(legacy);
        Require(LocalizedMetadataText.GetDescription(legacy, "ja-JP") == Japanese,
            "Title cache migration erased a valid localized description.");
        var handler = new RecoveryHandler();
        using var http = new HttpClient(handler);
        var service = new MetadataService(http, Path.Combine(folder, "recovery"));
        var settings = new LauncherSettings { Language = "en-US" };
        foreach (var name in new[] { "Mortal Kombat 4", "Mortal Kombat Trilogy", "Mortal Kombat Komplete Edition" })
        {
            var game = new Game { Id = name, Name = name,
                Platform = name.Contains("Komplete") ? GamePlatform.Manual : GamePlatform.GOG };
            await service.EnsureLocalizedDescriptionAsync(game, settings, forceRefresh: true);
            Require(!string.IsNullOrWhiteSpace(service.GetDisplayDescription(game, settings)),
                $"Known English source text was not displayed for {name}.");
            var recovered = service.GetDisplayDescription(game, settings);
            handler.Unavailable = true;
            await service.EnsureLocalizedDescriptionAsync(game, settings, forceRefresh: true);
            Require(service.GetDisplayDescription(game, settings) == recovered,
                "An unavailable source erased a recovered description.");
            settings.Language = "ja-JP";
            Require(service.GetDisplayDescription(game, settings) == recovered,
                "Recovered English text was not available as fallback.");
            settings.Language = "en-US";
            handler.Unavailable = false;
        }
    }

    private sealed class RecoveryHandler : HttpMessageHandler
    {
        public bool Unavailable { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Unavailable)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            object? payload = null;
            if (request.RequestUri!.Host == "api.gog.com")
                payload = new { title = "GOG fixture title", description = new { full = English } };
            else if (Uri.UnescapeDataString(request.RequestUri.AbsolutePath).Contains("Mortal_Kombat_(2011_video_game)"))
                payload = new { extract = "Mortal Kombat Komplete Edition is a fighting video game. " + English };
            return Task.FromResult(payload is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                });
        }
    }

    private static async Task CheckManualPrecedenceAsync(string folder)
    {
        using var http = new HttpClient(new FixtureHandler());
        var service = new MetadataService(http, Path.Combine(folder, "manual-precedence"));
        var settings = new LauncherSettings { Language = "en-US" };
        var cachedGame = new Game { Id = "123", Platform = GamePlatform.Steam, Name = "English title" };
        cachedGame.Metadata = new GameMetadata
        {
            Source = "Steam Store", ExternalId = "123", CanonicalName = "English title",
            Description = English, Developer = "Automatic developer", Publisher = "Automatic publisher",
            ReleaseYear = 2000, Genres = new() { "Action" }, UpdatedAtUtc = DateTime.UtcNow,
            HorizontalCoverLocalPath = "cached-horizontal", VerticalCoverLocalPath = "cached-vertical",
            AgeRatings = new() { ["esrb"] = "E" }
        };
        await service.EnsureLocalizedDescriptionAsync(cachedGame, settings);
        var game = new Game { Id = "123", Platform = GamePlatform.Steam, Name = "English title" };
        settings.ManualMetadata[game.ProviderId] = new ManualGameMetadata
        {
            LocalizedNames = new() { ["en-US"] = "My title" },
            LocalizedDescriptions = new() { ["en-US"] = "My description" },
            Developer = "My developer", Publisher = "My publisher", ReleaseYear = 1999,
            Genres = new() { "RPG" }, AgeRatings = new() { ["esrb"] = "M" },
            HorizontalCoverUrl = "my-horizontal", VerticalCoverUrl = "my-vertical"
        };
        var persisted = JsonSerializer.Serialize(settings);
        settings = JsonSerializer.Deserialize<LauncherSettings>(persisted)!;
        await service.EnrichGameAsync(game, settings);
        AssertManualFields(game, settings, service);

        // A cancellation must preserve overrides too, without swallowing cancellation.
        game.Metadata = new GameMetadata();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { await service.EnrichGameAsync(game, settings, token: cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        AssertManualFields(game, settings, service);

        game.Metadata.AgeRatings["esrb"] = "E";
        try { await service.EnsureLocalizedAgeRatingAsync(game, settings, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        AssertManualFields(game, settings, service);

        var duplicate = new Game { Id = "manual-copy", Platform = GamePlatform.Manual,
            Name = "Manual copy", IsDuplicate = true, CanonicalGameId = "group" };
        settings.ManualMetadata[duplicate.ProviderId] = JsonSerializer.Deserialize<ManualGameMetadata>(
            JsonSerializer.Serialize(settings.ManualMetadata[game.ProviderId]))!;
        cachedGame.IsDuplicate = true;
        cachedGame.CanonicalGameId = "group";
        settings.PreferredDuplicateProviders["group"] = cachedGame.ProviderId;
        var before = JsonSerializer.Serialize(settings.ManualMetadata);
        DuplicateMetadataService.Synchronize(new[] { cachedGame, duplicate }, settings);
        Require(JsonSerializer.Serialize(settings.ManualMetadata) == before, "Duplicate sync rewrote saved manual metadata.");
        AssertManualFields(duplicate, settings, service);
    }

    private static void AssertManualFields(Game game, LauncherSettings settings, MetadataService service)
    {
        Require(game.Metadata.Developer == "My developer", "Manual developer was overwritten.");
        Require(game.Metadata.Publisher == "My publisher", "Manual publisher was overwritten.");
        Require(game.Metadata.ReleaseYear == 1999, "Manual release year was overwritten.");
        Require(game.Metadata.Genres.SequenceEqual(GenreService.NormalizeMany(new[] { "RPG" })), "Manual genres were overwritten.");
        Require(AgeRatingService.GetValue(game.Metadata, "esrb") == "M", "Manual rating was overwritten.");
        Require(game.ManualLocalizedNames["en-US"] == "My title", "Manual title was overwritten.");
        Require(service.GetDisplayDescription(game, settings) == "My description", "Manual description was overwritten.");
        Require(game.Metadata.CustomHorizontalCoverLocalPath == "my-horizontal" &&
            game.Metadata.CustomVerticalCoverLocalPath == "my-vertical", "Manual artwork was overwritten.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class RetryHandler : FixtureHandler
    {
        public bool Available { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!Available && request.RequestUri!.Query.Contains("l=japanese", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class TimeoutHandler : FixtureHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "store.steampowered.com" &&
                request.RequestUri.Query.Contains("l=japanese", StringComparison.Ordinal))
                return base.SendAsync(request, cancellationToken);
            // HttpClient timeouts cancel its internal request token, not the caller's token.
            throw new TaskCanceledException("Simulated source timeout", new TimeoutException());
        }
    }

    private sealed class CancelHandler(CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Expected cancellation.");
        }
    }

    private class FixtureHandler : HttpMessageHandler
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

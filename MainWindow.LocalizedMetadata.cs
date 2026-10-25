using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher;

public partial class MainWindow
{
    private CancellationTokenSource? _libraryTextCts;

    private async void RefreshLibraryLocalizedTextAsync()
    {
        _libraryTextCts?.Cancel();
        _libraryTextCts?.Dispose();
        var cts = new CancellationTokenSource();
        _libraryTextCts = cts;
        var token = cts.Token;
        var language = _settings.Language;
        var snapshot = new LauncherSettings { Language = language, ManualMetadata = _settings.ManualMetadata };
        using var concurrency = new SemaphoreSlim(2);
        var tasks = _games.ToArray().Select(async game =>
        {
            await concurrency.WaitAsync(token);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                await _metadata.EnsureLocalizedDescriptionAsync(game, snapshot, timeout.Token);
                if (!token.IsCancellationRequested && _settings.Language == language)
                {
                    game.RefreshLocalizedLabels();
                    var selected = _visibleGames.ElementAtOrDefault(_selectedIndex);
                    if (selected?.ProviderId == game.ProviderId)
                    {
                        SelectedGameNameText.Text = game.DisplayName;
                        TvHeroTitle.Text = game.DisplayName;
                        var description = _metadata.GetDisplayDescription(game, snapshot)
                            ?? Services.LocalizationService.Translate("Sem descrição.");
                        SelectedGameDescriptionText.Text = description;
                        TvHeroDescription.Text = description;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Services.DiagnosticLogService.LogException("Could not refresh localized library text", exception);
            }
            finally { concurrency.Release(); }
        }).ToArray();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
    }
}

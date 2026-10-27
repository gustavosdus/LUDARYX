using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;

namespace UnifiedGameLauncher;

public partial class MainWindow
{
    #region Library loading and presentation

    private async Task RefreshLibrary(
        string loadingMessage = "CARREGANDO BIBLIOTECA...",
        bool forceArtworkRefresh = false,
        Task? uiPublishGate = null)
    {
        await SetLibraryLoadingAsync(true, loadingMessage);

        _settings = _settingsService.Load();
        SyncToolbarPreferenceSliders();
        LocalizationService.SetLanguage(_settings.Language);
        ApplyLanguage();
        ApplyVisualSettings();
        StatusText.Text = "Lendo suas bibliotecas de jogos...";

        try
        {
            // A descoberta local é a única etapa necessária para exibir a biblioteca.
            // Metadados e capas usam rede e são tratados depois como enriquecimento
            // opcional, para que uma API/CDN/DNS lenta nunca deixe a tela presa.
            using var discoveryCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));

            // Alguns providers executam trabalho síncrono antes do primeiro await
            // (Registro, manifests, VDF, PowerShell, etc.). Executá-los a partir da
            // thread da UI fazia o storyboard perder frames mesmo com APIs async.
            // Task.Run mantém toda a fase local fora do Dispatcher.
            var settingsSnapshot = _settings;
            var loadedGames = await Task.Run(async () =>
            {
                var discovered = await _library.LoadLibraryAsync(discoveryCts.Token)
                    .ConfigureAwait(false);

                discovered = discovered
                    .Where(g => !settingsSnapshot.ExcludedGameIds.Contains(
                        g.ProviderId,
                        StringComparer.OrdinalIgnoreCase))
                    .ToList();

                // Cache, preferências e duplicados trabalham apenas sobre modelos e
                // arquivos locais; também ficam fora da thread da animação.
                _metadata.ApplyCachedMetadata(discovered);

                foreach (var game in discovered)
                    _state.Apply(game, settingsSnapshot);

                _duplicates.Detect(discovered);
                return discovered;
            }, discoveryCts.Token);

            // Na abertura, espera somente a parte visual da introdução terminar antes
            // de criar/atualizar a grade. Atualizações manuais não fornecem este gate.
            if (uiPublishGate is not null)
                await uiPublishGate;

            // Publica imediatamente os jogos encontrados antes de qualquer acesso de rede.
            EnsureDuplicatePrimarySelections(loadedGames);
            if (DuplicateMetadataService.Synchronize(loadedGames, _settings))
                _settingsService.Save(_settings);
            PublishLoadedGames(loadedGames);
            StatusText.Text = $"{_games.Count} jogos na biblioteca";

            await SetLibraryLoadingAsync(true, "ATUALIZANDO BIBLIOTECA...");
            await EnrichLibraryBestEffortAsync(loadedGames, forceArtworkRefresh);

            // Garante uma última sincronização local antes de qualquer filtro/duplicata.
            // Isso também cobre caminhos de cache/idade localizada que não passam pela
            // substituição principal de metadados.
            foreach (var game in loadedGames)
                _state.Apply(game, _settings);

            // Metadados podem alterar o nome canônico. Recalcula duplicatas depois do
            // enriquecimento para que o filtro reflita os nomes finais e versões copiadas.
            _duplicates.Detect(loadedGames);
            EnsureDuplicatePrimarySelections(loadedGames);
            if (DuplicateMetadataService.Synchronize(loadedGames, _settings))
                _settingsService.Save(_settings);

            // Reaplica somente a apresentação depois do enriquecimento, sem substituir
            // a geração de objetos já exibida.
            ApplyCoverMode();
            BuildGenreFilter();
            ApplyFilter();
            StatusText.Text = $"{_games.Count} jogos na biblioteca";
        }
        catch (OperationCanceledException)
        {
            // Se a descoberta exceder o limite, mantém qualquer biblioteca anterior visível
            // e encerra o estado de carregamento em vez de ficar preso indefinidamente.
            StatusText.Text = _games.Count > 0
                ? $"{_games.Count} jogos na biblioteca"
                : "A descoberta da biblioteca excedeu o tempo limite";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Erro ao carregar biblioteca";
            LudaryxMessageBox.ShowError(ex, "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            await SetLibraryLoadingAsync(false);
        }
    }

    private void PublishLoadedGames(List<Game> loadedGames)
    {
        foreach (var game in _games.Concat(_visibleGames).Distinct())
            game.IsControllerSelected = false;

        _games = loadedGames;
        ApplyCoverMode();
        BuildGenreFilter();
        if (LibraryFilterCombo.SelectedItem is null)
            LibraryFilterCombo.SelectedIndex = 0;
        if (PlatformFilterCombo.SelectedItem is null)
            PlatformFilterCombo.SelectedIndex = 0;
        if (SortCombo.SelectedItem is null)
        {
            var sortItem = SortCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), _settings.LibrarySortMode, StringComparison.OrdinalIgnoreCase));
            SortCombo.SelectedItem = sortItem ?? SortCombo.Items[0];
        }
        UpdateSortDirectionButtonPresentation();
        _ = _sessions.RefreshRunningStateAsync(_games);
        ApplyFilter();
    }

    private static bool HasUsefulAutomaticMetadata(Game game)
    {
        var metadata = game.Metadata;
        if (metadata is null)
            return false;

        var hasRemoteSource = !string.IsNullOrWhiteSpace(metadata.Source) &&
                              !string.Equals(metadata.Source, "Local", StringComparison.OrdinalIgnoreCase);
        var hasDetails = !string.IsNullOrWhiteSpace(metadata.Description) ||
                         metadata.Genres.Count > 0 ||
                         !string.IsNullOrWhiteSpace(metadata.Developer);
        var hasVertical = !string.IsNullOrWhiteSpace(metadata.VerticalCoverLocalPath) &&
                          File.Exists(metadata.VerticalCoverLocalPath);
        var hasHorizontal = !string.IsNullOrWhiteSpace(metadata.HorizontalCoverLocalPath) &&
                            File.Exists(metadata.HorizontalCoverLocalPath);

        return hasRemoteSource && hasDetails && hasVertical && hasHorizontal;
    }

    private async Task EnrichLibraryBestEffortAsync(List<Game> loadedGames, bool forceArtworkRefresh)
    {
        // Capas oficiais da Steam usam URLs estáveis mesmo quando o criador troca
        // a imagem. Revalida o arquivo local antes do enriquecimento genérico.
        // A biblioteca já está visível, então essa etapa nunca bloqueia sua abertura.
        using (var steamArtworkCts = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
        {
            try
            {
                await _metadata.RefreshSteamArtworkAsync(
                    loadedGames,
                    forceArtworkRefresh,
                    steamArtworkCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        if (_settings.EnrichMetadataAutomatically)
        {
            // Prioriza jogos de outros launchers. Em versões anteriores o enriquecimento
            // era sequencial e um limite global podia expirar antes de Epic/GOG/Xbox/EA/
            // Ubisoft/Battle.net/Riot serem alcançados em bibliotecas grandes.
            var orderedGames = loadedGames
                .OrderBy(game => game.Platform == GamePlatform.Steam ? 1 : 0)
                .ThenBy(game => HasUsefulAutomaticMetadata(game) ? 1 : 0)
                .ToList();

            // Não existe mais um timeout GLOBAL para a biblioteca. Em versões anteriores,
            // o CancellationToken de 180 s cancelava jogos que ainda nem tinham recebido
            // sua vez no SemaphoreSlim, então cada clique em Atualizar corrigia apenas mais
            // uma parte da biblioteca. Agora todo jogo recebe uma tentativa própria.
            await EnrichMetadataPassAsync(
                orderedGames,
                forceArtworkRefresh,
                maxConcurrency: 4,
                perGameTimeout: TimeSpan.FromSeconds(45));

            // Persiste o progresso da primeira passada antes dos retries. Assim uma
            // eventual falha posterior não perde jogos que já foram corrigidos.
            _metadata.SaveCacheSnapshot();

            // Segunda passada apenas para entradas que continuam claramente incompletas.
            // Isso absorve falhas transitórias das APIs dentro do MESMO clique em Atualizar.
            var retryGames = orderedGames
                .Where(NeedsMetadataRetry)
                .ToList();

            if (retryGames.Count > 0)
            {
                await EnrichMetadataPassAsync(
                    retryGames,
                    forceArtworkRefresh: false,
                    maxConcurrency: 3,
                    perGameTimeout: TimeSpan.FromSeconds(35));
            }

            _metadata.SaveCacheSnapshot();
        }


        // Busca capas em paralelo com concorrência limitada e um teto global.
        // Falhas individuais são ignoradas; o jogo continua utilizável sem arte.
        using var coverCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var gate = new SemaphoreSlim(6);

        var coverTasks = loadedGames.Take(160).Select(async game =>
        {
            if (!forceArtworkRefresh && !string.IsNullOrWhiteSpace(game.CoverImage))
                return;

            try
            {
                await gate.WaitAsync(coverCts.Token);
                try
                {
                    game.CoverImage = await _covers.GetCoverAsync(game, coverCts.Token, forceArtworkRefresh);
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        });

        try
        {
            await Task.WhenAll(coverTasks);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task EnrichMetadataPassAsync(
        IReadOnlyCollection<Game> games,
        bool forceArtworkRefresh,
        int maxConcurrency,
        TimeSpan perGameTimeout)
    {
        using var gate = new SemaphoreSlim(maxConcurrency);

        var tasks = games.Select(async game =>
        {
            await gate.WaitAsync();
            try
            {
                using var perGameCts = new CancellationTokenSource(perGameTimeout);
                try
                {
                    await _metadata.EnrichGameAsync(
                        game,
                        _settings,
                        forceArtworkRefresh,
                        perGameCts.Token);

                    // EnrichGameAsync pode substituir Game.Metadata pelo objeto recém-
                    // obtido/cacheado. Reaplica imediatamente as preferências e os
                    // metadados manuais (inclusive ESRB E10+, ClassInd e PEGI) para que
                    // o filtro parental nunca avalie uma versão temporária sem os dados
                    // persistidos pelo usuário.
                    _state.Apply(game, _settings);
                }
                catch (OperationCanceledException)
                {
                    // O timeout pertence somente a este jogo. A fila continua até o fim.
                }
                catch
                {
                    // Metadados são opcionais; uma falha individual não cancela o lote.
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static bool NeedsMetadataRetry(Game game)
    {
        var metadata = game.Metadata;
        if (metadata is null)
            return true;

        var missingText = string.IsNullOrWhiteSpace(metadata.Description) ||
                          metadata.Genres.Count == 0 ||
                          string.IsNullOrWhiteSpace(metadata.Developer) ||
                          string.IsNullOrWhiteSpace(metadata.Publisher) ||
                          !metadata.ReleaseYear.HasValue;

        if (game.Platform != GamePlatform.Steam)
            return missingText;

        // Para Steam, além de completar campos, exige que a fonte principal e o App ID
        // correspondam ao jogo instalado. Isso força a limpeza de caches antigos GOG/Wiki.
        var source = metadata.Source ?? string.Empty;
        var primarySource = source.Split(
            " + ",
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        return missingText ||
               !primarySource.StartsWith("Steam Store", StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(metadata.ExternalId, game.Id, StringComparison.OrdinalIgnoreCase);
    }


    private async Task SetLibraryLoadingAsync(bool isLoading, string? message = null)
    {
        _isLibraryLoading = isLoading;

        if (!string.IsNullOrWhiteSpace(message))
        {
            var localizedMessage = LocalizationService.Translate(message);
            LibraryLoadingText.Text = localizedMessage;
            FullscreenLibraryLoadingText.Text = localizedMessage;
            StartupLibraryLoadingText.Text = localizedMessage;
        }

        var visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

        // Desktop mantém o indicador pequeno do cabeçalho. No fullscreen, a
        // atualização ganha um indicador próprio sobre a área de jogos.
        LibraryLoadingText.Visibility = !_fullscreen ? visibility : Visibility.Collapsed;
        FullscreenLibraryLoadingText.Visibility = Visibility.Collapsed;
        FullscreenLoadingIndicator.Visibility = _fullscreen ? visibility : Visibility.Collapsed;

        StartupLibraryLoadingText.Visibility = StartupOverlay.Visibility == Visibility.Visible
            ? visibility
            : Visibility.Collapsed;

        // Dá ao WPF a chance de desenhar o indicador antes de iniciar trabalho pesado.
        // Sem isto, providers que executam parte da descoberta de forma síncrona podem
        // bloquear a UI antes do próximo frame e o texto nunca chega a aparecer.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private void ApplyCoverMode()
    {
        foreach (var game in _games)
        {
            game.CoverImage = _settings.CoverMode == "Vertical"
                ? game.VerticalCover
                : game.HorizontalCover;
        }

        UpdateCoverDimensions();
    }

    private void UpdateCoverDimensions()
    {
        // A grade usa de quatro a seis colunas. O painel lateral do redesign reduz a largura
        // disponível no desktop, então dimensões fixas faziam as capas ultrapassarem
        // suas células e serem recortadas. Calculamos a largura real por célula e
        // preservamos a proporção da arte em ambos os modos.
        var viewportWidth = LibraryScroll?.ViewportWidth ?? 0;
        var libraryWidth = viewportWidth > 1
            ? Math.Max(1, viewportWidth - 42) // Padding horizontal do ScrollViewer: 24 + 18.
            : LibraryAreaBorder?.ActualWidth ?? 0;

        if (libraryWidth <= 1)
        {
            libraryWidth = Math.Max(720, ActualWidth - (_fullscreen ? 36 : 370)) - 42;
        }

        var columns = GetColumns();
        GameList.Tag = columns;

        // 28 px pertencem à margem do card. A folga adicional garante que o
        // contorno de foco não encoste no limite da célula, especialmente com
        // 4/5 colunas e o painel lateral ativo.
        const double cellHorizontalSpace = 44;
        var availableCardWidth = Math.Max(96, (libraryWidth / columns) - cellHorizontalSpace);

        var vertical = _settings.CoverMode == "Vertical";
        var maxWidth = vertical
            ? columns switch
            {
                4 => 250d,
                5 => 215d,
                _ => 180d
            }
            : columns switch
            {
                4 => 300d,
                5 => 240d,
                _ => 196d
            };
        const double cardBorderTotal = 4; // 2 px em cada lado.
        var outerWidth = Math.Min(maxWidth, availableCardWidth);
        var contentWidth = Math.Max(1, outerWidth - cardBorderTotal);

        foreach (var game in _games)
        {
            var contentHeight = vertical
                ? contentWidth * 1.5
                : contentWidth / GetArtworkAspectRatio(game.DisplayCover, 16.0 / 9.0);

            // A proporção é aplicada à área interna, não ao tamanho externo do Border.
            // Isso elimina a faixa vazia lateral causada pela espessura do contorno.
            game.CoverWidth = Math.Round(contentWidth + cardBorderTotal, 1);
            game.CoverHeight = Math.Round(contentHeight + cardBorderTotal, 1);
        }

        GameList?.Items.Refresh();
    }

    private sealed class GenreFilterOption
    {
        public string? CanonicalGenre
        {
            get; init;
        }
        public required string DisplayName
        {
            get; init;
        }

        public override string ToString() => DisplayName;
    }

    private void BuildGenreFilter()
    {
        var currentCanonicalGenre = (GenreFilter.SelectedItem as GenreFilterOption)?.CanonicalGenre;

        GenreFilter.Items.Clear();
        GenreFilter.Items.Add(new GenreFilterOption
        {
            CanonicalGenre = null,
            DisplayName = LocalizationService.Translate("TODOS OS GÊNEROS")
        });

        var genres = GenreService.NormalizeMany(_games.SelectMany(game => game.Metadata.Genres))
            .Select(genre => new GenreFilterOption
            {
                CanonicalGenre = genre,
                DisplayName = GenreService.Display(genre)
            })
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase);

        foreach (var genre in genres)
            GenreFilter.Items.Add(genre);

        GenreFilter.SelectedItem = GenreFilter.Items
            .OfType<GenreFilterOption>()
            .FirstOrDefault(option => string.Equals(
                option.CanonicalGenre,
                currentCanonicalGenre,
                StringComparison.OrdinalIgnoreCase))
            ?? GenreFilter.Items[0];
    }

    #endregion
}

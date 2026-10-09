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
            MessageBox.Show(ex.Message, "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Error);
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
        public string? CanonicalGenre { get; init; }
        public required string DisplayName { get; init; }

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

    #region Filtering and search

    private void LibraryFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LibraryFilterCombo.SelectedItem is not ComboBoxItem item) return;
        _specialFilter = item.Tag?.ToString() switch
        {
            "favorites" => "favorites",
            "recent" => "recent",
            "duplicates" => "duplicates",
            "hidden" => "hidden",
            _ => ""
        };
        ApplyFilter();
    }

    private void PlatformFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || PlatformFilterCombo.SelectedItem is not ComboBoxItem item) return;
        _platformFilter = item.Tag?.ToString() switch
        {
            "Steam" => GamePlatform.Steam,
            "Epic" => GamePlatform.Epic,
            "GOG" => GamePlatform.GOG,
            "Xbox" => GamePlatform.Xbox,
            "EAApp" => GamePlatform.EAApp,
            "UbisoftConnect" => GamePlatform.UbisoftConnect,
            "BattleNet" => GamePlatform.BattleNet,
            "RiotClient" => GamePlatform.RiotClient,
            "Manual" => GamePlatform.Manual,
            _ => null
        };
        ApplyFilter();
    }

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SortCombo.SelectedItem is not ComboBoxItem item) return;
        _settings.LibrarySortMode = item.Tag?.ToString() ?? "Name";
        _settingsService.Save(_settings);
        ApplyFilter();
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var window = new AddGameWindow { Owner = this };
        if (window.ShowDialog() != true || window.Result is null) return;
        _settings = _settingsService.Load();
        _settings.ManualGames.Add(window.Result);
        _settingsService.Save(_settings);
        await RefreshLibrary("ATUALIZANDO BIBLIOTECA...");
    }
    private void GenreFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSearchBoxes) return;
        _syncingSearchBoxes = true;
        if (FullscreenSearchBox is not null) FullscreenSearchBox.Text = SearchBox.Text;
        _syncingSearchBoxes = false;
        if (IsLoaded) ApplyFilter();
    }

    private void FullscreenSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSearchBoxes) return;
        _syncingSearchBoxes = true;
        if (SearchBox is not null) SearchBox.Text = FullscreenSearchBox.Text;
        _syncingSearchBoxes = false;
        if (IsLoaded) ApplyFilter();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
        Dispatcher.BeginInvoke(UpdateCoverDimensions, DispatcherPriority.Loaded);
    }

    private void LibraryScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        BackToTopButton.Visibility = e.VerticalOffset > 320 && _visibleGames.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void BackToTop_Click(object sender, RoutedEventArgs e)
    {
        LibraryScroll.ScrollToTop();
        BackToTopButton.Visibility = Visibility.Collapsed;
        GameList.Focus();
        Keyboard.Focus(GameList);
    }

    private void UpdateResponsiveLayout()
    {
        if (!IsLoaded)
            return;

        var showHero = _fullscreen && !_settings.HideGameDetailsPanels;
        TvHeroPanel.Visibility = showHero ? Visibility.Visible : Visibility.Collapsed;
        if (!showHero)
            _tvHeroMode = false;

        if (_fullscreen)
        {
            SelectedPanelColumn.Width = new GridLength(0);
            SelectedGamePanel.Visibility = Visibility.Collapsed;
            return;
        }

        var showSidePanel = ActualWidth >= 1120 && !_settings.HideGameDetailsPanels;
        SelectedPanelColumn.Width = showSidePanel ? new GridLength(330) : new GridLength(0);
        SelectedGamePanel.Visibility = showSidePanel ? Visibility.Visible : Visibility.Collapsed;
        if (!showSidePanel)
            _sidePanelMode = false;
    }

    private void UpdateSelectedGamePresentation()
    {
        var game = _visibleGames.ElementAtOrDefault(_selectedIndex);
        EmptyStatePanel.Visibility = game is null ? Visibility.Visible : Visibility.Collapsed;

        if (game is null)
        {
            SelectedGameNameText.Text = LocalizationService.Translate("Nenhum jogo");
            SelectedGameMetaText.Text = string.Empty;
            SelectedGameUsageText.Text = string.Empty;
            SelectedGameDescriptionText.Text = string.Empty;
            SelectedArtworkImage.Source = null;
            TvHeroArtwork.Source = null;
            TvHeroPreviewImage.Source = null;
            TvHeroTitle.Text = "LUDARYX";
            TvHeroMeta.Text = string.Empty;
            TvHeroUsage.Text = string.Empty;
            TvHeroDescription.Text = string.Empty;
            return;
        }

        var localizedGenres = GenreService.DisplayMany(game.Metadata.Genres);
        var genres = string.IsNullOrWhiteSpace(localizedGenres)
            ? LocalizationService.Translate("Não informado")
            : localizedGenres;
        var rating = AgeRatingService.GetDisplay(game.Metadata, _settings.Language);
        var description = _metadata.GetDisplayDescription(game, _settings);
        description = string.IsNullOrWhiteSpace(description)
            ? LocalizationService.Translate("Sem descrição.")
            : description;

        SelectedGameNameText.Text = game.Name;
        SelectedGameMetaText.Text = $"{game.PlatformDisplay}  •  {genres}";
        SelectedGameUsageText.Text =
            $"{game.PlayCountDisplay}  •  {game.TotalPlayTimeDisplay}\n" +
            $"{LocalizationService.Translate("Última execução")}: {game.LastPlayedDisplay}\n" +
            $"{LocalizationService.Translate("Classificação indicativa")}: {rating}";
        SelectedGameDescriptionText.Text = description;
        SelectedFavoriteButton.Content = game.IsFavorite ? "★ FAVORITO" : "☆ FAVORITO";
        SelectedPlayButton.Content = game.IsRunning ? "EM EXECUÇÃO" : "JOGAR";
        SelectedPlayButton.IsEnabled = !game.IsRunning;

        TvHeroTitle.Text = game.Name;
        TvHeroMeta.Text = $"{game.PlatformDisplay}  •  {genres}  •  {rating}";
        TvHeroUsage.Text = $"{game.TotalPlayTimeDisplay}  •  {game.PlayCountDisplay}  •  {game.LastPlayedDisplay}";
        TvHeroDescription.Text = description;
        TvPlayButton.IsEnabled = !game.IsRunning;
        UpdateTvHeroInputLabels();

        var heroPath = game.HeroArtwork;
        var coverPath = game.DisplayCover;
        var heroImage = LoadHomeArtwork(heroPath);
        var coverImage = LoadHomeArtwork(coverPath);
        var selectedSource = heroImage ?? coverImage;
        var selectedKey = !string.IsNullOrWhiteSpace(heroPath) && heroImage is not null
            ? heroPath
            : coverPath;

        ApplyArtworkWithFade(SelectedArtworkImage, selectedSource, selectedKey);
        ApplyArtworkWithFade(TvHeroArtwork, selectedSource, selectedKey);
        // No hero/header do modo TV, prioriza sempre a arte horizontal.
        // A capa vertical fica apenas como fallback quando não houver arte horizontal disponível.
        ApplyArtworkWithFade(TvHeroPreviewImage, selectedSource, selectedKey);
        UpdateSelectedArtworkFrames(selectedSource);

        RefreshSelectedLocalizedDescriptionAsync(game);
    }

    private async void RefreshSelectedLocalizedDescriptionAsync(Game game)
    {
        _selectedDescriptionCts?.Cancel();
        _selectedDescriptionCts?.Dispose();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _selectedDescriptionCts = cts;

        try
        {
            var localized = await _metadata.EnsureLocalizedDescriptionAsync(game, _settings, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            var selected = _visibleGames.ElementAtOrDefault(_selectedIndex);
            if (selected is null ||
                !selected.ProviderId.Equals(game.ProviderId, StringComparison.OrdinalIgnoreCase))
                return;

            var display = _metadata.GetDisplayDescription(game, _settings) ?? localized;
            if (string.IsNullOrWhiteSpace(display))
                display = LocalizationService.Translate("Sem descrição.");

            SelectedGameDescriptionText.Text = display;
            TvHeroDescription.Text = display;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // A descrição já exibida continua válida quando a atualização localizada falha.
        }
    }

    private static void ApplyArtworkWithFade(System.Windows.Controls.Image image, ImageSource? source, string? sourceKey)
    {
        // CoverImageConverter pode criar uma nova instância de ImageSource toda vez que
        // a seleção é reapresentada. Comparar apenas ReferenceEquals fazia a mesma arte
        // reiniciar o fade continuamente ao passar o mouse sobre o card selecionado.
        if (string.Equals(image.Tag as string, sourceKey, StringComparison.OrdinalIgnoreCase))
            return;

        image.Tag = sourceKey;
        image.BeginAnimation(OpacityProperty, null);
        image.Opacity = source is null ? 1 : 0.15;
        image.Source = source;

        if (source is null)
            return;

        image.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.15, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private ImageSource? LoadHomeArtwork(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            if (FindResource("CoverImageConverter") is CoverImageConverter converter)
                return converter.Convert(
                    path,
                    typeof(ImageSource),
                    null!,
                    System.Globalization.CultureInfo.CurrentCulture) as ImageSource;
        }
        catch
        {
        }

        return null;
    }

    private double GetArtworkAspectRatio(string? path, double fallback)
    {
        if (string.IsNullOrWhiteSpace(path))
            return fallback;

        if (_artworkAspectRatioCache.TryGetValue(path, out var cached))
            return cached;

        var source = LoadHomeArtwork(path);
        var ratio = GetArtworkAspectRatio(source, fallback);
        _artworkAspectRatioCache[path] = ratio;
        return ratio;
    }

    private static double GetArtworkAspectRatio(ImageSource? source, double fallback)
    {
        if (source is System.Windows.Media.Imaging.BitmapSource bitmap &&
            bitmap.PixelWidth > 0 &&
            bitmap.PixelHeight > 0)
        {
            var ratio = (double)bitmap.PixelWidth / bitmap.PixelHeight;
            if (double.IsFinite(ratio) && ratio > 0.1)
                return ratio;
        }

        return fallback;
    }

    private void UpdateSelectedArtworkFrames(ImageSource? source)
    {
        var ratio = GetArtworkAspectRatio(source, 16.0 / 9.0);

        // O quadro acompanha a proporção real da arte. Assim a imagem continua
        // inteira com Stretch=Uniform, sem letterboxing e sem recorte.
        Dispatcher.BeginInvoke(() =>
        {
            // Usa uma largura estável do painel e reserva sempre o espaço da
            // scrollbar. ViewportWidth muda quando a barra aparece/desaparece e isso
            // criava um ciclo de resize que fazia a mesma arte alternar de tamanho.
            var panelWidth = SelectedGamePanel.ActualWidth;
            var reservedScrollbar = SystemParameters.VerticalScrollBarWidth;
            var sideMaxWidth = Math.Max(
                1,
                panelWidth > 1
                    ? panelWidth - 30 - reservedScrollbar
                    : 300 - reservedScrollbar);

            const double sideMaxHeight = 160;
            var sideWidth = Math.Min(sideMaxWidth, sideMaxHeight * ratio);
            var sideHeight = sideWidth / ratio;

            SelectedArtworkBorder.Width = sideWidth;
            SelectedArtworkBorder.Height = sideHeight;
            SelectedArtworkBorder.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;

            const double heroMaxWidth = 340;
            const double heroMaxHeight = 190;
            var heroWidth = Math.Min(heroMaxWidth, heroMaxHeight * ratio);
            var heroHeight = heroWidth / ratio;

            TvHeroPreviewBorder.Width = heroWidth;
            TvHeroPreviewBorder.Height = heroHeight;
        }, DispatcherPriority.Loaded);
    }

    private void SelectedGameScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Recalcula apenas quando a largura externa realmente muda. A presença da
        // scrollbar já é considerada de forma fixa em UpdateSelectedArtworkFrames,
        // evitando oscilações entre dois tamanhos.
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1)
            UpdateSelectedArtworkFrames(SelectedArtworkImage.Source);
    }

    private void FramedArtworkImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Image image ||
            image.ActualWidth <= 0 ||
            image.ActualHeight <= 0)
        {
            return;
        }

        var radius = 0d;
        DependencyObject? current = image;

        while (current is not null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is Border border)
            {
                radius = Math.Max(
                    0,
                    border.CornerRadius.TopLeft -
                    Math.Max(border.BorderThickness.Left, border.BorderThickness.Top));
                break;
            }
        }

        image.Clip = new RectangleGeometry(
            new Rect(0, 0, image.ActualWidth, image.ActualHeight),
            radius,
            radius);
    }

    private void UpdateFilterChips()
    {
        var collectionItem = LibraryFilterCombo.SelectedItem as ComboBoxItem;
        var collectionTag = collectionItem?.Tag?.ToString() ?? "all";
        CollectionFilterChip.Visibility = collectionTag == "all" ? Visibility.Collapsed : Visibility.Visible;
        CollectionFilterChip.Content = collectionTag == "all" ? null : $"{collectionItem?.Content}  ×";

        var platformItem = PlatformFilterCombo.SelectedItem as ComboBoxItem;
        var platformTag = platformItem?.Tag?.ToString() ?? "all";
        PlatformFilterChip.Visibility = platformTag == "all" ? Visibility.Collapsed : Visibility.Visible;
        PlatformFilterChip.Content = platformTag == "all" ? null : $"{platformItem?.Content}  ×";

        if (GenreFilter.SelectedItem is GenreFilterOption genre && genre.CanonicalGenre is not null)
        {
            GenreFilterChip.Visibility = Visibility.Visible;
            GenreFilterChip.Content = $"{genre.DisplayName}  ×";
        }
        else
        {
            GenreFilterChip.Visibility = Visibility.Collapsed;
        }

        var sortItem = SortCombo.SelectedItem as ComboBoxItem;
        var sortTag = sortItem?.Tag?.ToString() ?? "Name";
        SortFilterChip.Visibility = sortTag.Equals("Name", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Collapsed
            : Visibility.Visible;
        SortFilterChip.Content = sortTag.Equals("Name", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{sortItem?.Content}  ×";
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _syncingSearchBoxes = true;
        SearchBox.Text = string.Empty;
        FullscreenSearchBox.Text = string.Empty;
        _syncingSearchBoxes = false;

        LibraryFilterCombo.SelectedIndex = 0;
        PlatformFilterCombo.SelectedIndex = 0;
        GenreFilter.SelectedIndex = 0;
        SortCombo.SelectedIndex = 0;
        _specialFilter = string.Empty;
        _platformFilter = null;
        ApplyFilter();
    }

    private void ClearCollectionFilter_Click(object sender, RoutedEventArgs e)
    {
        LibraryFilterCombo.SelectedIndex = 0;
    }

    private void ClearPlatformFilter_Click(object sender, RoutedEventArgs e)
    {
        PlatformFilterCombo.SelectedIndex = 0;
    }

    private void ClearGenreFilter_Click(object sender, RoutedEventArgs e)
    {
        GenreFilter.SelectedIndex = 0;
        ApplyFilter();
    }

    private void ResetSortFilter_Click(object sender, RoutedEventArgs e)
    {
        SortCombo.SelectedIndex = 0;
    }

    private async void SelectedPlay_Click(object sender, RoutedEventArgs e)
    {
        var game = _visibleGames.ElementAtOrDefault(_selectedIndex);
        if (game is not null)
            await LaunchGameAsync(game);
    }

    private void SelectedDetails_Click(object sender, RoutedEventArgs e)
    {
        var game = _visibleGames.ElementAtOrDefault(_selectedIndex);
        if (game is not null)
            OpenDetails(game);
    }

    private void SelectedFavorite_Click(object sender, RoutedEventArgs e)
    {
        ToggleSelectedFavorite();
        UpdateSelectedGamePresentation();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var genre = (GenreFilter.SelectedItem as GenreFilterOption)?.CanonicalGenre;
        var showHidden = _settings.ShowHiddenApps;

        IEnumerable<Game> filtered = _games.Where(g =>
            !g.IsExcluded &&
            (_specialFilter == "hidden"
                ? g.IsHidden || IsAutoHiddenDuplicateSecondary(g)
                : (showHidden || !g.IsHidden)) &&
            (!_platformFilter.HasValue || g.Platform == _platformFilter.Value) &&
            (_specialFilter != "favorites" || g.IsFavorite) &&
            (_specialFilter != "recent" || g.LastPlayedUtc.HasValue) &&
            (_specialFilter != "duplicates" || g.IsDuplicate) &&
            (!_settings.AutoHideDuplicateSecondary ||
             !g.IsDuplicate ||
             !IsAutoHiddenDuplicateSecondary(g) ||
             _specialFilter is "duplicates" or "hidden") &&
            (genre == null || g.Metadata.Genres.Any(x => x.Equals(genre, StringComparison.OrdinalIgnoreCase))) &&
            SearchNormalizationService.Matches(g, query));

        filtered = (_settings.LibrarySortMode ?? "Name") switch
        {
            "Recent" => filtered.OrderByDescending(g => g.LastPlayedUtc ?? DateTime.MinValue)
                                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            "MostPlayed" => filtered.OrderByDescending(g => g.PlayCount)
                                    .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            "PlayTime" => filtered.OrderByDescending(g => g.TotalPlayTimeSeconds)
                                  .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            "Platform" => filtered.OrderBy(g => g.PlatformDisplay, StringComparer.OrdinalIgnoreCase)
                                  .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            _ when _specialFilter == "recent" => filtered.OrderByDescending(g => g.LastPlayedUtc ?? DateTime.MinValue)
                                                         .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
        };

        _visibleGames = filtered.ToList();

        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _visibleGames.Count - 1));
        UpdateControllerSelection();
        GameList.ItemsSource = null;
        GameList.ItemsSource = _visibleGames;
        UpdateControllerSelection();
        GameCountText.Text = LocalizedGameCount(_visibleGames.Count);
        UpdateFilterChips();
        UpdateSelectedGamePresentation();
    }

    private bool IsAutoHiddenDuplicateSecondary(Game game)
    {
        if (!_settings.AutoHideDuplicateSecondary ||
            !game.IsDuplicate ||
            string.IsNullOrWhiteSpace(game.CanonicalGameId))
        {
            return false;
        }

        return _settings.PreferredDuplicateProviders.TryGetValue(
                   game.CanonicalGameId,
                   out var preferredProvider) &&
               !game.ProviderId.Equals(
                   preferredProvider,
                   StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureDuplicatePrimarySelections(IEnumerable<Game> games)
    {
        if (!_settings.AutoHideDuplicateSecondary)
            return;

        var changed = false;
        var priority = _settings.DuplicatePlatformPriority
            .Select(value => Enum.TryParse<GamePlatform>(value, true, out var platform) ? platform : (GamePlatform?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        if (priority.Length == 0)
            priority = new[]
            {
                GamePlatform.Steam, GamePlatform.Epic, GamePlatform.GOG, GamePlatform.Xbox,
                GamePlatform.EAApp, GamePlatform.UbisoftConnect, GamePlatform.BattleNet,
                GamePlatform.RiotClient, GamePlatform.Manual
            };

        foreach (var group in games.Where(g => g.IsDuplicate)
                     .GroupBy(g => g.CanonicalGameId, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key) ||
                _settings.PreferredDuplicateProviders.ContainsKey(group.Key))
                continue;

            var selected = group
                .OrderBy(g =>
                {
                    var index = Array.IndexOf(priority, g.Platform);
                    return index < 0 ? int.MaxValue : index;
                })
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .First();

            _settings.PreferredDuplicateProviders[group.Key] = selected.ProviderId;
            changed = true;
        }

        if (changed)
            _settingsService.Save(_settings);
    }

    #endregion

    #region Game selection and actions

    private int GetColumns() => Math.Clamp(_settings.LibraryColumns, 4, 6);

    private void SyncToolbarPreferenceSliders()
    {
        if (LibraryColumnsSlider is null || CoverModeSlider is null || ThemeSlider is null)
            return;

        _syncingLibraryColumnsSlider = true;
        _syncingToolbarPreferenceSliders = true;
        try
        {
            LibraryColumnsSlider.Value = GetColumns();
            CoverModeSlider.Value = string.Equals(_settings.CoverMode, "Vertical", StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1;
            ThemeSlider.Value = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1;
        }
        finally
        {
            _syncingLibraryColumnsSlider = false;
            _syncingToolbarPreferenceSliders = false;
        }
    }

    private void ToolbarSlider_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not Slider slider)
            return;

        var controls = GetToolbarControls();
        var index = controls
            .Select((control, controlIndex) => new { control, controlIndex })
            .FirstOrDefault(item => ReferenceEquals(item.control, slider))
            ?.controlIndex ?? -1;

        if (index < 0)
            return;

        _controllerToolbarMode = true;
        _toolbarIndex = index;
        UpdateControllerSelection();
        ControllerStatusText.Text = GetControllerStatusText();
    }

    private void LibraryColumnsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _syncingLibraryColumnsSlider)
            return;

        var columns = Math.Clamp((int)Math.Round(e.NewValue), 4, 6);
        if (Math.Abs(LibraryColumnsSlider.Value - columns) > 0.001)
        {
            _syncingLibraryColumnsSlider = true;
            LibraryColumnsSlider.Value = columns;
            _syncingLibraryColumnsSlider = false;
        }

        if (_settings.LibraryColumns == columns)
            return;

        _settings.LibraryColumns = columns;
        _settingsService.Save(_settings);
        UpdateCoverDimensions();
        ScrollSelectedIntoView();
    }

    private void CoverModeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _syncingToolbarPreferenceSliders)
            return;

        var mode = e.NewValue < 0.5 ? "Vertical" : "Horizontal";
        if (string.Equals(_settings.CoverMode, mode, StringComparison.OrdinalIgnoreCase))
            return;

        _settings.CoverMode = mode;
        _settingsService.Save(_settings);
        ApplyCoverMode();
        ScrollSelectedIntoView();
    }

    private void ThemeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _syncingToolbarPreferenceSliders)
            return;

        var theme = e.NewValue < 0.5 ? "Light" : "Dark";
        if (string.Equals(_settings.Theme, theme, StringComparison.OrdinalIgnoreCase))
            return;

        _settings.Theme = theme;
        _settingsService.Save(_settings);
        ApplyVisualSettings();
        UpdateSelectedGamePresentation();
    }

    private void MoveSelection(int delta)
    {
        if (_visibleGames.Count == 0) return;
        var previousIndex = _selectedIndex;
        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _visibleGames.Count - 1);
        if (_selectedIndex == previousIndex) return;

        PlayNavigationSound();
        UpdateControllerSelection();

        // IsControllerSelected notifica apenas os cards afetados. Não fazemos mais
        // Items.Refresh() a cada passo, pois isso reconstruía/atualizava a lista
        // inteira e fazia a rolagem ficar para trás durante navegação contínua.
        ScrollSelectedIntoView();
    }

    private void ScrollSelectedIntoView()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _visibleGames.Count) return;

        if (GameList.ItemContainerGenerator.ContainerFromIndex(_selectedIndex) is FrameworkElement element)
        {
            // BringIntoView é imediato para acompanhar a repetição do teclado/controle.
            // A chamada antiga em DispatcherPriority.Background criava uma fila de
            // rolagens atrasadas quando várias direções chegavam rapidamente.
            element.BringIntoView();
            return;
        }

        // Durante uma troca/recarga de ItemsSource o container pode ainda não existir.
        // Nesse caso fazemos uma única tentativa em prioridade de entrada, sem deixar
        // várias operações de baixa prioridade acumularem na fila da UI.
        Dispatcher.BeginInvoke(() =>
        {
            if (_selectedIndex < 0 || _selectedIndex >= _visibleGames.Count) return;
            if (GameList.ItemContainerGenerator.ContainerFromIndex(_selectedIndex) is FrameworkElement generated)
                generated.BringIntoView();
        }, DispatcherPriority.Input);
    }

    private async Task LaunchSelectedAsync()
    {
        if (_visibleGames.Count == 0) return;
        await LaunchGameAsync(_visibleGames[_selectedIndex]);
    }

    private void OpenSelectedDetails()
    {
        if (_visibleGames.Count > 0) OpenDetails(_visibleGames[_selectedIndex]);
    }

    private void ToggleSelectedFavorite()
    {
        if (_visibleGames.Count == 0) return;
        _state.ToggleFavorite(_visibleGames[_selectedIndex], _settings);
        ApplyFilter();
    }

    private void Cover_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border border && border.DataContext is Game game)
            SelectGameWithMouse(game);
    }

    private void Cover_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is Game game)
        {
            SelectGameWithMouse(game);
            _ = LaunchGameAsync(game);
            e.Handled = true;
        }
    }

    private void Cover_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is Game game)
        {
            SelectGameWithMouse(game);
            OpenDetails(game);
            e.Handled = true;
        }
    }

    private void SelectGameWithMouse(Game game)
    {
        var index = _visibleGames.IndexOf(game);
        if (index < 0)
            return;

        // O mouse também define qual zona está ativa. Ao voltar do hero, painel
        // lateral ou barra superior para a biblioteca, o destaque precisa migrar
        // imediatamente para o card sob o ponteiro, sem exigir teclado/controle.
        var contextChanged = _controllerToolbarMode || _tvHeroMode || _sidePanelMode;

        _controllerToolbarMode = false;
        _tvHeroMode = false;
        _sidePanelMode = false;
        _suppressHeroVerticalUntilNeutral = false;
        _selectedIndex = index;

        // Se um botão do hero/painel ainda tiver foco de teclado, seu contorno pode
        // permanecer desenhado mesmo depois de o mouse retornar à biblioteca.
        // Transferir o foco para o ItemsControl mantém apenas o card como contexto ativo.
        if (contextChanged || !game.IsControllerSelected)
        {
            Keyboard.ClearFocus();
            GameList.Focus();
            Keyboard.Focus(GameList);
        }

        // Items.Refresh recriava/reaplicava containers e causava flicker. O próprio
        // Game notifica IsControllerSelected, então basta atualizar o estado.
        if (contextChanged || !game.IsControllerSelected)
            UpdateControllerSelection();
    }

    private void OpenDetails(Game game)
    {
        var manualCountBefore = _settings.ManualGames.Count;
        var window = new DetailsWindow(game, _settings, _launcher, _metadata, _sessions, _games) { Owner = this };
        window.ShowDialog();
        _settings = _settingsService.Load();

        if (_settings.ManualGames.Count != manualCountBefore)
        {
            _ = RefreshLibrary("ATUALIZANDO BIBLIOTECA...");
        }
        else
        {
            foreach (var g in _games)
                _state.Apply(g, _settings);
            ApplyCoverMode();
            ApplyFilter();
        }

        RestoreLibraryFocus();
    }

    private void RestoreLibraryFocus()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_controllerToolbarMode)
            {
                FocusToolbarControl();
                return;
            }

            if (_sidePanelMode)
            {
                FocusSidePanelControl();
                return;
            }

            GameList.Focus();
            Keyboard.Focus(GameList);
            UpdateControllerSelection();
            ScrollSelectedIntoView();
        }, DispatcherPriority.Input);
    }
    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is Game game) { _state.ToggleFavorite(game, _settings); ApplyFilter(); }
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Game game) await LaunchGameAsync(game);
    }

    private async Task LaunchGameAsync(Game game)
    {
        // O overlay bloqueia a interface visualmente, mas atalhos de teclado podem
        // chegar à janela. Evita iniciar um segundo fluxo enquanto há um em andamento.
        if (_gameLaunchCts is not null)
            return;

        if (game.IsRunning || _sessions.IsRunning(game))
        {
            game.IsRunning = true;
            StatusText.Text = $"{game.Name} já está em execução";
            ShowToast($"{game.Name} já está em execução.");
            return;
        }

        var activeGame = _sessions.GetActiveGame(_games, game);
        if (activeGame is not null)
        {
            activeGame.IsRunning = true;

            var choice = new ConcurrentLaunchWindow(activeGame, game, _settings)
            {
                Owner = this
            };

            if (choice.ShowDialog() != true || !choice.AllowConcurrentLaunch)
            {
                StatusText.Text = $"Mantendo sessão atual: {activeGame.Name}";
                return;
            }
        }

        // Nunca mantenha o LUDARYX acima do jogo durante a transição
        // de lançamento, inclusive quando o launcher está em fullscreen.
        if (_fullscreen)
            Topmost = false;

        using var launchCts = new CancellationTokenSource();
        _gameLaunchCts = launchCts;
        var token = launchCts.Token;

        try
        {
            PlayLaunchSound();
            StatusText.Text = $"Iniciando {game.Name}...";
            ShowGameLaunchOverlay(game);

            // Garante que a interface seja desenhada antes de o provider começar.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            token.ThrowIfCancellationRequested();

            SetGameLaunchProgress(8);
            GameLaunchStatusText.Text = LocalizationService.Translate("Preparando inicialização...");

            await RunLaunchCommandWithProgressAsync(game, token);
            token.ThrowIfCancellationRequested();

            _state.MarkPlayed(game, _settings);
            _sessions.TrackAfterLaunch(game, _settings);

            SetGameLaunchProgress(Math.Max(GameLaunchProgressBar.Value, 52));
            GameLaunchStatusText.Text = LocalizationService.Translate("Aguardando o jogo...");

            var gameStarted = await WaitForGameStartAsync(
                game,
                TimeSpan.FromMinutes(2),
                token);

            token.ThrowIfCancellationRequested();

            if (gameStarted)
            {
                SetGameLaunchProgress(100);
                GameLaunchStatusText.Text = LocalizationService.Translate("Jogo iniciado");
                StatusText.Text = $"Executando: {game.Name}";
                ApplyFilter();

                // Mantém 100% por um instante para tornar a conclusão perceptível.
                await Task.Delay(320, CancellationToken.None);
            }
            else
            {
                // Alguns launchers/jogos não expõem um executável detectável.
                // A barra permanece quase completa, mas não afirma 100% sem confirmação.
                SetGameLaunchProgress(95);
                StatusText.Text = $"Inicialização enviada: {game.Name}";
                await Task.Delay(250, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = LocalizationService.Translate("Inicialização interrompida");
            GameLaunchStatusText.Text = LocalizationService.Translate("Inicialização interrompida");
            CancelGameLaunchButton.IsEnabled = false;
            await Task.Delay(220, CancellationToken.None);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Falha ao iniciar o jogo";
            MessageBox.Show(
                $"Não foi possível iniciar {game.Name}.\n\n{ex.Message}",
                "LUDARYX",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            HideGameLaunchOverlay();

            if (ReferenceEquals(_gameLaunchCts, launchCts))
                _gameLaunchCts = null;
        }
    }

    private async Task RunLaunchCommandWithProgressAsync(Game game, CancellationToken token)
    {
        var launchTask = _launcher.LaunchAsync(game, token);
        var progress = Math.Max(12d, GameLaunchProgressBar.Value);
        SetGameLaunchProgress(progress);

        while (!launchTask.IsCompleted)
        {
            token.ThrowIfCancellationRequested();

            // A fase de comando cresce de forma suave até 48%; a metade restante
            // fica reservada para a detecção real do processo do jogo.
            progress = Math.Min(48, progress + 1.25);
            SetGameLaunchProgress(progress);

            await Task.WhenAny(
                launchTask,
                Task.Delay(160, token));
        }

        await launchTask;
        SetGameLaunchProgress(Math.Max(50, GameLaunchProgressBar.Value));
    }

    private async Task<bool> WaitForGameStartAsync(
        Game game,
        TimeSpan timeout,
        CancellationToken token)
    {
        var startedAt = DateTime.UtcNow;
        var deadline = startedAt + timeout;

        var securityHintShown = false;

        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();

            if (game.IsRunning || _sessions.IsRunning(game))
                return true;

            // Crescimento assintótico: sobe de 52% em direção a 95% enquanto
            // aguardamos o processo, mas nunca chega a 100% antes da detecção.
            var elapsedSeconds = Math.Max(0, (DateTime.UtcNow - startedAt).TotalSeconds);
            var waitProgress = 52 + (43 * (1 - Math.Exp(-elapsedSeconds / 10.0)));
            SetGameLaunchProgress(Math.Min(95, waitProgress));

            if (!securityHintShown && elapsedSeconds >= 15)
            {
                securityHintShown = true;
                GameLaunchSecurityHintText.Text = LocalizationService.Translate(
                    "Se o jogo não abrir e o Windows Security mostrar uma notificação, permita o aplicativo e tente novamente.");
                GameLaunchSecurityHintText.Visibility = Visibility.Visible;
            }

            await Task.Delay(250, token);
        }

        return false;
    }

    private void SetGameLaunchProgress(double value)
    {
        var normalized = Math.Clamp(value, 0, 100);
        GameLaunchProgressBar.Value = normalized;
        GameLaunchProgressText.Text = $"{(int)Math.Round(normalized)}%";
    }

    private void CancelGameLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (_gameLaunchCts is null || _gameLaunchCts.IsCancellationRequested)
            return;

        CancelGameLaunchButton.IsEnabled = false;
        GameLaunchStatusText.Text = LocalizationService.Translate("Interrompendo...");
        _gameLaunchCts.Cancel();
    }

    private void ShowGameLaunchOverlay(Game game)
    {
        GameLaunchNameText.Text = game.Name;
        GameLaunchPlatformText.Text = game.PlatformDisplay.ToUpperInvariant();
        GameLaunchStatusText.Text = LocalizationService.Translate("Preparando inicialização...");
        CancelGameLaunchButton.Content = LocalizationService.Translate("INTERROMPER");
        CancelGameLaunchButton.IsEnabled = true;
        SetGameLaunchProgress(0);
        GameLaunchSecurityHintText.Text = string.Empty;
        GameLaunchSecurityHintText.Visibility = Visibility.Collapsed;

        var verticalArtwork = game.VerticalCover;
        GameLaunchCoverImage.Source = LoadHomeArtwork(verticalArtwork);

        GameLaunchOverlay.Visibility = Visibility.Visible;
        GameLaunchOverlay.Opacity = 0;

        CancelGameLaunchButton.Focus();
        Keyboard.Focus(CancelGameLaunchButton);

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140));
        GameLaunchOverlay.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void HideGameLaunchOverlay()
    {
        if (GameLaunchOverlay.Visibility != Visibility.Visible)
            return;

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
        fadeOut.Completed += (_, _) =>
        {
            GameLaunchOverlay.Visibility = Visibility.Collapsed;
            GameLaunchOverlay.Opacity = 1;
            GameLaunchCoverImage.Source = null;
            GameLaunchSecurityHintText.Text = string.Empty;
            GameLaunchSecurityHintText.Visibility = Visibility.Collapsed;
            SetGameLaunchProgress(0);
            CancelGameLaunchButton.IsEnabled = true;
        };
        GameLaunchOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }

    private async void ShowToast(string message)
    {
        StatusText.Text = message;
        ToastText.Text = message;
        ToastBorder.Visibility = Visibility.Visible;
        ToastBorder.Opacity = 0;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120));
        ToastBorder.BeginAnimation(OpacityProperty, fadeIn);

        await Task.Delay(2600);

        if (ToastText.Text == message)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
            fadeOut.Completed += (_, _) =>
            {
                if (ToastText.Text == message)
                    ToastBorder.Visibility = Visibility.Collapsed;
            };
            ToastBorder.BeginAnimation(OpacityProperty, fadeOut);
        }

        if (StatusText.Text == message)
            StatusText.Text = $"{_games.Count} jogos na biblioteca";
    }

    #endregion
}

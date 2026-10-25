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
    #region Filtering and search

    private void LibraryFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LibraryFilterCombo.SelectedItem is not ComboBoxItem item)
            return;
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
        if (!IsLoaded || PlatformFilterCombo.SelectedItem is not ComboBoxItem item)
            return;
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
        if (!IsLoaded || SortCombo.SelectedItem is not ComboBoxItem item)
            return;
        _settings.LibrarySortMode = item.Tag?.ToString() ?? "Name";
        _settingsService.Save(_settings);
        ApplyFilter();
    }

    private void SortDirectionButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.LibrarySortDirection = NormalizeSortDirection(_settings.LibrarySortDirection) switch
        {
            "Default" => "Ascending",
            "Ascending" => "Descending",
            _ => "Default"
        };

        _settingsService.Save(_settings);
        UpdateSortDirectionButtonPresentation();
        ApplyFilter();
    }

    private static string NormalizeSortDirection(string? value) => value switch
    {
        "Ascending" => "Ascending",
        "Descending" => "Descending",
        _ => "Default"
    };

    private void UpdateSortDirectionButtonPresentation()
    {
        if (SortDirectionButton is null)
            return;

        var direction = NormalizeSortDirection(_settings.LibrarySortDirection);
        SortDirectionButton.Content = direction switch
        {
            "Ascending" => "↑",
            "Descending" => "↓",
            _ => "↕"
        };

        SortDirectionButton.ToolTip = direction switch
        {
            "Ascending" => LocalizationService.Translate("Ordem crescente"),
            "Descending" => LocalizationService.Translate("Ordem decrescente"),
            _ => LocalizationService.Translate("Ordem padrão")
        };
    }

    private static bool IsDefaultSortDescending(string sortMode, string specialFilter)
    {
        if (sortMode is "Recent" or "MostPlayed" or "PlayTime")
            return true;

        return sortMode == "Name" &&
               specialFilter.Equals("recent", StringComparison.OrdinalIgnoreCase);
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var window = new AddGameWindow { Owner = this };
        if (window.ShowDialog() != true || window.Result is null)
            return;
        _settings = _settingsService.Load();
        _settings.ManualGames.Add(window.Result);
        _settingsService.Save(_settings);
        await RefreshLibrary("ATUALIZANDO BIBLIOTECA...");
    }
    private void GenreFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            ApplyFilter();
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSearchBoxes)
            return;
        _syncingSearchBoxes = true;
        if (FullscreenSearchBox is not null)
            FullscreenSearchBox.Text = SearchBox.Text;
        _syncingSearchBoxes = false;
        if (IsLoaded)
            ApplyFilter();
    }

    private void FullscreenSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSearchBoxes)
            return;
        _syncingSearchBoxes = true;
        if (SearchBox is not null)
            SearchBox.Text = FullscreenSearchBox.Text;
        _syncingSearchBoxes = false;
        if (IsLoaded)
            ApplyFilter();
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

        SelectedGameNameText.Text = game.DisplayName;
        SelectedGameMetaText.Text = $"{game.PlatformDisplay}  •  {genres}";
        SelectedGameUsageText.Text =
            $"{game.PlayCountDisplay}  •  {game.TotalPlayTimeDisplay}\n" +
            $"{LocalizationService.Translate("Última execução")}: {game.LastPlayedDisplay}\n" +
            $"{LocalizationService.Translate("Classificação indicativa")}: {rating}";
        SelectedGameDescriptionText.Text = description;
        SelectedFavoriteButton.Content = LocalizationService.Translate(game.IsFavorite ? "★ FAVORITO" : "☆ FAVORITO");
        SelectedPlayButton.Content = LocalizationService.Translate(game.IsRunning ? "EM EXECUÇÃO" : "JOGAR");
        SelectedPlayButton.IsEnabled = !game.IsRunning;

        TvHeroTitle.Text = game.DisplayName;
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
        RefreshSelectedLocalizedAgeRatingAsync(game);
    }

    private async void RefreshSelectedLocalizedAgeRatingAsync(Game game)
    {
        _selectedAgeRatingCts?.Cancel();
        _selectedAgeRatingCts?.Dispose();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _selectedAgeRatingCts = cts;

        try
        {
            await _metadata.EnsureLocalizedAgeRatingAsync(game, _settings, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            // Reaplica somente o overlay de preferências/valores manuais depois de
            // qualquer enriquecimento remoto, preservando ratings manuais por sistema.
            _state.Apply(game, _settings);

            var selected = _visibleGames.ElementAtOrDefault(_selectedIndex);
            if (selected is null ||
                !selected.ProviderId.Equals(game.ProviderId, StringComparison.OrdinalIgnoreCase))
                return;

            var rating = AgeRatingService.GetDisplay(game.Metadata, _settings.Language);
            var localizedGenres = GenreService.DisplayMany(game.Metadata.Genres);
            var genres = string.IsNullOrWhiteSpace(localizedGenres)
                ? LocalizationService.Translate("Não informado")
                : localizedGenres;

            SelectedGameMetaText.Text = $"{game.PlatformDisplay}  •  {genres}";
            SelectedGameUsageText.Text =
                $"{game.PlayCountDisplay}  •  {game.TotalPlayTimeDisplay}\n" +
                $"{LocalizationService.Translate("Última execução")}: {game.LastPlayedDisplay}\n" +
                $"{LocalizationService.Translate("Classificação indicativa")}: {rating}";

            TvHeroMeta.Text = $"{game.PlatformDisplay}  •  {genres}  •  {rating}";
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Mantém a classificação já exibida se a fonte externa estiver indisponível.
        }
    }

    private async void RefreshSelectedLocalizedDescriptionAsync(Game game)
    {
        _selectedDescriptionCts?.Cancel();
        _selectedDescriptionCts?.Dispose();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _selectedDescriptionCts = cts;

        try
        {
            await _metadata.EnsureLocalizedDescriptionAsync(game, _settings, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            var selected = _visibleGames.ElementAtOrDefault(_selectedIndex);
            if (selected is null ||
                !selected.ProviderId.Equals(game.ProviderId, StringComparison.OrdinalIgnoreCase))
                return;

            var display = _metadata.GetDisplayDescription(game, _settings);
            game.RefreshLocalizedLabels();
            SelectedGameNameText.Text = game.DisplayName;
            TvHeroTitle.Text = game.DisplayName;
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
        var direction = NormalizeSortDirection(_settings.LibrarySortDirection);
        var hasCustomSort = !sortTag.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                            direction != "Default";
        SortFilterChip.Visibility = hasCustomSort ? Visibility.Visible : Visibility.Collapsed;
        SortFilterChip.Content = hasCustomSort
            ? direction switch
            {
                "Ascending" => $"{sortItem?.Content} • ↑  ×",
                "Descending" => $"{sortItem?.Content} • ↓  ×",
                _ => $"{sortItem?.Content}  ×"
            }
            : null;
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
        _settings.LibrarySortDirection = "Default";
        UpdateSortDirectionButtonPresentation();
        _settingsService.Save(_settings);
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
        _settings.LibrarySortDirection = "Default";
        UpdateSortDirectionButtonPresentation();
        _settingsService.Save(_settings);
        ApplyFilter();
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
            ParentalControlService.IsGameAllowed(g.Metadata, _settings.ParentalControlMode) &&
            (genre == null || g.Metadata.Genres.Any(x => x.Equals(genre, StringComparison.OrdinalIgnoreCase))) &&
            SearchNormalizationService.Matches(g, query));

        var sortMode = _settings.LibrarySortMode ?? "Name";
        var direction = NormalizeSortDirection(_settings.LibrarySortDirection);
        var descending = direction switch
        {
            "Ascending" => false,
            "Descending" => true,
            _ => IsDefaultSortDescending(sortMode, _specialFilter)
        };

        filtered = sortMode switch
        {
            "Recent" => descending
                ? filtered.OrderBy(g => !g.LastPlayedUtc.HasValue)
                          .ThenByDescending(g => g.LastPlayedUtc)
                          .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(g => !g.LastPlayedUtc.HasValue)
                          .ThenBy(g => g.LastPlayedUtc)
                          .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase),

            "MostPlayed" => descending
                ? filtered.OrderByDescending(g => g.PlayCount)
                          .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(g => g.PlayCount)
                          .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase),

            "PlayTime" => descending
                ? filtered.OrderByDescending(g => g.TotalPlayTimeSeconds)
                          .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(g => g.TotalPlayTimeSeconds)
                          .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase),

            "Platform" => descending
                ? filtered.OrderByDescending(g => g.PlatformDisplay, StringComparer.OrdinalIgnoreCase)
                          .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(g => g.PlatformDisplay, StringComparer.OrdinalIgnoreCase)
                          .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase),

            "ReleaseDate" => descending
                ? filtered.OrderBy(g => !g.Metadata.ReleaseYear.HasValue)
                          .ThenByDescending(g => g.Metadata.ReleaseYear)
                          .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(g => !g.Metadata.ReleaseYear.HasValue)
                          .ThenBy(g => g.Metadata.ReleaseYear)
                          .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase),

            _ when _specialFilter == "recent" => descending
                ? filtered.OrderByDescending(g => g.LastPlayedUtc)
                          .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(g => g.LastPlayedUtc)
                          .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase),

            _ => descending
                ? filtered.OrderByDescending(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
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
}

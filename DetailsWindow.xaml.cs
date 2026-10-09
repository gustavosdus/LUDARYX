using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class DetailsWindow : Window
{
    private readonly WindowGamepadNavigationService _controllerNavigation;
    private readonly Game _game;
    private readonly LauncherSettings _settings;
    private readonly GameStateService _state = new();
    private readonly GameLaunchService _launcher;
    private readonly MetadataService _metadata;
    private readonly GameSessionService _sessions;
    private readonly IReadOnlyList<Game> _libraryGames;
    private readonly ArtworkService _artwork = new();
    private readonly SteamGridDbService _steamGridDb = new();

    public DetailsWindow(
        Game game,
        LauncherSettings settings,
        GameLaunchService launcher,
        MetadataService metadata,
        GameSessionService sessions,
        IEnumerable<Game> libraryGames)
    {
        InitializeComponent();
        _controllerNavigation = new WindowGamepadNavigationService(this, () =>
        {
            if (IsVisible)
            {
                DialogResult = true;
                Close();
            }
        });
        _game = game;
        _settings = settings;
        LudaryxThemeService.Apply(_settings);
        _launcher = launcher;
        _metadata = metadata;
        _sessions = sessions;
        _libraryGames = libraryGames.ToList();
        LoadData();
        LocalizationService.Apply(this);
        Loaded += async (_, _) =>
        {
            LocalizationService.Apply(this);
            WindowPlacementService.FitToWorkingArea(this, Owner, margin: 16, center: true);
            try
            {
                await _metadata.EnsureLocalizedDescriptionAsync(_game, _settings);
                await _metadata.EnsureLocalizedAgeRatingAsync(_game, _settings);
                LoadData();
                LocalizationService.Apply(this);
            }
            catch
            {
                // A descrição já existente continua disponível se a fonte localizada
                // estiver temporariamente indisponível.
            }
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && ResizeMode != ResizeMode.NoResize)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void LoadData()
    {
        TitleText.Text = _game.Name;
        PlatformText.Text = $"{_game.Platform} • {_game.PlayCountDisplay} • {_game.LastPlayedDisplay}";
        var localizedGenres = GenreService.DisplayMany(_game.Metadata.Genres);
        GenreText.Text = string.IsNullOrWhiteSpace(localizedGenres) ? "Não classificado" : localizedGenres;
        CompanyText.Text = string.Join(" / ", new[] { _game.Metadata.Developer, _game.Metadata.Publisher }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(CompanyText.Text)) CompanyText.Text = "Não informado";
        ReleaseText.Text = _game.ReleaseYearDisplay.Length > 0 ? _game.ReleaseYearDisplay : LocalizationService.Translate("Não informado");
        AgeRatingText.Text = AgeRatingService.GetDisplay(_game.Metadata, _settings.Language);
        UsageText.Text = $"{_game.PlayCountDisplay} • {_game.TotalPlayTimeDisplay} • {LocalizationService.Translate("Última execução")}: {_game.LastPlayedDisplay}";
        var installPath = ResolveInstallDirectory(_game);
        InstallPathText.Text = string.IsNullOrWhiteSpace(installPath) ? LocalizationService.Translate("Não informado") : installPath;
        OpenInstallFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath);
        RunningText.Text = _game.IsRunning ? LocalizationService.Translate("JOGANDO AGORA") : string.Empty;
        DuplicateText.Text = _game.IsDuplicate ? $"Possível duplicata encontrada em: {_game.DuplicatePlatformsDisplay}" : string.Empty;
        DuplicateText.Visibility = _game.IsDuplicate ? Visibility.Visible : Visibility.Collapsed;
        var displayDescription = _metadata.GetDisplayDescription(_game, _settings);
        DescriptionText.Text = string.IsNullOrWhiteSpace(displayDescription) ? "Sem descrição." : displayDescription;
        MetadataSourceText.Text = string.IsNullOrWhiteSpace(_game.Metadata.Source)
            ? string.Empty
            : $"{LocalizationService.Translate("Fonte dos metadados")}: {_game.Metadata.Source}";
        FavoriteButton.Content = _game.IsFavorite ? $"★ {LocalizationService.Translate("FAVORITO")}" : LocalizationService.Translate("☆ FAVORITO");

        var isHidden = _settings.HiddenGameIds.Contains(
            _game.ProviderId,
            StringComparer.OrdinalIgnoreCase);
        _game.IsHidden = isHidden;
        HideButton.Content = LocalizationService.Translate(isHidden ? "DESOCULTAR" : "OCULTAR");

        SetImage(VerticalCoverImage, _game.VerticalCover);
        SetImage(HorizontalCoverImage, _game.HorizontalCover);
        ManualNameBox.Text = _game.Name;
        ManualGenresBox.Text = GenreService.DisplayManyCommaSeparated(_game.Metadata.Genres);
        ManualDeveloperBox.Text = _game.Metadata.Developer ?? "";
        ManualPublisherBox.Text = _game.Metadata.Publisher ?? "";
        ManualReleaseYearBox.Text = _game.Metadata.ReleaseYear?.ToString() ?? "";
        var preferredRatingSystem = AgeRatingService.GetPreferredSystemKey(_settings.Language);
        ManualAgeRatingBox.Text = _game.Metadata.AgeRatings.TryGetValue(preferredRatingSystem, out var manualRating)
            ? manualRating
            : "";
        ManualAgeRatingBox.ToolTip = $"{LocalizationService.Translate("Classificação indicativa")} ({AgeRatingService.GetPreferredSystemLabel(_settings.Language)})";
        ManualDescriptionBox.Text = displayDescription ?? "";
        EditManualLaunchButton.Visibility = _game.Platform == GamePlatform.Manual ? Visibility.Visible : Visibility.Collapsed;
        DuplicateManualGameButton.Visibility = _game.Platform == GamePlatform.Manual ? Visibility.Visible : Visibility.Collapsed;
        MakePrimaryDuplicateButton.Visibility = _game.IsDuplicate ? Visibility.Visible : Visibility.Collapsed;
        DuplicatePrimaryBadge.Visibility = Visibility.Collapsed;

        if (_game.IsDuplicate &&
            _settings.PreferredDuplicateProviders.TryGetValue(_game.CanonicalGameId, out var preferred) &&
            preferred.Equals(_game.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            MakePrimaryDuplicateButton.Content = LocalizationService.Translate("VERSÃO PRINCIPAL");
            MakePrimaryDuplicateButton.IsEnabled = false;
            DuplicatePrimaryBadgeText.Text = LocalizationService.Translate("VERSÃO PRINCIPAL DA DUPLICATA");
            DuplicatePrimaryBadge.Visibility = Visibility.Visible;
        }
        else
        {
            MakePrimaryDuplicateButton.Content = LocalizationService.Translate("TORNAR ESTA VERSÃO PRINCIPAL");
            MakePrimaryDuplicateButton.IsEnabled = true;

            if (_game.IsDuplicate)
            {
                DuplicatePrimaryBadgeText.Text = LocalizationService.Translate("VERSÃO SECUNDÁRIA");
                DuplicatePrimaryBadge.Visibility = Visibility.Visible;
            }
        }
    }

    private static void SetImage(System.Windows.Controls.Image image, string path)
    {
        try
        {
            image.Source = string.IsNullOrWhiteSpace(path)
                ? null
                : new BitmapImage(new Uri(path, UriKind.Absolute));
        }
        catch
        {
            image.Source = null;
        }
    }

    private void Favorite_Click(object sender, RoutedEventArgs e) { _state.ToggleFavorite(_game, _settings); LoadData(); }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        var isHidden = _settings.HiddenGameIds.Contains(
            _game.ProviderId,
            StringComparer.OrdinalIgnoreCase);

        if (isHidden)
        {
            _settings.HiddenGameIds.RemoveAll(x =>
                x.Equals(_game.ProviderId, StringComparison.OrdinalIgnoreCase));
            _game.IsHidden = false;
        }
        else
        {
            AddId(_settings.HiddenGameIds);
            _game.IsHidden = true;
        }

        _settingsServiceSave();
        DialogResult = true;
        Close();
    }

    private void Exclude_Click(object sender, RoutedEventArgs e) { AddId(_settings.ExcludedGameIds); _settings.HiddenGameIds.RemoveAll(x => x.Equals(_game.ProviderId, StringComparison.OrdinalIgnoreCase)); _settingsServiceSave(); DialogResult = true; Close(); }
    private void AddId(List<string> list) { if (!list.Contains(_game.ProviderId, StringComparer.OrdinalIgnoreCase)) list.Add(_game.ProviderId); }
    private void _settingsServiceSave() => new JsonSettingsService().Save(_settings);

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_game.IsRunning || _sessions.IsRunning(_game))
            {
                _game.IsRunning = true;
                LoadData();
                MessageBox.Show(this, $"{_game.Name} já está em execução.", "LUDARYX",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var activeGame = _sessions.GetActiveGame(_libraryGames, _game);
            if (activeGame is not null)
            {
                activeGame.IsRunning = true;
                var choice = new ConcurrentLaunchWindow(activeGame, _game, _settings)
                {
                    Owner = this
                };

                if (choice.ShowDialog() != true || !choice.AllowConcurrentLaunch)
                    return;
            }

            await _launcher.LaunchAsync(_game);
            _state.MarkPlayed(_game, _settings);
            _sessions.TrackAfterLaunch(_game, _settings);
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erro ao iniciar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? ResolveInstallDirectory(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.InstallPath))
        {
            try
            {
                var full = Path.GetFullPath(game.InstallPath);
                if (Directory.Exists(full))
                    return full;
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(game.Executable))
        {
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(game.Executable));
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    return directory;
            }
            catch
            {
            }
        }

        return null;
    }

    private void OpenInstallFolder_Click(object sender, RoutedEventArgs e)
    {
        var directory = ResolveInstallDirectory(_game);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            MessageBox.Show(this, LocalizationService.Translate("A pasta do jogo não foi encontrada."),
                "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{directory}\"",
            UseShellExecute = true
        });
    }

    private async void RefreshMetadata_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            await _metadata.EnrichGameAsync(_game, _settings, forceArtworkRefresh: true);

            // A atualização manual deve reconsultar a classificação regional mesmo
            // quando uma tentativa anterior falhou e deixou cache local sem resultado.
            await _metadata.EnsureLocalizedAgeRatingAsync(
                _game,
                _settings,
                forceRefresh: true);

            if (DuplicateMetadataService.Synchronize(_libraryGames, _settings))
                new JsonSettingsService().Save(_settings);

            _metadata.SaveCacheSnapshot();
            LoadData();
            LocalizationService.Apply(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Não foi possível atualizar os metadados deste jogo.\n\n{ex.Message}",
                "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void DuplicateManualGame_Click(object sender, RoutedEventArgs e)
    {
        if (_game.Platform != GamePlatform.Manual)
            return;

        var source = _settings.ManualGames.FirstOrDefault(x =>
            x.Id.Equals(_game.Id, StringComparison.OrdinalIgnoreCase));
        if (source is null)
            return;

        var copy = new ManualGameDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = source.Name + " (Cópia)",
            Executable = source.Executable,
            Arguments = source.Arguments,
            LaunchUri = source.LaunchUri,
            WorkingDirectory = source.WorkingDirectory,
            RunAsAdministrator = source.RunAsAdministrator,
            IconPath = source.IconPath,
            CoverPath = source.CoverPath
        };

        var sourceProfiles = source.LaunchProfiles ?? new();
        foreach (var profile in sourceProfiles)
        {
            var cloned = new ManualLaunchProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = profile.Name,
                Executable = profile.Executable,
                Arguments = profile.Arguments,
                LaunchUri = profile.LaunchUri,
                WorkingDirectory = profile.WorkingDirectory,
                RunAsAdministrator = profile.RunAsAdministrator
            };
            copy.LaunchProfiles.Add(cloned);

            if (profile.Id.Equals(source.PreferredLaunchProfileId, StringComparison.OrdinalIgnoreCase))
                copy.PreferredLaunchProfileId = cloned.Id;
        }

        if (copy.LaunchProfiles.Count == 0)
        {
            var profile = new ManualLaunchProfile
            {
                Name = "Padrão",
                Executable = copy.Executable,
                Arguments = copy.Arguments,
                LaunchUri = copy.LaunchUri,
                WorkingDirectory = copy.WorkingDirectory,
                RunAsAdministrator = copy.RunAsAdministrator
            };
            copy.LaunchProfiles.Add(profile);
            copy.PreferredLaunchProfileId = profile.Id;
        }

        _settings.ManualGames.Add(copy);

        // A cópia mantém somente overrides realmente manuais da entrada original.
        // Metadados automáticos não são mais transformados em ManualMetadata, pois isso
        // congelava descrição/gêneros/empresas e impedia localização futura.
        _settings.ManualMetadata.TryGetValue(_game.ProviderId, out var sourceMetadata);
        var currentMetadata = _game.Metadata;

        _settings.ManualMetadata[$"Manual:{copy.Id}"] = new ManualGameMetadata
        {
            Name = sourceMetadata?.Name ?? _game.Name,
            Description = sourceMetadata?.Description,
            Genres = sourceMetadata?.Genres?.ToList() ?? new(),
            Developer = sourceMetadata?.Developer,
            Publisher = sourceMetadata?.Publisher,
            ReleaseYear = sourceMetadata?.ReleaseYear,
            AgeRatings = new Dictionary<string, string>(
                sourceMetadata?.AgeRatings ?? new(),
                StringComparer.OrdinalIgnoreCase),
            HorizontalCoverUrl = sourceMetadata?.HorizontalCoverUrl
                ?? currentMetadata.CustomHorizontalCoverLocalPath,
            VerticalCoverUrl = sourceMetadata?.VerticalCoverUrl
                ?? currentMetadata.CustomVerticalCoverLocalPath,
            DisableAutomaticSteamGridDbVertical = sourceMetadata?.DisableAutomaticSteamGridDbVertical ?? false,
            DisableAutomaticSteamGridDbHorizontal = sourceMetadata?.DisableAutomaticSteamGridDbHorizontal ?? false
        };

        if (_settings.SteamGridDbGameIds.TryGetValue(_game.ProviderId, out var sourceSteamGridId))
            _settings.SteamGridDbGameIds[$"Manual:{copy.Id}"] = sourceSteamGridId;

        _settingsServiceSave();
        MessageBox.Show(this,
            LocalizationService.Translate("Entrada manual duplicada. Atualize a biblioteca para exibi-la."),
            "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void EditManualLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (_game.Platform != GamePlatform.Manual)
            return;

        var definition = _settings.ManualGames.FirstOrDefault(x =>
            x.Id.Equals(_game.Id, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
        {
            MessageBox.Show("A definição deste jogo manual não foi encontrada.", "LUDARYX",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(definition.Executable) && !File.Exists(definition.Executable))
        {
            MessageBox.Show(this,
                $"O executável configurado para este jogo não existe mais:\n\n{definition.Executable}\n\nVocê pode selecionar um novo executável na próxima tela.",
                "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        var window = new AddGameWindow(definition) { Owner = this };
        if (window.ShowDialog() != true || window.Result is null)
            return;

        var index = _settings.ManualGames.FindIndex(x =>
            x.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            _settings.ManualGames[index] = window.Result;

        _game.Name = window.Result.Name;
        _game.Executable = window.Result.Executable;
        _game.LaunchArguments = window.Result.Arguments;
        _game.LaunchUri = window.Result.LaunchUri;
        _game.InstallPath = window.Result.WorkingDirectory;
        _settingsServiceSave();
        LoadData();
    }

    private void MakePrimaryDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (!_game.IsDuplicate || string.IsNullOrWhiteSpace(_game.CanonicalGameId))
            return;

        _settings.PreferredDuplicateProviders[_game.CanonicalGameId] = _game.ProviderId;
        _settingsServiceSave();
        LoadData();
    }

    private void ChooseVerticalArtwork_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_artwork.ChooseAndSave(_game, true)) { PersistArtwork(); LoadData(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Imagem inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ChooseHorizontalArtwork_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_artwork.ChooseAndSave(_game, false)) { PersistArtwork(); LoadData(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Imagem inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RestoreVerticalArtwork_Click(object sender, RoutedEventArgs e)
    {
        _artwork.Restore(_game, true);
        SetAutomaticSteamGridDbDisabled(vertical: true, disabled: false);
        PersistArtwork();
        LoadData();
    }

    private void RestoreHorizontalArtwork_Click(object sender, RoutedEventArgs e)
    {
        _artwork.Restore(_game, false);
        SetAutomaticSteamGridDbDisabled(vertical: false, disabled: false);
        PersistArtwork();
        LoadData();
    }


    private async void DownloadSteamGridVertical_Click(object sender, RoutedEventArgs e)
    {
        await DownloadSteamGridArtworkAsync(true);
    }

    private async void DownloadSteamGridHorizontal_Click(object sender, RoutedEventArgs e)
    {
        await DownloadSteamGridArtworkAsync(false);
    }

    private async void ChooseSteamGridVertical_Click(object sender, RoutedEventArgs e)
    {
        await ChooseSteamGridArtworkAsync(true);
    }

    private async void ChooseSteamGridHorizontal_Click(object sender, RoutedEventArgs e)
    {
        await ChooseSteamGridArtworkAsync(false);
    }

    private async void CorrectSteamGridGame_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var apiKey = _settings.SteamGridDbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show(
                    "Configure a API Key do SteamGridDB em Configurações → SteamGridDB e tente novamente.",
                    "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var picker = new SteamGridDbGamePickerWindow(_game.Name, apiKey, _steamGridDb) { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedGame is null) return;

            _settings.SteamGridDbGameIds[_game.ProviderId] = picker.SelectedGame.Id;
            new JsonSettingsService().Save(_settings);

            MessageBox.Show(
                $"Jogo associado ao SteamGridDB:\n\n{picker.SelectedGame.Name}\nID: {picker.SelectedGame.Id}\n\nAs próximas buscas automáticas usarão esta associação.",
                "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DownloadSteamGridArtworkAsync(bool vertical)
    {
        try
        {
            var apiKey = _settings.SteamGridDbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show(
                    "Configure a API Key do SteamGridDB em Configurações → SteamGridDB e tente novamente.",
                    "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            var gameId = await ResolveSteamGridGameIdAsync(apiKey);
            if (gameId is null)
            {
                MessageBox.Show("O SteamGridDB não encontrou um jogo correspondente. Use CORRIGIR JOGO DO STEAMGRIDDB para escolher manualmente.", "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = await _steamGridDb.DownloadBestArtworkAsync(_game, apiKey, vertical, gameId.Value);
            SetAutomaticSteamGridDbDisabled(vertical, disabled: false);
            PersistArtwork();
            LoadData();
            MessageBox.Show(
                $"Arte baixada com sucesso do SteamGridDB.\n\nTamanho: {result.Width}×{result.Height}\nPontuação: {result.Score}\nEstilo: {result.Style ?? "não informado"}\nAutor: {result.Author ?? "não informado"}",
                "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async Task ChooseSteamGridArtworkAsync(bool vertical)
    {
        try
        {
            var apiKey = _settings.SteamGridDbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show(
                    "Configure a API Key do SteamGridDB em Configurações → SteamGridDB e tente novamente.",
                    "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            var gameId = await ResolveSteamGridGameIdAsync(apiKey);
            Mouse.OverrideCursor = null;
            if (gameId is null)
            {
                MessageBox.Show("O SteamGridDB não encontrou um jogo correspondente. Use CORRIGIR JOGO DO STEAMGRIDDB para escolher manualmente.", "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var picker = new SteamGridDbArtworkPickerWindow(_game, _settings, _steamGridDb, vertical, gameId.Value) { Owner = this };
            if (picker.ShowDialog() == true && picker.ArtworkChanged)
            {
                SetAutomaticSteamGridDbDisabled(vertical, disabled: false);
                PersistArtwork();
                LoadData();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async Task<int?> ResolveSteamGridGameIdAsync(string apiKey)
    {
        if (_settings.SteamGridDbGameIds.TryGetValue(_game.ProviderId, out var mappedId))
            return mappedId;

        return await _steamGridDb.ResolveGameIdAsync(_game, apiKey);
    }

    private void PersistArtwork()
    {
        if (!_settings.ManualMetadata.TryGetValue(_game.ProviderId, out var manual))
            manual = new ManualGameMetadata();
        manual.HorizontalCoverUrl = _game.Metadata.CustomHorizontalCoverLocalPath;
        manual.VerticalCoverUrl = _game.Metadata.CustomVerticalCoverLocalPath;
        _settings.ManualMetadata[_game.ProviderId] = manual;
        new JsonSettingsService().Save(_settings);
    }

    private void SetAutomaticSteamGridDbDisabled(bool vertical, bool disabled)
    {
        if (!_settings.ManualMetadata.TryGetValue(_game.ProviderId, out var manual))
            manual = new ManualGameMetadata();

        if (vertical) manual.DisableAutomaticSteamGridDbVertical = disabled;
        else manual.DisableAutomaticSteamGridDbHorizontal = disabled;
        _settings.ManualMetadata[_game.ProviderId] = manual;
    }

    private void ClearAutomaticSteamGridDbArtwork(bool vertical)
    {
        if (vertical)
        {
            if (IsSteamGridDbPath(_game.Metadata.VerticalCoverLocalPath))
            {
                if (string.Equals(_game.Metadata.CoverLocalPath, _game.Metadata.VerticalCoverLocalPath, StringComparison.OrdinalIgnoreCase))
                    _game.Metadata.CoverLocalPath = null;
                _game.Metadata.VerticalCoverLocalPath = null;
            }
            if (IsSteamGridDbUrl(_game.Metadata.VerticalCoverUrl)) _game.Metadata.VerticalCoverUrl = null;
        }
        else
        {
            if (IsSteamGridDbPath(_game.Metadata.HorizontalCoverLocalPath))
            {
                if (string.Equals(_game.Metadata.CoverLocalPath, _game.Metadata.HorizontalCoverLocalPath, StringComparison.OrdinalIgnoreCase))
                    _game.Metadata.CoverLocalPath = null;
                _game.Metadata.HorizontalCoverLocalPath = null;
            }
            if (IsSteamGridDbUrl(_game.Metadata.HorizontalCoverUrl)) _game.Metadata.HorizontalCoverUrl = null;
        }
    }

    private static bool IsSteamGridDbPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.GetFileName(path).Contains("steamgriddb", StringComparison.OrdinalIgnoreCase);

    private static bool IsSteamGridDbUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Host.Contains("steamgriddb", StringComparison.OrdinalIgnoreCase);


    private void SaveMetadata_Click(object sender, RoutedEventArgs e)
    {
        int? releaseYear = null;
        if (int.TryParse(ManualReleaseYearBox.Text.Trim(), out var parsedYear) &&
            parsedYear >= 1970 && parsedYear <= DateTime.UtcNow.Year + 2)
        {
            releaseYear = parsedYear;
        }

        var ageRatings = _settings.ManualMetadata.TryGetValue(_game.ProviderId, out var previousManual)
            ? new Dictionary<string, string>(previousManual.AgeRatings ?? new(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var ratingSystem = AgeRatingService.GetPreferredSystemKey(_settings.Language);
        var ratingValue = ManualAgeRatingBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ratingValue))
            ageRatings.Remove(ratingSystem);
        else
            ageRatings[ratingSystem] = ratingValue;

        var manual = new ManualGameMetadata
        {
            Name = ManualNameBox.Text,
            Genres = GenreService.NormalizeMany(ManualGenresBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList(),
            Developer = ManualDeveloperBox.Text,
            Publisher = ManualPublisherBox.Text,
            ReleaseYear = releaseYear,
            AgeRatings = ageRatings,
            Description = ManualDescriptionBox.Text
        };
        _state.SaveManualMetadata(_game, _settings, manual);
        LoadData();
    }

    private void Close_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
}

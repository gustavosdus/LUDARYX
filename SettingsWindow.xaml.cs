using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class SettingsWindow : Window
{
    private readonly WindowGamepadNavigationService _controllerNavigation;
    private readonly JsonSettingsService _settingsService = new();
    private readonly LauncherSettings _settings;
    private readonly ObservableCollection<GameVisibilityItem> _items = new();
    private readonly List<Game> _games;

    public SettingsWindow(IEnumerable<Game> games, LauncherSettings settings)
    {
        InitializeComponent();
        _controllerNavigation = new WindowGamepadNavigationService(this, () =>
        {
            if (IsVisible)
            {
                DialogResult = false;
                Close();
            }
        });
        _games = games.ToList();
        _settings = Clone(settings);
        ShowHiddenCheck.IsChecked = _settings.ShowHiddenApps;
        HideGameDetailsPanelsCheck.IsChecked = _settings.HideGameDetailsPanels;
        AutoStartClientsCheck.IsChecked = _settings.StartClientsAutomatically;
        KeepLauncherOpenCheck.IsChecked = _settings.KeepLauncherOpen;
        StartWithWindowsCheck.IsChecked = StartupService.IsEnabled();
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        StartFullscreenCheck.IsChecked = _settings.StartFullscreen;
        CoverModeCombo.SelectedIndex = _settings.CoverMode switch { "Horizontal" => 1, "Vertical" => 2, _ => 0 };
        NeonLineColorCombo.SelectedIndex = _settings.NeonLineColor == "LightBlue" ? 1 : 0;
        ThemeCombo.SelectedIndex = _settings.Theme == "Light" ? 1 : 0;
        SelectLanguage(_settings.Language);
        SteamGridDbApiKeyBox.Password = _settings.SteamGridDbApiKey ?? "";
        CheckForUpdatesOnStartupCheck.IsChecked = _settings.CheckForUpdatesOnStartup;
        AutoHideDuplicateSecondaryCheck.IsChecked = _settings.AutoHideDuplicateSecondary || _settings.ShowOnlyPrimaryDuplicates;
        DuplicatePriorityBox.Text = string.Join(", ", _settings.DuplicatePlatformPriority);
        ShortcutSearchBox.Text = _settings.Shortcuts.FocusSearch;
        ShortcutFullscreenBox.Text = _settings.Shortcuts.ToggleFullscreen;
        ShortcutRefreshBox.Text = _settings.Shortcuts.RefreshLibrary;
        ShortcutSettingsBox.Text = _settings.Shortcuts.OpenSettings;
        AutoCleanupCacheCheck.IsChecked = _settings.AutoCleanupCache;
        MaxCacheSizeBox.Text = _settings.MaxCacheSizeMb.ToString();
        RefreshCacheSize();
        IntegrationStatusList.ItemsSource = IntegrationStatusService.GetStatuses(_settings);

        LocalizationService.SetLanguage(_settings.Language);
        LocalizationService.Apply(this);

        foreach (var game in _games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            _items.Add(new GameVisibilityItem(game, !_settings.HiddenGameIds.Contains(game.ProviderId, StringComparer.OrdinalIgnoreCase) && !_settings.ExcludedGameIds.Contains(game.ProviderId, StringComparer.OrdinalIgnoreCase)));
        GamesList.ItemsSource = _items;
        Loaded += (_, _) =>
        {
            LocalizationService.Apply(this);

            // CenterOwner pode posicionar parte da janela fora da área útil quando
            // o proprietário está maximizado ou quando a tela é menor que 820x780.
            // Limita o tamanho e centraliza no monitor do LUDARYX, acima da taskbar.
            WindowPlacementService.FitToWorkingArea(this, Owner, margin: 16, center: true);
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
        DialogResult = false;
        Close();
    }

    private void SettingsSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SettingsSearchBox.Text.Trim();
        SetSectionVisibility(LibrarySection, query, "biblioteca ocultos painel lateral hero");
        SetSectionVisibility(ExecutionSection, query, "execução iniciar clientes windows tela cheia");
        SetSectionVisibility(AppearanceSection, query, "aparência capas linha neon tema idioma");
        SetSectionVisibility(SteamGridDbSection, query, "steamgriddb api capas arte");
        SetSectionVisibility(UpdatesSection, query, "atualizações github verificar");
        SetSectionVisibility(DuplicatesSection, query, "duplicatas versão principal prioridade plataforma");
        SetSectionVisibility(CacheSection, query, "cache backup diagnóstico pasta dados");
        SetSectionVisibility(ShortcutsSection, query, "atalhos teclado pesquisar tela cheia atualizar configurações");
        SetSectionVisibility(IntegrationsSection, query, "status integrações clientes steam epic gog xbox ea ubisoft battle riot");
        SetSectionVisibility(ManageItemsSection, query, "gerenciar itens jogos ocultar restaurar excluir");
    }

    private static void SetSectionVisibility(FrameworkElement section, string query, string searchableText)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            section.Visibility = Visibility.Visible;
            return;
        }

        var normalizedQuery = SearchNormalizationService.Normalize(query);
        var normalizedText = SearchNormalizationService.Normalize(searchableText);
        section.Visibility = normalizedText.Contains(normalizedQuery, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ManageItemsSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_items);
        var query = SearchNormalizationService.Normalize(ManageItemsSearchBox.Text);

        view.Filter = item =>
        {
            if (item is not GameVisibilityItem gameItem || string.IsNullOrWhiteSpace(query))
                return true;

            return SearchNormalizationService.Normalize(gameItem.Name).Contains(query, StringComparison.Ordinal) ||
                   SearchNormalizationService.Normalize(gameItem.ProviderId).Contains(query, StringComparison.Ordinal);
        };

        view.Refresh();
    }

    private void SelectLanguage(string? languageCode)
    {
        foreach (var item in LanguageCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), languageCode, StringComparison.OrdinalIgnoreCase))
            {
                LanguageCombo.SelectedItem = item;
                return;
            }
        }

        LanguageCombo.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowHiddenApps = ShowHiddenCheck.IsChecked == true;
        _settings.HideGameDetailsPanels = HideGameDetailsPanelsCheck.IsChecked == true;
        _settings.StartClientsAutomatically = AutoStartClientsCheck.IsChecked == true;
        _settings.KeepLauncherOpen = KeepLauncherOpenCheck.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.StartFullscreen = StartFullscreenCheck.IsChecked == true;
        try
        {
            StartupService.SetEnabled(_settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Não foi possível alterar a inicialização automática do Windows.\n\n{ex.Message}",
                "LUDARYX",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }
        _settings.CoverMode = (CoverModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "Auto";
        _settings.NeonLineColor = (NeonLineColorCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() == "LightBlue" ? "LightBlue" : "Red";
        _settings.Theme = (ThemeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() == "Light" ? "Light" : "Dark";
        _settings.Language = (LanguageCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? LocalizationService.PortugueseBrazil;
        _settings.SteamGridDbApiKey = string.IsNullOrWhiteSpace(SteamGridDbApiKeyBox.Password) ? null : SteamGridDbApiKeyBox.Password.Trim();
        _settings.CheckForUpdatesOnStartup = CheckForUpdatesOnStartupCheck.IsChecked == true;
        _settings.AutoHideDuplicateSecondary = AutoHideDuplicateSecondaryCheck.IsChecked == true;
        // Campo legado mantido apenas para compatibilidade de leitura de settings antigos.
        _settings.ShowOnlyPrimaryDuplicates = false;
        _settings.DuplicatePlatformPriority = DuplicatePriorityBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var shortcutValues = new[]
        {
            ShortcutSearchBox.Text.Trim(),
            ShortcutFullscreenBox.Text.Trim(),
            ShortcutRefreshBox.Text.Trim(),
            ShortcutSettingsBox.Text.Trim()
        };
        foreach (var shortcut in shortcutValues)
        {
            if (!IsValidShortcut(shortcut))
            {
                MessageBox.Show(this,
                    $"Atalho inválido: {shortcut}\n\nUse formatos como Ctrl+F, F11, F5 ou Ctrl+,.",
                    "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _settings.Shortcuts = new ShortcutSettings
        {
            FocusSearch = shortcutValues[0],
            ToggleFullscreen = shortcutValues[1],
            RefreshLibrary = shortcutValues[2],
            OpenSettings = shortcutValues[3]
        };

        _settings.AutoCleanupCache = AutoCleanupCacheCheck.IsChecked == true;
        _settings.MaxCacheSizeMb = int.TryParse(MaxCacheSizeBox.Text, out var cacheLimit)
            ? Math.Clamp(cacheLimit, 128, 16384)
            : 1024;
        // Atualiza apenas os itens que estão realmente presentes no gerenciador.
        // IDs ocultos de jogos que não foram descobertos nesta sessão são preservados,
        // evitando que uma atualização/recarga temporária faça itens antigos reaparecerem.
        var managedIds = _items
            .Select(x => x.Game.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var preservedHiddenIds = _settings.HiddenGameIds
            .Where(id => !managedIds.Contains(id))
            .ToList();

        var hiddenManagedIds = _items
            .Where(x => !x.IsVisible && !x.Game.IsExcluded)
            .Select(x => x.Game.ProviderId);

        _settings.HiddenGameIds = preservedHiddenIds
            .Concat(hiddenManagedIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _settingsService.Save(_settings);
        DialogResult = true;
        Close();
    }

    private void RestoreExcluded_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button b || b.Tag is not GameVisibilityItem item) return;
        _settings.ExcludedGameIds.RemoveAll(x => x.Equals(item.Game.ProviderId, StringComparison.OrdinalIgnoreCase));
        item.Game.IsExcluded = false;
        item.IsVisible = true;
        _settingsService.Save(_settings);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var window = new AboutWindow { Owner = this };
        window.ShowDialog();
    }

    private async void CheckUpdatesNow_Click(object sender, RoutedEventArgs e)
    {
        CheckForUpdatesOnStartupCheck.IsEnabled = false;
        CheckUpdatesNowButton.IsEnabled = false;
        UpdateStatusText.Text = "Verificando atualizações no GitHub...";

        try
        {
            _settings.CheckForUpdatesOnStartup = CheckForUpdatesOnStartupCheck.IsChecked == true;
            _settingsService.Save(_settings);

            var result = await GitHubUpdateService.CheckForUpdatesAsync();
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settingsService.Save(_settings);

            if (!result.IsUpdateAvailable)
            {
                UpdateStatusText.Text = $"Você já está na versão mais recente ({result.CurrentVersionDisplay}).";
                MessageBox.Show(
                    this,
                    $"Nenhuma atualização foi encontrada.\n\nVersão atual: {result.CurrentVersionDisplay}",
                    "LUDARYX",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            UpdateStatusText.Text = $"Nova versão encontrada: {result.LatestVersionDisplay}";
            var installNow = await GitHubUpdateService.PromptAndDownloadUpdateAsync(this, result, CancellationToken.None);
            if (installNow)
            {
                DialogResult = true;
                System.Windows.Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "Não foi possível verificar atualizações agora.";
            DiagnosticLogService.LogException("Could not check for updates", ex);
            MessageBox.Show(
                this,
                $"Não foi possível verificar atualizações agora.\n\n{ex.Message}",
                "LUDARYX",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            CheckForUpdatesOnStartupCheck.IsEnabled = true;
            CheckUpdatesNowButton.IsEnabled = true;
        }
    }

    private static bool IsValidShortcut(string value) =>
        ShortcutGestureService.IsValid(value);

    private void RefreshIntegrationStatus_Click(object sender, RoutedEventArgs e)
    {
        IntegrationStatusList.ItemsSource = IntegrationStatusService.GetStatuses(_settings);
    }

    private void RefreshCacheSize()
    {
        var mb = CacheMaintenanceService.GetCacheSizeBytes() / 1024d / 1024d;
        CacheSizeText.Text = $"Uso atual: {mb:0.0} MB";
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        CacheMaintenanceService.ClearDownloadCache();
        RefreshCacheSize();
        MessageBox.Show(this, "O cache de capas baixadas foi limpo. Artes personalizadas foram preservadas.",
            "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppDataService.OpenRootDirectory();
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException("Could not open LUDARYX data folder", ex);
            MessageBox.Show(this, ex.Message, "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OpenIntegrationClient_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.Tag is not IntegrationStatusItem item ||
            item.Platform is not GamePlatform platform)
            return;

        try
        {
            button.IsEnabled = false;
            var provider = new LibraryService().GetProvider(platform)
                ?? throw new InvalidOperationException("A integração selecionada não foi encontrada.");

            if (!provider.IsInstalled())
                throw new InvalidOperationException($"{provider.Name} não foi encontrado neste computador.");

            await provider.StartClientAsync();
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException($"Could not open integration client: {item.Name}", ex);
            MessageBox.Show(this,
                $"Não foi possível abrir {item.Name}.\n\n{ex.Message}",
                "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var optionsWindow = new BackupOptionsWindow { Owner = this };
        if (optionsWindow.ShowDialog() != true)
            return;

        var selection = optionsWindow.Selection;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Backup do LUDARYX (*.zip)|*.zip",
            FileName = $"LUDARYX-backup-{DateTime.Now:yyyy-MM-dd}.zip"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            BackupService.Export(dialog.FileName, selection);
            MessageBox.Show(this, "Backup exportado com sucesso.", "LUDARYX",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível exportar o backup.\n\n{ex.Message}", "LUDARYX",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Backup do LUDARYX (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true) return;

        var optionsWindow = new BackupOptionsWindow(importMode: true, sourceZip: dialog.FileName) { Owner = this };
        if (optionsWindow.ShowDialog() != true)
            return;

        var selection = optionsWindow.Selection;
        if (MessageBox.Show(this,
                "Somente as categorias selecionadas serão restauradas e poderão substituir os dados locais correspondentes. Continuar?",
                "LUDARYX", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            BackupService.Import(dialog.FileName, selection);
            MessageBox.Show(this,
                "Backup restaurado com sucesso para as categorias selecionadas.\n\nReinicie o LUDARYX para aplicar os dados restaurados. O cache automático de capas continua sendo recriado quando necessário.",
                "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível importar o backup.\n\n{ex.Message}", "LUDARYX",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(DiagnosticLogService.BuildDiagnosticReport());
        MessageBox.Show(this, "Relatório de diagnóstico sanitizado copiado para a área de transferência.",
            "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private static LauncherSettings Clone(LauncherSettings source) => new()
    {
        StartClientsAutomatically = source.StartClientsAutomatically,
        KeepLauncherOpen = source.KeepLauncherOpen,
        StartWithWindows = source.StartWithWindows,
        StartFullscreen = source.StartFullscreen,
        ShowHiddenApps = source.ShowHiddenApps,
        HideGameDetailsPanels = source.HideGameDetailsPanels,
        HiddenGameIds = source.HiddenGameIds.ToList(),
        ExcludedGameIds = source.ExcludedGameIds.ToList(),
        FavoriteGameIds = source.FavoriteGameIds.ToList(),
        LastPlayedUtc = new Dictionary<string, DateTime>(source.LastPlayedUtc, StringComparer.OrdinalIgnoreCase),
        PlayCounts = new Dictionary<string, int>(source.PlayCounts, StringComparer.OrdinalIgnoreCase),
        TotalPlayTimeSeconds = new Dictionary<string, long>(source.TotalPlayTimeSeconds, StringComparer.OrdinalIgnoreCase),
        MonthlyPlayTimeSeconds = new Dictionary<string, long>(source.MonthlyPlayTimeSeconds, StringComparer.OrdinalIgnoreCase),
        ManualMetadata = source.ManualMetadata.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
        CoverMode = source.CoverMode,
        NeonLineColor = source.NeonLineColor,
        Theme = source.Theme,
        Language = source.Language,
        CheckForUpdatesOnStartup = source.CheckForUpdatesOnStartup,
        LastUpdateCheckUtc = source.LastUpdateCheckUtc,
        SkippedUpdateVersion = source.SkippedUpdateVersion,
        AutoCleanupCache = source.AutoCleanupCache,
        MaxCacheSizeMb = source.MaxCacheSizeMb,
        AutoHideDuplicateSecondary = source.AutoHideDuplicateSecondary || source.ShowOnlyPrimaryDuplicates,
        ShowOnlyPrimaryDuplicates = false,
        PreferredDuplicateProviders = new Dictionary<string, string>(source.PreferredDuplicateProviders, StringComparer.OrdinalIgnoreCase),
        DuplicatePlatformPriority = source.DuplicatePlatformPriority.ToList(),
        LibrarySortMode = source.LibrarySortMode,
        Shortcuts = new ShortcutSettings
        {
            FocusSearch = source.Shortcuts.FocusSearch,
            ToggleFullscreen = source.Shortcuts.ToggleFullscreen,
            RefreshLibrary = source.Shortcuts.RefreshLibrary,
            OpenSettings = source.Shortcuts.OpenSettings
        },
        EnrichMetadataAutomatically = source.EnrichMetadataAutomatically,
        UseSteamStoreMetadata = source.UseSteamStoreMetadata,
        UseIgdbMetadata = source.UseIgdbMetadata,
        IgdbClientId = source.IgdbClientId,
        IgdbClientSecret = source.IgdbClientSecret,
        SteamGridDbApiKey = source.SteamGridDbApiKey,
        ProtectedIgdbClientId = source.ProtectedIgdbClientId,
        ProtectedIgdbClientSecret = source.ProtectedIgdbClientSecret,
        ProtectedSteamGridDbApiKey = source.ProtectedSteamGridDbApiKey,
        ManualGames = source.ManualGames.Select(x => new ManualGameDefinition
        {
            Id = x.Id,
            Name = x.Name,
            Executable = x.Executable,
            Arguments = x.Arguments,
            LaunchUri = x.LaunchUri,
            WorkingDirectory = x.WorkingDirectory,
            RunAsAdministrator = x.RunAsAdministrator,
            IconPath = x.IconPath,
            CoverPath = x.CoverPath,
            PreferredLaunchProfileId = x.PreferredLaunchProfileId,
            LaunchProfiles = (x.LaunchProfiles ?? new()).Select(profile => new ManualLaunchProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                Executable = profile.Executable,
                Arguments = profile.Arguments,
                LaunchUri = profile.LaunchUri,
                WorkingDirectory = profile.WorkingDirectory,
                RunAsAdministrator = profile.RunAsAdministrator
            }).ToList()
        }).ToList(),
        SteamGridDbGameIds = new Dictionary<string, int>(source.SteamGridDbGameIds, StringComparer.OrdinalIgnoreCase)
    };

    private sealed class GameVisibilityItem : INotifyPropertyChanged
    {
        public Game Game { get; }
        public string Name => Game.Name;
        public string ProviderId => Game.ProviderId;
        public bool IsFavorite => Game.IsFavorite;
        public bool IsExcluded => Game.IsExcluded;
        private bool _isVisible;
        public bool IsVisible { get => _isVisible; set { if (_isVisible == value) return; _isVisible = value; OnPropertyChanged(); } }
        public GameVisibilityItem(Game game, bool isVisible) { Game = game; _isVisible = isVisible; }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

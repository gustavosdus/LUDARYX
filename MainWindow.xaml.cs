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

internal enum FooterInputMode
{
    Keyboard,
    Xbox,
    PlayStation
}

public partial class MainWindow : Window
{
    #region Fields

    private readonly LibraryService _library = new();
    private readonly GameLaunchService _launcher;
    private readonly CoverService _covers = new();
    private readonly MetadataService _metadata = new();
    private readonly JsonSettingsService _settingsService = new();
    private readonly GameStateService _state = new();
    private readonly DuplicateDetectionService _duplicates = new();
    private readonly GamepadService _gamepad;
    private LauncherSettings _settings = new();
    private List<Game> _games = new();
    private List<Game> _visibleGames = new();
    private GamePlatform? _platformFilter;
    private string _specialFilter = "";
    private int _selectedIndex = 0;
    private bool _fullscreen;
    private WindowState _previousWindowState;
    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;
    private bool _previousTopmost;
    private Rect _previousWindowBounds;
    private bool _controllerConnected;
    private DateTime _nextNavigationAllowedUtc = DateTime.MinValue;
    private int _pendingAnalogVerticalDirection;
    private int _pendingAnalogVerticalSamples;
    private int _pendingAnalogHorizontalDirection;
    private int _pendingAnalogHorizontalSamples;
    private NotifyIcon? _trayIcon;
    private bool _allowRealClose;
    private bool _mainWindowLoaded;
    private readonly System.Media.SoundPlayer? _navigationSound;
    private readonly System.Media.SoundPlayer? _powerOnSound;
    private readonly System.Media.SoundPlayer? _launchSound;
    private bool _startupAnimationPlayed;
    private bool _controllerToolbarMode;
    private int _toolbarIndex;
    private bool _syncingSearchBoxes;
    private FooterInputMode _footerInputMode = FooterInputMode.Keyboard;

    #endregion

    #region Initialization and lifetime

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        _navigationSound = LoadSoundResource("Assets/Navigation.wav");
        _powerOnSound = LoadSoundResource("Assets/PowerOn.wav");
        _launchSound = LoadSoundResource("Assets/Launch.wav");
        _gamepad = new GamepadService(this);
        _launcher = new GameLaunchService(_library);
        Loaded += async (_, _) =>
        {
            _mainWindowLoaded = true;
            _settings = _settingsService.Load();
            LocalizationService.SetLanguage(_settings.Language);
            ApplyLanguage();
            ApplyVisualSettings();

            if (_settings.StartFullscreen && !_fullscreen)
            {
                // A janela já possui um HWND neste ponto, então podemos usar os
                // limites reais do monitor e entrar no mesmo fullscreen do F11.
                ToggleFullscreen();
            }

            // A bandeja é complementar. Se o Windows rejeitar o .ico ou o
            // NotifyIcon, a janela principal ainda deve continuar funcionando.
            try
            {
                InitializeTrayIcon();
            }
            catch
            {
                _trayIcon?.Dispose();
                _trayIcon = null;
            }

            // A animação recebe prioridade na thread da interface. A descoberta da
            // biblioteca ocorre em paralelo numa thread de trabalho, mas a montagem
            // da grade e o enriquecimento só começam depois que o storyboard termina.
            // Isso evita engasgos visuais causados por Registro, arquivos, PowerShell
            // ou criação de muitos cards durante a introdução.
            var startupAnimationTask = PlayStartupAnimationAsync();
            var libraryTask = RefreshLibrary(uiPublishGate: startupAnimationTask);
            await Task.WhenAll(startupAnimationTask, libraryTask);
            _ = MaybeCheckForUpdatesAsync();
        };
        Closing += MainWindow_Closing;
        Closed += (_, _) =>
        {
            _trayIcon?.Dispose();
            _gamepad.Dispose();
        };
        _gamepad.StateChanged += Gamepad_StateChanged;
    }


    #endregion

    #region Audio and startup animation

    private static System.Media.SoundPlayer? LoadSoundResource(string relativePath)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/{relativePath}", UriKind.Absolute);
            var resource = System.Windows.Application.GetResourceStream(uri);
            if (resource?.Stream is null) return null;

            var memory = new MemoryStream();
            resource.Stream.CopyTo(memory);
            memory.Position = 0;
            var player = new System.Media.SoundPlayer(memory);
            player.Load();
            return player;
        }
        catch
        {
            return null;
        }
    }

    private void PlayNavigationSound()
    {
        try { _navigationSound?.Play(); } catch { }
    }

    private void PlayLaunchSound()
    {
        try { _launchSound?.Play(); } catch { }
    }

    private async Task PlayStartupAnimationAsync()
    {
        if (_startupAnimationPlayed) return;
        _startupAnimationPlayed = true;

        StartupOverlay.Visibility = Visibility.Visible;
        StartupOverlay.Opacity = 1;

        if (FindResource("StartupStoryboard") is Storyboard storyboard)
            storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);

        // O pulso principal da animação acontece por volta de 3,5 s.
        await Task.Delay(3500);
        try { _powerOnSound?.Play(); } catch { }

        // O storyboard inicia o fade-out aos 4,5 s e termina em torno de 5 s.
        await Task.Delay(1500);
        StartupOverlay.Visibility = Visibility.Collapsed;
        StartupOverlay.Opacity = 1;
    }

    private async Task MaybeCheckForUpdatesAsync()
    {
        try
        {
            if (!_settings.CheckForUpdatesOnStartup)
                return;

            var lastCheckUtc = _settings.LastUpdateCheckUtc;
            if (lastCheckUtc.HasValue && DateTime.UtcNow - lastCheckUtc.Value < TimeSpan.FromHours(24))
                return;

            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settingsService.Save(_settings);

            var result = await GitHubUpdateService.CheckForUpdatesAsync();
            if (!result.IsUpdateAvailable)
                return;

            var shouldCloseForInstaller = await GitHubUpdateService.PromptAndDownloadUpdateAsync(this, result, CancellationToken.None);
            if (!shouldCloseForInstaller)
                return;

            _allowRealClose = true;
            try { _trayIcon?.Dispose(); } catch { }
            Close();
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException("Automatic update check failed", ex);
        }
    }

    #endregion

    #region System tray

    private void InitializeTrayIcon()
    {
        Icon trayIcon = SystemIcons.Application;
        try
        {
            // O ícone é um Resource WPF embutido no assembly. Isso continua
            // funcionando quando o aplicativo é publicado como single-file e
            // instalado em Program Files, sem depender de Assets\LUDARYX.ico
            // existir fisicamente ao lado do executável.
            var iconUri = new Uri("pack://application:,,,/Assets/LUDARYX.ico", UriKind.Absolute);
            var iconResource = System.Windows.Application.GetResourceStream(iconUri);
            if (iconResource?.Stream is not null)
            {
                using var embeddedIcon = new Icon(iconResource.Stream);
                trayIcon = (Icon)embeddedIcon.Clone();
            }
        }
        catch
        {
            // Mantém o ícone padrão caso o recurso embutido não possa ser lido.
        }

        _trayIcon = new NotifyIcon
        {
            Text = "LUDARYX",
            Icon = trayIcon,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        var menu = new ContextMenuStrip();
        menu.Items.Add(LocalizationService.Translate("Abrir LUDARYX"), null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(LocalizationService.Translate("Encerrar"), null, (_, _) => ExitFromTray());
        _trayIcon.ContextMenuStrip = menu;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowRealClose) return;

        // Nunca transforme um fechamento ocorrido durante a inicialização em
        // uma janela invisível presa na bandeja. O comportamento de ocultar
        // só é habilitado depois que a janela principal realmente carregou.
        if (!_mainWindowLoaded) return;

        e.Cancel = true;
        Hide();
    }

    private void RestoreFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            if (!IsVisible) Show();
            ShowInTaskbar = true;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        });
    }

    public void RestoreFromExternalLaunch() => RestoreFromTray();

    private void ExitFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            _allowRealClose = true;
            _trayIcon!.Visible = false;
            Close();
            System.Windows.Application.Current.Shutdown();
        });
    }

    #endregion

    #region Library loading and presentation

    private async Task RefreshLibrary(
        string loadingMessage = "CARREGANDO BIBLIOTECA...",
        bool forceArtworkRefresh = false,
        Task? uiPublishGate = null)
    {
        await SetLibraryLoadingAsync(true, loadingMessage);

        _settings = _settingsService.Load();
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

                if (!settingsSnapshot.ShowStoreApps)
                    discovered = discovered.Where(g => g.Platform != GamePlatform.Xbox).ToList();

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
            PublishLoadedGames(loadedGames);
            StatusText.Text = $"{_games.Count} jogos na biblioteca";

            await SetLibraryLoadingAsync(true, "ATUALIZANDO BIBLIOTECA...");
            await EnrichLibraryBestEffortAsync(loadedGames, forceArtworkRefresh);

            // Metadados podem alterar nome, gênero e capas. Reaplica somente a apresentação
            // depois do enriquecimento, sem substituir a geração de objetos já exibida.
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
        if (!string.IsNullOrWhiteSpace(message))
        {
            var localizedMessage = LocalizationService.Translate(message);
            LibraryLoadingText.Text = localizedMessage;
            FullscreenLibraryLoadingText.Text = localizedMessage;
            StartupLibraryLoadingText.Text = localizedMessage;
        }

        var visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        LibraryLoadingText.Visibility = !_fullscreen ? visibility : Visibility.Collapsed;
        FullscreenLibraryLoadingText.Visibility = _fullscreen ? visibility : Visibility.Collapsed;
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
            switch (_settings.CoverMode)
            {
                case "Vertical":
                    game.CoverImage = game.VerticalCover;
                    game.CoverWidth = 180;
                    game.CoverHeight = 270;
                    break;
                case "Horizontal":
                    game.CoverImage = game.HorizontalCover;
                    game.CoverWidth = 196;
                    game.CoverHeight = 92;
                    break;
                default:
                    game.CoverImage = game.HorizontalCover;
                    game.CoverWidth = 196;
                    game.CoverHeight = 92;
                    break;
            }
        }
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

    #region Window commands and primary actions

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeWindow();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void ToggleMaximizeWindow()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleMaximizeWindow();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshLibrary("ATUALIZANDO BIBLIOTECA...", forceArtworkRefresh: true);
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_games, _settings) { Owner = this };
        if (window.ShowDialog() == true) await RefreshLibrary("ATUALIZANDO BIBLIOTECA...");
    }


    #endregion

    #region Filtering and search

    private void LibraryFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LibraryFilterCombo.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag?.ToString() ?? "all";
        _platformFilter = null;
        _specialFilter = "";
        switch (tag)
        {
            case "favorites": _specialFilter = "favorites"; break;
            case "recent": _specialFilter = "recent"; break;
            case "duplicates": _specialFilter = "duplicates"; break;
            case "Steam": _platformFilter = GamePlatform.Steam; break;
            case "Epic": _platformFilter = GamePlatform.Epic; break;
            case "GOG": _platformFilter = GamePlatform.GOG; break;
            case "Xbox": _platformFilter = GamePlatform.Xbox; break;
            case "EAApp": _platformFilter = GamePlatform.EAApp; break;
            case "UbisoftConnect": _platformFilter = GamePlatform.UbisoftConnect; break;
            case "BattleNet": _platformFilter = GamePlatform.BattleNet; break;
            case "RiotClient": _platformFilter = GamePlatform.RiotClient; break;
            case "Manual": _platformFilter = GamePlatform.Manual; break;
        }
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

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var genre = (GenreFilter.SelectedItem as GenreFilterOption)?.CanonicalGenre;
        var showHidden = _settings.ShowHiddenApps;
        _visibleGames = _games.Where(g =>
            !g.IsExcluded &&
            (showHidden || !g.IsHidden) &&
            (!_platformFilter.HasValue || g.Platform == _platformFilter.Value) &&
            (_specialFilter != "favorites" || g.IsFavorite) &&
            (_specialFilter != "recent" || g.LastPlayedUtc.HasValue) &&
            (_specialFilter != "duplicates" || g.IsDuplicate) &&
            (genre == null || g.Metadata.Genres.Any(x => x.Equals(genre, StringComparison.OrdinalIgnoreCase))) &&
            (string.IsNullOrWhiteSpace(query) || g.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(g => _specialFilter == "recent" ? g.LastPlayedUtc : null)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();

        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _visibleGames.Count - 1));
        UpdateControllerSelection();
        GameList.ItemsSource = null;
        GameList.ItemsSource = _visibleGames;
        UpdateControllerSelection();
        GameCountText.Text = LocalizedGameCount(_visibleGames.Count);
    }

    #endregion

    #region Controller navigation

    private void UpdateControllerSelection()
    {
        // O destaque azul é exclusivo do contexto ativo:
        // barra ativa = nenhum jogo destacado; biblioteca ativa = apenas o jogo selecionado.
        // Limpa tanto a biblioteca publicada quanto a lista que está atualmente
        // na tela. Em recargas assíncronas elas podem, por alguns instantes, conter
        // instâncias de gerações diferentes. Limpar a união impede que cards antigos
        // preservem o contorno azul ao navegar.
        foreach (var g in _games.Concat(_visibleGames).Distinct())
            g.IsControllerSelected = false;

        if (_visibleGames.Count > 0 && !_controllerToolbarMode)
            _visibleGames[_selectedIndex].IsControllerSelected = true;

        ControllerStatusText.Text = GetControllerStatusText(includeSelectedGame: true);
    }

    private string GetControllerStatusText(bool includeSelectedGame = false)
    {
        if (!_controllerConnected)
            return LocalizationService.Translate("Controle: procurando...");

        if (_controllerToolbarMode)
        {
            return _fullscreen
                ? $"{LocalizationService.Translate("MODO TV")} • {LocalizationService.Translate("Barra de opções")}"
                : $"{LocalizationService.Translate("Controle conectado")} • {LocalizationService.Translate("Barra de opções")}";
        }

        if (includeSelectedGame)
        {
            var gameName = _visibleGames.ElementAtOrDefault(_selectedIndex)?.Name
                ?? LocalizationService.Translate("Nenhum jogo");
            return $"{LocalizationService.Translate("Controle conectado")} • {gameName}";
        }

        return _fullscreen
            ? $"{LocalizationService.Translate("MODO TV")} • {LocalizationService.Translate("Controle conectado")}"
            : LocalizationService.Translate("Controle conectado");
    }

    private async void Gamepad_StateChanged(object? sender, GamepadState state)
    {
        // O controle só comanda o launcher quando esta janela está em primeiro plano.
        if (!IsActive || !IsVisible || WindowState == WindowState.Minimized)
        {
            ResetAnalogNavigation();
            return;
        }

        if (!state.IsConnected)
        {
            _controllerConnected = false;
            _controllerToolbarMode = false;
            ControllerStatusText.Text = LocalizationService.Translate("Controle: procurando...");
            return;
        }

        _controllerConnected = true;
        ControllerStatusText.Text = GetControllerStatusText(includeSelectedGame: true);

        // A legenda do rodapé acompanha o último método realmente usado. O evento
        // do controle é publicado continuamente, então só trocamos o ícone quando
        // existe entrada significativa para não sobrescrever o teclado por inércia.
        if (HasMeaningfulGamepadInput(state))
        {
            SetFooterInputMode(_gamepad.ActiveDeviceKind == GamepadDeviceKind.PlayStation
                ? FooterInputMode.PlayStation
                : FooterInputMode.Xbox);
        }

        const short axisThreshold = 22000;
        var analogVertical = state.LeftY > axisThreshold ? 1 : state.LeftY < -axisThreshold ? -1 : 0;
        var analogHorizontal = state.LeftX > axisThreshold ? 1 : state.LeftX < -axisThreshold ? -1 : 0;
        UpdateAnalogSamples(analogVertical, analogHorizontal);

        var up = state.Buttons.HasFlag(GamepadButtons.DPadUp) ||
                 (_pendingAnalogVerticalDirection == 1 && _pendingAnalogVerticalSamples >= 2);
        var down = state.Buttons.HasFlag(GamepadButtons.DPadDown) ||
                   (_pendingAnalogVerticalDirection == -1 && _pendingAnalogVerticalSamples >= 2);
        var left = state.Buttons.HasFlag(GamepadButtons.DPadLeft) ||
                   (_pendingAnalogHorizontalDirection == -1 && _pendingAnalogHorizontalSamples >= 2);
        var right = state.Buttons.HasFlag(GamepadButtons.DPadRight) ||
                    (_pendingAnalogHorizontalDirection == 1 && _pendingAnalogHorizontalSamples >= 2);

        // R1 dá acesso direto à barra tanto no modo padrão quanto em tela cheia.
        // B retorna à biblioteca quando a barra está ativa.
        if (_gamepad.WasPressed(GamepadButtons.RightShoulder, state))
        {
            EnterToolbarMode();
            return;
        }

        if (_controllerToolbarMode)
        {
            if (_gamepad.WasPressed(GamepadButtons.B, state))
            {
                ExitToolbarMode();
                return;
            }
            if (_gamepad.WasPressed(GamepadButtons.X, state) || _gamepad.WasPressed(GamepadButtons.Start, state))
            {
                ToggleFullscreen();
                return;
            }

            if (DateTime.UtcNow >= _nextNavigationAllowedUtc)
            {
                if (left) MoveToolbarSelection(-1);
                else if (right) MoveToolbarSelection(1);
                else if (up) AdjustToolbarValue(-1);
                else if (down) AdjustToolbarValue(1);
                if (up || down || left || right) _nextNavigationAllowedUtc = DateTime.UtcNow.AddMilliseconds(170);
            }

            if (_gamepad.WasPressed(GamepadButtons.A, state)) ActivateToolbarControl();
            return;
        }

        if (_visibleGames.Count == 0)
        {
            if (up || _gamepad.WasPressed(GamepadButtons.RightShoulder, state)) EnterToolbarMode();
            return;
        }

        if (DateTime.UtcNow >= _nextNavigationAllowedUtc)
        {
            if (up && _selectedIndex < GetColumns())
                EnterToolbarMode();
            else if (up) MoveSelection(-GetColumns());
            else if (down) MoveSelection(GetColumns());
            else if (left) MoveSelection(-1);
            else if (right) MoveSelection(1);
            if (up || down || left || right) _nextNavigationAllowedUtc = DateTime.UtcNow.AddMilliseconds(170);
        }

        if (_gamepad.WasPressed(GamepadButtons.A, state)) await LaunchSelectedAsync();
        else if (_gamepad.WasPressed(GamepadButtons.B, state)) OpenSelectedDetails();
        else if (_gamepad.WasPressed(GamepadButtons.X, state)) ToggleFullscreen();
        else if (_gamepad.WasPressed(GamepadButtons.Y, state)) ToggleSelectedFavorite();
        else if (_gamepad.WasPressed(GamepadButtons.Start, state)) ToggleFullscreen();
        else if (_gamepad.WasPressed(GamepadButtons.Back, state) && _fullscreen) ToggleFullscreen();
    }

    private void SetFooterInputMode(FooterInputMode mode)
    {
        if (_footerInputMode == mode) return;

        _footerInputMode = mode;
        UpdateFooterInputHints();
    }

    private void UpdateFooterInputHints()
    {
        KeyboardFooterHints.Visibility = _footerInputMode == FooterInputMode.Keyboard
            ? Visibility.Visible
            : Visibility.Collapsed;
        XboxFooterHints.Visibility = _footerInputMode == FooterInputMode.Xbox
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlayStationFooterHints.Visibility = _footerInputMode == FooterInputMode.PlayStation
            ? Visibility.Visible
            : Visibility.Collapsed;

        var selectText = LocalizationService.Translate("SELECIONAR");
        var customizeText = LocalizationService.Translate("PERSONALIZAR");

        KeyboardSelectHintText.Text = selectText;
        XboxSelectHintText.Text = selectText;
        PlayStationSelectHintText.Text = selectText;

        KeyboardCustomizeHintText.Text = customizeText;
        XboxCustomizeHintText.Text = customizeText;
        PlayStationCustomizeHintText.Text = customizeText;
    }

    private static bool HasMeaningfulGamepadInput(GamepadState state)
    {
        const short stickThreshold = 12000;
        return state.Buttons != GamepadButtons.None ||
               Math.Abs((int)state.LeftX) >= stickThreshold ||
               Math.Abs((int)state.LeftY) >= stickThreshold ||
               state.LeftTrigger >= 40 ||
               state.RightTrigger >= 40;
    }

    private void ResetAnalogNavigation()
    {
        _pendingAnalogVerticalDirection = 0;
        _pendingAnalogVerticalSamples = 0;
        _pendingAnalogHorizontalDirection = 0;
        _pendingAnalogHorizontalSamples = 0;
    }

    private void UpdateAnalogSamples(int analogVertical, int analogHorizontal)
    {
        if (analogVertical == 0)
        {
            _pendingAnalogVerticalDirection = 0;
            _pendingAnalogVerticalSamples = 0;
        }
        else if (analogVertical == _pendingAnalogVerticalDirection) _pendingAnalogVerticalSamples++;
        else
        {
            _pendingAnalogVerticalDirection = analogVertical;
            _pendingAnalogVerticalSamples = 1;
        }

        if (analogHorizontal == 0)
        {
            _pendingAnalogHorizontalDirection = 0;
            _pendingAnalogHorizontalSamples = 0;
        }
        else if (analogHorizontal == _pendingAnalogHorizontalDirection) _pendingAnalogHorizontalSamples++;
        else
        {
            _pendingAnalogHorizontalDirection = analogHorizontal;
            _pendingAnalogHorizontalSamples = 1;
        }
    }

    #endregion

    #region Toolbar navigation

    private IReadOnlyList<System.Windows.Controls.Control> GetToolbarControls()
    {
        var controls = new List<System.Windows.Controls.Control>
        {
            LibraryFilterCombo,
            GenreFilter,
            AddGameButton,
            RefreshButton,
            SettingsButton
        };

        // Estes controles só existem visualmente na barra durante o modo tela cheia.
        if (_fullscreen)
        {
            controls.Add(FullscreenSearchBox);
            controls.Add(FullscreenToolbarButton);
        }

        return controls;
    }

    private void EnterToolbarMode()
    {
        var controls = GetToolbarControls();
        if (controls.Count == 0) return;

        _controllerToolbarMode = true;
        _toolbarIndex = Math.Clamp(_toolbarIndex, 0, controls.Count - 1);
        UpdateControllerSelection();
        GameList.Items.Refresh();
        FocusToolbarControl();
        PlayNavigationSound();
        ControllerStatusText.Text = GetControllerStatusText();
    }

    private void ExitToolbarMode()
    {
        var controls = GetToolbarControls();
        if (_toolbarIndex >= 0 && _toolbarIndex < controls.Count && controls[_toolbarIndex] is ComboBox combo)
            combo.IsDropDownOpen = false;

        _controllerToolbarMode = false;
        UpdateControllerSelection();
        GameList.Items.Refresh();
        Keyboard.ClearFocus();
        GameList.Focus();
        Keyboard.Focus(GameList);
        PlayNavigationSound();
        UpdateControllerSelection();
    }

    private void MoveToolbarSelection(int delta)
    {
        var controls = GetToolbarControls();
        if (controls.Count == 0) return;
        var next = Math.Clamp(_toolbarIndex + delta, 0, controls.Count - 1);
        if (next == _toolbarIndex) return;
        if (controls[_toolbarIndex] is ComboBox currentCombo) currentCombo.IsDropDownOpen = false;
        _toolbarIndex = next;
        FocusToolbarControl();
        PlayNavigationSound();
    }

    private void FocusToolbarControl()
    {
        var controls = GetToolbarControls();
        if (_toolbarIndex < 0 || _toolbarIndex >= controls.Count) return;
        controls[_toolbarIndex].Focus();
        Keyboard.Focus(controls[_toolbarIndex]);
    }

    private void AdjustToolbarValue(int delta)
    {
        var controls = GetToolbarControls();
        if (_toolbarIndex < 0 || _toolbarIndex >= controls.Count) return;
        if (controls[_toolbarIndex] is ComboBox combo && combo.Items.Count > 0)
        {
            var old = Math.Max(0, combo.SelectedIndex);
            var next = Math.Clamp(old + delta, 0, combo.Items.Count - 1);
            if (next != old)
            {
                combo.SelectedIndex = next;
                combo.IsDropDownOpen = true;
                PlayNavigationSound();
            }
            return;
        }

        // Para botões/campo de pesquisa, para baixo retorna naturalmente à biblioteca.
        if (delta > 0) ExitToolbarMode();
    }

    private void ActivateToolbarControl()
    {
        var controls = GetToolbarControls();
        if (_toolbarIndex < 0 || _toolbarIndex >= controls.Count) return;
        switch (controls[_toolbarIndex])
        {
            case ComboBox combo:
                combo.IsDropDownOpen = !combo.IsDropDownOpen;
                break;
            case Button button:
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                break;
            case TextBox textBox:
                textBox.Focus();
                Keyboard.Focus(textBox);
                break;
        }
    }

    #endregion

    #region Game selection and actions

    private int GetColumns() => 6;

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
        if (index < 0) return;
        _selectedIndex = index;
        UpdateControllerSelection();
        GameList.Items.Refresh();
    }

    private void OpenDetails(Game game)
    {
        var window = new DetailsWindow(game, _settings, _launcher, _metadata) { Owner = this };
        window.ShowDialog();
        _settings = _settingsService.Load();
        foreach (var g in _games) _state.Apply(g, _settings);
        ApplyCoverMode();
        ApplyFilter();
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
        try
        {
            PlayLaunchSound();
            StatusText.Text = $"Iniciando {game.Name}...";
            await _launcher.LaunchAsync(game);
            _state.MarkPlayed(game, _settings);
            StatusText.Text = $"Executando: {game.Name}";
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Falha ao iniciar o jogo";
            MessageBox.Show($"Não foi possível iniciar {game.Name}.\n\n{ex.Message}", "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Native window sizing

    // WindowChrome remove a borda padrão do Windows. Sem este hook, uma janela
    // maximizada pode ocupar o monitor inteiro e ficar atrás da barra de tarefas.
    // WM_GETMINMAXINFO limita somente o modo maximizado à área útil do monitor.
    // O fullscreen real continua usando rcMonitor em ToggleFullscreen().
    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(MainWindow_WndProc);

        // Resoluções menores que o tamanho padrão (1400x900) não podem deixar a
        // janela nascer parcialmente fora da tela. Ajusta e centraliza antes do
        // primeiro desenho, sempre dentro da área útil do monitor.
        WindowPlacementService.FitToWorkingArea(this, margin: 8, center: true);
    }

    private IntPtr MainWindow_WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO && !_fullscreen)
        {
            ApplyMaximizedWorkArea(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void ApplyMaximizedWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            return;

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
            return;

        var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var workArea = monitorInfo.rcWork;
        var monitorArea = monitorInfo.rcMonitor;

        minMaxInfo.ptMaxPosition.X = workArea.Left - monitorArea.Left;
        minMaxInfo.ptMaxPosition.Y = workArea.Top - monitorArea.Top;
        minMaxInfo.ptMaxSize.X = workArea.Right - workArea.Left;
        minMaxInfo.ptMaxSize.Y = workArea.Bottom - workArea.Top;
        minMaxInfo.ptMaxTrackSize = minMaxInfo.ptMaxSize;

        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    private const int WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    #endregion

    #region Fullscreen and monitor handling

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _previousWindowState = WindowState;
            _previousWindowStyle = WindowStyle;
            _previousResizeMode = ResizeMode;
            _previousTopmost = Topmost;
            _previousWindowBounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            // Marca o estado de fullscreen ANTES de alterar o tamanho da janela.
            // Durante a transição o Windows envia WM_GETMINMAXINFO; se _fullscreen
            // ainda estiver false, o hook de maximização limita a janela à área útil
            // (acima da barra de tarefas), fazendo o fullscreen se comportar como
            // uma janela apenas maximizada.
            _fullscreen = true;

            // WindowState.Maximized usa a área de trabalho do Windows e, por isso,
            // deixa a barra de tarefas visível. Para fullscreen real usamos os
            // limites físicos do monitor atual.
            var monitorBounds = GetCurrentMonitorBounds();
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Left = monitorBounds.Left;
            Top = monitorBounds.Top;
            Width = monitorBounds.Width;
            Height = monitorBounds.Height;
            Cursor = Cursors.None;
        }
        else
        {
            Cursor = Cursors.Arrow;

            // Ao sair do fullscreen, reativa primeiro o comportamento normal de
            // WM_GETMINMAXINFO para que um estado maximizado volte a respeitar a
            // área útil do monitor e a barra de tarefas.
            _fullscreen = false;

            Topmost = _previousTopmost;
            WindowStyle = _previousWindowStyle;
            ResizeMode = _previousResizeMode;

            if (_previousWindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                Left = _previousWindowBounds.Left;
                Top = _previousWindowBounds.Top;
                Width = _previousWindowBounds.Width;
                Height = _previousWindowBounds.Height;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Normal;
                Left = _previousWindowBounds.Left;
                Top = _previousWindowBounds.Top;
                Width = _previousWindowBounds.Width;
                Height = _previousWindowBounds.Height;
                WindowState = _previousWindowState;
            }
        }
        UpdateFullscreenUi();
    }

    private Rect GetCurrentMonitorBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            return new Rect(info.rcMonitor.Left, info.rcMonitor.Top,
                info.rcMonitor.Right - info.rcMonitor.Left,
                info.rcMonitor.Bottom - info.rcMonitor.Top);

        return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private void UpdateFullscreenUi()
    {
        HeaderGrid.Visibility = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
        HeaderRow.Height = new GridLength(_fullscreen ? 0 : 112);
        FullscreenSearchContainer.Visibility = _fullscreen ? Visibility.Visible : Visibility.Collapsed;
        FullscreenToolbarButton.Visibility = _fullscreen ? Visibility.Visible : Visibility.Collapsed;

        // O indicador acompanha o layout atual: abaixo da logo no modo padrão e
        // ao lado da pesquisa na fileira quando estiver em tela cheia.
        var loadingVisible = LibraryLoadingText.Visibility == Visibility.Visible ||
                             FullscreenLibraryLoadingText.Visibility == Visibility.Visible;
        LibraryLoadingText.Visibility = !_fullscreen && loadingVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        FullscreenLibraryLoadingText.Visibility = _fullscreen && loadingVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_controllerToolbarMode)
        {
            var controls = GetToolbarControls();
            if (controls.Count > 0)
            {
                _toolbarIndex = Math.Clamp(_toolbarIndex, 0, controls.Count - 1);
                Dispatcher.BeginInvoke(FocusToolbarControl, DispatcherPriority.Background);
            }
        }

        ControllerStatusText.Text = GetControllerStatusText();
    }

    #endregion

    #region Theme and visual settings

    private void ApplyLanguage()
    {
        LocalizationService.Apply(this);

        foreach (var item in LibraryFilterCombo.Items.OfType<ComboBoxItem>())
        {
            if (item.Content is string text)
                item.Content = LocalizationService.Translate(text);
        }

        // Alguns textos são gerados em tempo de execução e precisam ser refeitos
        // após uma troca de idioma.
        GameCountText.Text = LocalizedGameCount(_visibleGames.Count);
        UpdateFooterInputHints();

        if (_trayIcon?.ContextMenuStrip is { } menu && menu.Items.Count >= 3)
        {
            menu.Items[0].Text = LocalizationService.Translate("Abrir LUDARYX");
            menu.Items[2].Text = LocalizationService.Translate("Encerrar");
        }
    }

    private static string LocalizedGameCount(int count)
    {
        return LocalizationService.CurrentLanguage switch
        {
            LocalizationService.PortuguesePortugal => $"{count} jogos apresentados",
            LocalizationService.EnglishUnitedStates or LocalizationService.EnglishUnitedKingdom => $"{count} games shown",
            LocalizationService.SpanishLatinAmerica or LocalizationService.SpanishEurope => $"{count} juegos mostrados",
            _ => $"{count} jogos exibidos"
        };
    }

    private void ApplyVisualSettings()
    {
        NeonSeparator.Background = _settings.NeonLineColor == "LightBlue"
            ? (Brush)FindResource("NeonBlue")
            : (Brush)FindResource("NeonRed");

        var lightTheme = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        var topBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#666B73" : "#050507"));
        var libraryBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#D9DDE3" : "#07182D"));
        var footerBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#666B73" : "#050507"));
        var selectionBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#FF163D" : "#66F5FF");
        var selectionGameFillColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#FF334F" : "#13DFFF");

        // No tema claro, Launcher e Gênero acompanham o cinza do cabeçalho.
        // Pesquisa e botões de ação usam um cinza levemente mais escuro para
        // ficarem perceptíveis sem destoar. No tema escuro, preservamos as
        // cores originais. O cursor da pesquisa é preto no claro e branco no escuro.
        var toolbarFieldColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#666B73" : "#050507");
        var searchFieldColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#565B62" : "#11151D");
        var navButtonColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#565B62" : "#11151D");
        var navButtonHoverColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#60666E" : "#1A202B");
        var navButtonPressedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#4C5158" : "#252D3A");
        var toolbarFieldHoverColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#737982" : "#0A0D12");
        var toolbarPopupColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#666B73" : "#050507");
        var toolbarItemHoverColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#737982" : "#111A26");
        var toolbarItemSelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#555B63" : "#10283D");
        var searchCaretColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#000000" : "#FFFFFF");

        MainRoot.Background = topBrush;
        HeaderGrid.Background = topBrush;
        ToolbarBorder.Background = topBrush;
        LibraryAreaBorder.Background = libraryBrush;
        FooterBorder.Background = footerBrush;

        SetOrUpdateBrushResource("SelectionAccentBorder", selectionBorderColor);
        SetOrUpdateBrushResource("SelectionAccentGameFill", selectionGameFillColor);
        SetOrUpdateBrushResource("ToolbarFieldBackground", toolbarFieldColor);
        SetOrUpdateBrushResource("SearchFieldBackground", searchFieldColor);
        SetOrUpdateBrushResource("NavButtonBackground", navButtonColor);
        SetOrUpdateBrushResource("NavButtonHoverBackground", navButtonHoverColor);
        SetOrUpdateBrushResource("NavButtonPressedBackground", navButtonPressedColor);
        SetOrUpdateBrushResource("ToolbarFieldHoverBackground", toolbarFieldHoverColor);
        SetOrUpdateBrushResource("ToolbarPopupBackground", toolbarPopupColor);
        SetOrUpdateBrushResource("ToolbarItemHoverBackground", toolbarItemHoverColor);
        SetOrUpdateBrushResource("ToolbarItemSelectedBackground", toolbarItemSelectedColor);
        SetOrUpdateBrushResource("SearchCaretBrush", searchCaretColor);
    }


    private void SetOrUpdateBrushResource(string key, System.Windows.Media.Color color)
    {
        // Brushes declarados em XAML podem ser congelados (Freezable) pelo WPF.
        // Alterar brush.Color nesses casos lança InvalidOperationException e pode
        // encerrar o aplicativo ao trocar o tema. Substituir o recurso inteiro
        // mantém os DynamicResource atualizados sem modificar uma instância congelada.
        Resources[key] = new SolidColorBrush(color);
    }

    #endregion

    #region Keyboard navigation

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F11) ToggleFullscreen();
        else if (e.Key == Key.Escape && _fullscreen) ToggleFullscreen();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Qualquer uso do teclado torna as dicas Enter/Espaço as dicas ativas.
        SetFooterInputMode(FooterInputMode.Keyboard);

        // Quando a pesquisa está com foco, mantém a edição de texto sem prender
        // a navegação do teclado dentro do TextBox. As setas verticais deixam
        // explicitamente a pesquisa: para cima vai à barra e para baixo aos jogos.
        if (Keyboard.FocusedElement is TextBox focusedTextBox &&
            (ReferenceEquals(focusedTextBox, SearchBox) || ReferenceEquals(focusedTextBox, FullscreenSearchBox)))
        {
            switch (e.Key)
            {
                case Key.Down:
                case Key.Escape:
                    ExitToolbarMode();
                    e.Handled = true;
                    return;
                case Key.Up:
                    _controllerToolbarMode = true;
                    _toolbarIndex = 0;
                    UpdateControllerSelection();
                    GameList.Items.Refresh();
                    FocusToolbarControl();
                    PlayNavigationSound();
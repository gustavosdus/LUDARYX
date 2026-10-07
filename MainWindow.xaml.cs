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
    private readonly GameSessionService _sessions = new();
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
    private double _previousChromeCaptionHeight = 40;
    private Thickness _previousChromeResizeBorderThickness = new(6);
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
    private bool _tvHeroMode;
    private int _tvHeroActionIndex;
    private bool _sidePanelMode;
    private bool _suppressHeroVerticalUntilNeutral;
    private int _sidePanelActionIndex;
    private bool _syncingSearchBoxes;
    private CancellationTokenSource? _selectedDescriptionCts;
    private FooterInputMode _footerInputMode = FooterInputMode.Keyboard;
    private readonly Dictionary<string, double> _artworkAspectRatioCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _syncingLibraryColumnsSlider;
    private bool _syncingToolbarPreferenceSliders;
    private bool _isLibraryLoading;
    private CancellationTokenSource? _gameLaunchCts;

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
            SyncToolbarPreferenceSliders();
            if (_settings.AutoCleanupCache)
                _ = Task.Run(() => CacheMaintenanceService.CleanupToLimit(_settings.MaxCacheSizeMb));
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
            _selectedDescriptionCts?.Cancel();
            _selectedDescriptionCts?.Dispose();
            _gamepad.Dispose();
            _sessions.Dispose();
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

            // A opção diz "ao iniciar": quando habilitada, consulta o GitHub em
            // toda inicialização. LastUpdateCheckUtc é apenas informativo e não deve
            // impedir uma nova versão publicada poucas horas após a última abertura.
            var result = await GitHubUpdateService.CheckForUpdatesAsync();
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settingsService.Save(_settings);

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
            PublishLoadedGames(loadedGames);
            StatusText.Text = $"{_games.Count} jogos na biblioteca";

            await SetLibraryLoadingAsync(true, "ATUALIZANDO BIBLIOTECA...");
            await EnrichLibraryBestEffortAsync(loadedGames, forceArtworkRefresh);

            // Metadados podem alterar o nome canônico. Recalcula duplicatas depois do
            // enriquecimento para que o filtro reflita os nomes finais e versões copiadas.
            _duplicates.Detect(loadedGames);
            EnsureDuplicatePrimarySelections(loadedGames);

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

    #region Window commands and primary actions

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Fullscreen/TV deve se comportar como tela cheia real, não como uma janela
        // sem borda que pode ser arrastada ou "desgrudada" do monitor.
        if (_fullscreen)
        {
            e.Handled = true;
            return;
        }

        // Controles interativos dentro do cabeçalho (principalmente a pesquisa)
        // nunca devem iniciar DragMove. WindowChrome trata parte do cabeçalho como
        // área não-cliente, então checamos também os ancestrais do elemento clicado.
        if (IsInteractiveHeaderSource(e.OriginalSource as DependencyObject))
            return;

        if (e.ClickCount == 2)
        {
            ToggleMaximizeWindow();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private bool IsInteractiveHeaderSource(DependencyObject? source)
    {
        while (source is not null && source != HeaderGrid)
        {
            if (source is TextBox ||
                source is Button ||
                source is ComboBox ||
                source is IInputElement inputElement &&
                System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(inputElement))
            {
                return true;
            }

            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    private void ToggleMaximizeWindow()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleMaximizeWindow();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshLibrary("ATUALIZANDO BIBLIOTECA...", forceArtworkRefresh: true);

    private async void RefreshVisible_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleGames.Count == 0)
            return;

        await SetLibraryLoadingAsync(true, "ATUALIZANDO BIBLIOTECA...");
        try
        {
            using var gate = new SemaphoreSlim(3);
            var tasks = _visibleGames.ToList().Select(async game =>
            {
                await gate.WaitAsync();
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    await _metadata.EnrichGameAsync(game, _settings, forceArtworkRefresh: true, cts.Token);
                }
                catch
                {
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(tasks);
            _metadata.SaveCacheSnapshot();
            _duplicates.Detect(_games);
            EnsureDuplicatePrimarySelections(_games);
            ApplyCoverMode();
            BuildGenreFilter();
            ApplyFilter();
            ShowToast($"{_visibleGames.Count} jogos visíveis atualizados.");
        }
        finally
        {
            await SetLibraryLoadingAsync(false);
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_games, _settings) { Owner = this };
        if (window.ShowDialog() == true)
            await RefreshLibrary("ATUALIZANDO BIBLIOTECA...");

        RestoreLibraryFocus();
    }

    private void Statistics_Click(object sender, RoutedEventArgs e)
    {
        var window = new StatisticsWindow(_games) { Owner = this };
        window.ShowDialog();
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
            // ViewportWidth já desconta a barra de rolagem vertical. Usá-lo evita
            // que a capa seja parcialmente escondida quando nome/descrição tornam
            // o painel alto o suficiente para exibir o scrollbar.
            var viewportWidth = SelectedGameScrollViewer.ViewportWidth;
            var sideMaxWidth = viewportWidth > 1
                ? viewportWidth
                : Math.Max(1, SelectedGamePanel.ActualWidth - 30);

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
        // O aparecimento/desaparecimento da barra vertical altera o viewport.
        // Recalcula o quadro usando a nova largura útil.
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
            (_specialFilter == "hidden" ? g.IsHidden : (showHidden || !g.IsHidden)) &&
            (!_platformFilter.HasValue || g.Platform == _platformFilter.Value) &&
            (_specialFilter != "favorites" || g.IsFavorite) &&
            (_specialFilter != "recent" || g.LastPlayedUtc.HasValue) &&
            (_specialFilter != "duplicates" || g.IsDuplicate) &&
            (!_settings.AutoHideDuplicateSecondary ||
             !g.IsDuplicate ||
             !_settings.PreferredDuplicateProviders.TryGetValue(g.CanonicalGameId, out var preferredProvider) ||
             g.ProviderId.Equals(preferredProvider, StringComparison.OrdinalIgnoreCase) ||
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

    #region Controller navigation

    private void ToolbarRegion_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _controllerToolbarMode = true;
        _sidePanelMode = false;
        _tvHeroMode = false;

        var controls = GetToolbarControls();
        if (e.NewFocus is System.Windows.Controls.Control focused)
        {
            var index = controls
                .Select((control, controlIndex) => new { control, controlIndex })
                .FirstOrDefault(item => ReferenceEquals(item.control, focused))
                ?.controlIndex ?? -1;
            if (index >= 0)
                _toolbarIndex = index;
        }

        UpdateControllerSelection();
    }

    private void SidePanelRegion_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_fullscreen)
            return;

        _sidePanelMode = true;
        _controllerToolbarMode = false;
        _tvHeroMode = false;

        var controls = GetSidePanelControls();
        if (e.NewFocus is Button focused)
        {
            var index = controls
                .Select((control, controlIndex) => new { control, controlIndex })
                .FirstOrDefault(item => ReferenceEquals(item.control, focused))
                ?.controlIndex ?? -1;
            if (index >= 0)
                _sidePanelActionIndex = index;
        }

        UpdateControllerSelection();
    }

    private void HeroRegion_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_fullscreen)
            return;

        _tvHeroMode = true;
        _controllerToolbarMode = false;
        _sidePanelMode = false;

        var controls = GetTvHeroControls();
        if (e.NewFocus is Button focused)
        {
            var index = controls
                .Select((control, controlIndex) => new { control, controlIndex })
                .FirstOrDefault(item => ReferenceEquals(item.control, focused))
                ?.controlIndex ?? -1;
            if (index >= 0)
                _tvHeroActionIndex = index;
        }

        UpdateControllerSelection();
    }

    private void GameList_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _controllerToolbarMode = false;
        _sidePanelMode = false;
        _tvHeroMode = false;
        UpdateControllerSelection();
    }

    private void UpdateControllerSelection()
    {
        // O destaque do jogo é exclusivo do contexto ativo:
        // hero/painel/barra ativos = nenhum card destacado; biblioteca ativa = apenas o jogo selecionado.
        // Limpa tanto a biblioteca publicada quanto a lista que está atualmente
        // na tela. Em recargas assíncronas elas podem, por alguns instantes, conter
        // instâncias de gerações diferentes. Limpar a união impede que cards antigos
        // preservem o contorno azul ao navegar.
        foreach (var g in _games.Concat(_visibleGames).Distinct())
            g.IsControllerSelected = false;

        if (_visibleGames.Count > 0 &&
            !_controllerToolbarMode &&
            !_tvHeroMode &&
            !_sidePanelMode)
        {
            _visibleGames[_selectedIndex].IsControllerSelected = true;
        }

        ControllerStatusText.Text = GetControllerStatusText(includeSelectedGame: true);
        UpdateSelectedGamePresentation();
    }

    private string GetControllerStatusText(bool includeSelectedGame = false)
    {
        if (!_controllerConnected)
            return LocalizationService.Translate("Controle: procurando...");

        if (_tvHeroMode && _fullscreen)
            return $"{LocalizationService.Translate("MODO TV")} • {LocalizationService.Translate("Ações do jogo")}";

        if (_sidePanelMode && !_fullscreen)
            return $"{LocalizationService.Translate("Controle conectado")} • {LocalizationService.Translate("Ações do jogo")}";

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
        if (!IsActive)
            return;

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

        // Atalhos de TV: LB/RB percorrem coleções e LT/RT percorrem plataformas.
        // A barra de filtros continua acessível subindo a partir do hero/primeira linha.
        if (_gamepad.WasPressed(GamepadButtons.LeftShoulder, state))
        {
            CycleComboSelection(LibraryFilterCombo, -1);
            return;
        }
        if (_gamepad.WasPressed(GamepadButtons.RightShoulder, state))
        {
            CycleComboSelection(LibraryFilterCombo, 1);
            return;
        }
        if (_gamepad.WasPressed(GamepadButtons.LeftTriggerDigital, state))
        {
            CycleComboSelection(PlatformFilterCombo, -1);
            return;
        }
        if (_gamepad.WasPressed(GamepadButtons.RightTriggerDigital, state))
        {
            CycleComboSelection(PlatformFilterCombo, 1);
            return;
        }

        if (_sidePanelMode && !_fullscreen)
        {
            if (_gamepad.WasPressed(GamepadButtons.Back, state) ||
                _gamepad.WasPressed(GamepadButtons.B, state) ||
                left)
            {
                ExitSidePanelMode();
                return;
            }

            if (up || down)
            {
                if (DateTime.UtcNow >= _nextNavigationAllowedUtc)
                {
                    MoveSidePanelSelection(up ? -1 : 1);
                    _nextNavigationAllowedUtc = DateTime.UtcNow.AddMilliseconds(170);
                }
                return;
            }

            if (_gamepad.WasPressed(GamepadButtons.A, state))
            {
                ActivateSidePanelControl();
                return;
            }

            if (_gamepad.WasPressed(GamepadButtons.X, state))
            {
                OpenSelectedDetails();
                return;
            }

            if (_gamepad.WasPressed(GamepadButtons.Y, state))
            {
                ToggleSelectedFavorite();
                return;
            }

            if (_gamepad.WasPressed(GamepadButtons.Start, state))
            {
                Settings_Click(this, new RoutedEventArgs());
                return;
            }

            return;
        }

        if (_tvHeroMode && _fullscreen)
        {
            if (_gamepad.WasPressed(GamepadButtons.B, state))
            {
                ToggleFullscreen();
                return;
            }

            if (_gamepad.WasPressed(GamepadButtons.Back, state))
            {
                ExitTvHeroMode();
                return;
            }

            if (_suppressHeroVerticalUntilNeutral)
            {
                if (!up && !down && analogVertical == 0)
                    _suppressHeroVerticalUntilNeutral = false;
            }
            else
            {
                if (down)
                {
                    ExitTvHeroMode();
                    return;
                }

                if (up && DateTime.UtcNow >= _nextNavigationAllowedUtc)
                {
                    _tvHeroMode = false;
                    EnterToolbarMode();
                    return;
                }
            }

            if (DateTime.UtcNow >= _nextNavigationAllowedUtc)
            {
                if (left) MoveTvHeroSelection(-1);
                else if (right) MoveTvHeroSelection(1);
                if (left || right) _nextNavigationAllowedUtc = DateTime.UtcNow.AddMilliseconds(170);
            }

            if (_gamepad.WasPressed(GamepadButtons.A, state)) ActivateTvHeroControl();
            else if (_gamepad.WasPressed(GamepadButtons.X, state)) OpenSelectedDetails();
            else if (_gamepad.WasPressed(GamepadButtons.Y, state)) ToggleSelectedFavorite();
            else if (_gamepad.WasPressed(GamepadButtons.Start, state)) Settings_Click(this, new RoutedEventArgs());
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
            {
                EnterToolbarMode();
            }
            else if (up) MoveSelection(-GetColumns());
            else if (down) MoveSelection(GetColumns());
            else if (left) MoveSelection(-1);
            else if (right) MoveSelection(1);
            if (up || down || left || right) _nextNavigationAllowedUtc = DateTime.UtcNow.AddMilliseconds(170);
        }

        if (_gamepad.WasPressed(GamepadButtons.Back, state))
        {
            if (_fullscreen)
            {
                EnterTvHeroMode();
                return;
            }

            if (SelectedGamePanel.Visibility == Visibility.Visible &&
                !_settings.HideGameDetailsPanels)
            {
                EnterSidePanelMode();
                return;
            }
        }

        if (_gamepad.WasPressed(GamepadButtons.A, state)) await LaunchSelectedAsync();
        else if (_gamepad.WasPressed(GamepadButtons.B, state))
        {
            if (_fullscreen) ToggleFullscreen();
            else OpenSelectedDetails();
        }
        else if (_gamepad.WasPressed(GamepadButtons.X, state))
        {
            if (_fullscreen) OpenSelectedDetails();
            else ToggleFullscreen();
        }
        else if (_gamepad.WasPressed(GamepadButtons.Y, state)) ToggleSelectedFavorite();
        else if (_gamepad.WasPressed(GamepadButtons.Start, state))
        {
            if (_fullscreen) Settings_Click(this, new RoutedEventArgs());
            else ToggleFullscreen();
        }
    }

    private void CycleComboSelection(ComboBox combo, int delta)
    {
        if (combo.Items.Count == 0)
            return;

        var current = combo.SelectedIndex < 0 ? 0 : combo.SelectedIndex;
        var next = (current + delta) % combo.Items.Count;
        if (next < 0)
            next += combo.Items.Count;

        combo.SelectedIndex = next;
        PlayNavigationSound();
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

        KeyboardSelectHintText.Text = $"ENTER  {selectText}";
        KeyboardCustomizeHintText.Text = $"SPACE  {customizeText}";
        XboxSelectHintText.Text = $"A  {selectText}";
        PlayStationSelectHintText.Text = $"X  {selectText}";

        if (_fullscreen)
        {
            XboxCustomizeHintText.Text = $"X  {customizeText}";
            PlayStationCustomizeHintText.Text = $"□  {customizeText}";
            XboxExtraHintText.Text = "Y  FAVORITO   •   B  VOLTAR   •   LB/RB  COLEÇÃO   •   LT/RT  PLATAFORMA";
            PlayStationExtraHintText.Text = "△  FAVORITO   •   ○  VOLTAR   •   L1/R1  COLEÇÃO   •   L2/R2  PLATAFORMA";
            KeyboardExtraHintText.Text = "F  FAVORITO   •   TAB  HERO   •   ESC  VOLTAR   •   PGUP/PGDN  COLEÇÃO   •   CTRL+PGUP/PGDN  PLATAFORMA";
        }
        else
        {
            XboxCustomizeHintText.Text = $"X  {LocalizationService.Translate("TELA CHEIA")}";
            PlayStationCustomizeHintText.Text = $"□  {LocalizationService.Translate("TELA CHEIA")}";
            XboxExtraHintText.Text = "B  PERSONALIZAR   •   Y  FAVORITO   •   LB/RB  COLEÇÃO";
            PlayStationExtraHintText.Text = "○  PERSONALIZAR   •   △  FAVORITO   •   L1/R1  COLEÇÃO";
            KeyboardExtraHintText.Text = "F  FAVORITO   •   TAB  PAINEL   •   PGUP/PGDN  COLEÇÃO   •   CTRL+PGUP/PGDN  PLATAFORMA   •   F11  TELA CHEIA";
        }

        UpdateTvHeroInputLabels();
    }

    private void UpdateTvHeroInputLabels()
    {
        if (TvPlayButtonText is null || TvDetailsButtonText is null || TvFavoriteButtonText is null)
            return;

        var selected = _visibleGames.ElementAtOrDefault(_selectedIndex);
        var play = selected?.IsRunning == true
            ? LocalizationService.Translate("EM EXECUÇÃO")
            : LocalizationService.Translate("JOGAR");
        var details = LocalizationService.Translate("DETALHES");
        var favorite = LocalizationService.Translate("FAVORITO");
        var favoriteGlyph = selected?.IsFavorite == true ? "★" : "☆";

        switch (_footerInputMode)
        {
            case FooterInputMode.PlayStation:
                TvPlayButtonText.Text = selected?.IsRunning == true ? play : $"✕  {play}";
                TvDetailsButtonText.Text = $"□  {details}";
                TvFavoriteButtonText.Text = $"△  {favoriteGlyph} {favorite}";
                break;

            case FooterInputMode.Keyboard:
                TvPlayButtonText.Text = selected?.IsRunning == true ? play : $"ENTER  {play}";
                TvDetailsButtonText.Text = $"SPACE  {details}";
                TvFavoriteButtonText.Text = $"F  {favoriteGlyph} {favorite}";
                break;

            default:
                TvPlayButtonText.Text = selected?.IsRunning == true ? play : $"A  {play}";
                TvDetailsButtonText.Text = $"X  {details}";
                TvFavoriteButtonText.Text = $"Y  {favoriteGlyph} {favorite}";
                break;
        }
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
            PlatformFilterCombo,
            GenreFilter,
            SortCombo,
            RefreshVisibleButton,
            ClearFiltersButton,
            AddGameButton,
            LibraryColumnsSlider,
            CoverModeSlider,
            ThemeSlider
        };

        if (_fullscreen)
        {
            controls.Add(FullscreenSearchBox);
            controls.Add(FullscreenRefreshButton);
            controls.Add(FullscreenSettingsButton);
            controls.Add(FullscreenStatisticsButton);
            controls.Add(FullscreenToolbarButton);
        }
        else
        {
            controls.Add(SearchBox);
            controls.Add(GlobalRefreshButton);
            controls.Add(GlobalSettingsButton);
            controls.Add(GlobalStatisticsButton);
            controls.Add(GlobalFullscreenButton);
        }

        return controls
            .Where(control => control.Visibility == Visibility.Visible && control.IsEnabled)
            .ToList();
    }

    private IReadOnlyList<Button> GetTvHeroControls() => new[]
    {
        TvPlayButton,
        TvDetailsButton,
        TvFavoriteButton
    }.Where(button => button.Visibility == Visibility.Visible && button.IsEnabled).ToList();

    private IReadOnlyList<Button> GetSidePanelControls() => new[]
    {
        SelectedPlayButton,
        SelectedDetailsButton,
        SelectedFavoriteButton
    }.Where(button => button.Visibility == Visibility.Visible && button.IsEnabled).ToList();

    private void EnterSidePanelMode()
    {
        if (_fullscreen ||
            _settings.HideGameDetailsPanels ||
            SelectedGamePanel.Visibility != Visibility.Visible)
        {
            return;
        }

        var controls = GetSidePanelControls();
        if (controls.Count == 0)
            return;

        _sidePanelMode = true;
        _controllerToolbarMode = false;
        _tvHeroMode = false;
        _sidePanelActionIndex = Math.Clamp(_sidePanelActionIndex, 0, controls.Count - 1);

        // Ao entrar no painel lateral, o card deixa de ser o contexto ativo.
        // Apenas o botão focado no painel mantém o contorno de seleção.
        UpdateControllerSelection();
        FocusSidePanelControl();
        PlayNavigationSound();
        ControllerStatusText.Text =
            $"{LocalizationService.Translate("Controle conectado")} • {LocalizationService.Translate("Ações do jogo")}";
    }

    private void ExitSidePanelMode()
    {
        _sidePanelMode = false;
        Keyboard.ClearFocus();
        GameList.Focus();
        Keyboard.Focus(GameList);
        UpdateControllerSelection();
        PlayNavigationSound();
    }

    private void MoveSidePanelSelection(int delta)
    {
        var controls = GetSidePanelControls();
        if (controls.Count == 0)
            return;

        var next = Math.Clamp(_sidePanelActionIndex + delta, 0, controls.Count - 1);
        if (next == _sidePanelActionIndex)
            return;

        _sidePanelActionIndex = next;
        FocusSidePanelControl();
        PlayNavigationSound();
    }

    private void FocusSidePanelControl()
    {
        var controls = GetSidePanelControls();
        if (_sidePanelActionIndex < 0 || _sidePanelActionIndex >= controls.Count)
            return;

        controls[_sidePanelActionIndex].Focus();
        Keyboard.Focus(controls[_sidePanelActionIndex]);
    }

    private void ActivateSidePanelControl()
    {
        var controls = GetSidePanelControls();
        if (_sidePanelActionIndex < 0 || _sidePanelActionIndex >= controls.Count)
            return;

        controls[_sidePanelActionIndex].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private void EnterTvHeroMode()
    {
        if (!_fullscreen || _settings.HideGameDetailsPanels)
        {
            EnterToolbarMode();
            return;
        }

        var controls = GetTvHeroControls();
        if (controls.Count == 0)
        {
            EnterToolbarMode();
            return;
        }

        _tvHeroMode = true;
        _controllerToolbarMode = false;

        // O mesmo "cima" usado para entrar no hero pode continuar pressionado por
        // alguns frames. Ignora navegação vertical até o eixo/direcional voltar ao
        // neutro para não saltar imediatamente para a barra superior.
        _suppressHeroVerticalUntilNeutral = true;
        _nextNavigationAllowedUtc = DateTime.UtcNow.AddMilliseconds(180);
        ResetAnalogNavigation();

        _tvHeroActionIndex = Math.Clamp(_tvHeroActionIndex, 0, controls.Count - 1);
        UpdateControllerSelection();
        FocusTvHeroControl();
        PlayNavigationSound();
        ControllerStatusText.Text = $"{LocalizationService.Translate("MODO TV")} • {LocalizationService.Translate("Ações do jogo")}";
    }

    private void ExitTvHeroMode()
    {
        _tvHeroMode = false;
        _suppressHeroVerticalUntilNeutral = false;
        Keyboard.ClearFocus();
        GameList.Focus();
        Keyboard.Focus(GameList);
        UpdateControllerSelection();
        PlayNavigationSound();
    }

    private void MoveTvHeroSelection(int delta)
    {
        var controls = GetTvHeroControls();
        if (controls.Count == 0) return;
        var next = Math.Clamp(_tvHeroActionIndex + delta, 0, controls.Count - 1);
        if (next == _tvHeroActionIndex) return;
        _tvHeroActionIndex = next;
        FocusTvHeroControl();
        PlayNavigationSound();
    }

    private void FocusTvHeroControl()
    {
        var controls = GetTvHeroControls();
        if (_tvHeroActionIndex < 0 || _tvHeroActionIndex >= controls.Count) return;
        controls[_tvHeroActionIndex].Focus();
        Keyboard.Focus(controls[_tvHeroActionIndex]);
    }

    private void ActivateTvHeroControl()
    {
        var controls = GetTvHeroControls();
        if (_tvHeroActionIndex < 0 || _tvHeroActionIndex >= controls.Count) return;
        controls[_tvHeroActionIndex].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

        if (controls[_toolbarIndex] is Slider slider)
        {
            var old = slider.Value;
            var minimum = slider.Minimum;
            var maximum = slider.Maximum;
            var next = Math.Clamp(old + delta, minimum, maximum);

            if (Math.Abs(next - old) > 0.001)
            {
                slider.Value = next;
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
            case Slider:
                // Sliders são ajustados por cima/baixo no teclado/controle e por
                // clique/arraste no mouse. Enter/A não altera o valor acidentalmente.
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
            StatusText.Text = $"{activeGame.Name} já está em execução";
            ShowToast($"Feche {activeGame.Name} antes de iniciar outro jogo.");
            return;
        }

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

        var verticalArtwork = game.VerticalCover;
        GameLaunchCoverImage.Source = LoadHomeArtwork(verticalArtwork);

        GameLaunchOverlay.Visibility = Visibility.Visible;
        GameLaunchOverlay.Opacity = 0;

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
            ApplyWindowMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        else if (msg == WM_DISPLAYCHANGE && _fullscreen)
        {
            Dispatcher.BeginInvoke(new Action(ApplyFullscreenBounds), DispatcherPriority.Background);
        }

        return IntPtr.Zero;
    }

    private void ApplyWindowMinMaxInfo(IntPtr hwnd, IntPtr lParam)
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

        // Como tratamos WM_GETMINMAXINFO manualmente por causa do WindowChrome,
        // o Windows não aplica sozinho MinWidth/MinHeight do WPF. Define o mínimo
        // também no nível nativo, respeitando o DPI do monitor atual.
        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96.0;
        minMaxInfo.ptMinTrackSize.X = Math.Min(
            minMaxInfo.ptMaxTrackSize.X,
            (int)Math.Ceiling(MinWidth * scale));
        minMaxInfo.ptMinTrackSize.Y = Math.Min(
            minMaxInfo.ptMaxTrackSize.Y,
            (int)Math.Ceiling(MinHeight * scale));

        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_DISPLAYCHANGE = 0x007E;

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
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;

            // Remove também as regiões nativas de legenda/redimensionamento do
            // WindowChrome. Sem isso, o Windows ainda pode interpretar alguns
            // cliques nas bordas/topo como arraste mesmo com DragMove bloqueado.
            if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome)
            {
                _previousChromeCaptionHeight = chrome.CaptionHeight;
                _previousChromeResizeBorderThickness = chrome.ResizeBorderThickness;
                chrome.CaptionHeight = 0;
                chrome.ResizeBorderThickness = new Thickness(0);
            }

            Topmost = true;
            ApplyFullscreenBounds();

            // 1.1.5: o mouse permanece visível no modo tela cheia para que todas
            // as ações continuem acessíveis também por ponteiro.
            Cursor = Cursors.Arrow;
        }
        else
        {
            Cursor = Cursors.Arrow;

            // Ao sair do fullscreen, reativa primeiro o comportamento normal de
            // WM_GETMINMAXINFO para que um estado maximizado volte a respeitar a
            // área útil do monitor e a barra de tarefas.
            _fullscreen = false;

            if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome)
            {
                chrome.CaptionHeight = _previousChromeCaptionHeight;
                chrome.ResizeBorderThickness = _previousChromeResizeBorderThickness;
            }

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

    private void ApplyFullscreenBounds()
    {
        if (!_fullscreen)
            return;

        var monitorBounds = GetCurrentMonitorBounds();
        Left = monitorBounds.Left;
        Top = monitorBounds.Top;
        Width = monitorBounds.Width;
        Height = monitorBounds.Height;
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
        HeaderRow.Height = new GridLength(_fullscreen ? 0 : 78);

        TvHeroPanel.Visibility = _fullscreen && !_settings.HideGameDetailsPanels
            ? Visibility.Visible
            : Visibility.Collapsed;
        FullscreenSearchContainer.Visibility = _fullscreen ? Visibility.Visible : Visibility.Collapsed;
        FullscreenRefreshButton.Visibility = _fullscreen ? Visibility.Visible : Visibility.Collapsed;
        FullscreenSettingsButton.Visibility = _fullscreen ? Visibility.Visible : Visibility.Collapsed;
        FullscreenStatisticsButton.Visibility = _fullscreen ? Visibility.Visible : Visibility.Collapsed;
        FullscreenToolbarButton.Visibility = _fullscreen ? Visibility.Visible : Visibility.Collapsed;

        FilterSummaryBorder.Visibility = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
        FilterSummaryRow.Height = new GridLength(_fullscreen ? 0 : 32);

        if (!_fullscreen)
            _tvHeroMode = false;
        else
            _sidePanelMode = false;

        UpdateResponsiveLayout();
        Dispatcher.BeginInvoke(UpdateCoverDimensions, DispatcherPriority.Loaded);
        UpdateSelectedGamePresentation();

        LibraryLoadingText.Visibility = !_fullscreen && _isLibraryLoading
            ? Visibility.Visible
            : Visibility.Collapsed;
        FullscreenLibraryLoadingText.Visibility = Visibility.Collapsed;
        FullscreenLoadingIndicator.Visibility = _fullscreen && _isLibraryLoading
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

        ControllerStatusText.Text = GetControllerStatusText(includeSelectedGame: true);
        UpdateFooterInputHints();
    }

    #endregion

    #region Theme and visual settings

    private void ApplyLanguage()
    {
        LocalizationService.Apply(this);

        // Os gêneros são objetos gerados em tempo de execução, portanto não são
        // atualizados por LocalizationService.Apply. Reconstrói o filtro no idioma
        // atual preservando o gênero canônico selecionado.
        BuildGenreFilter();

        foreach (var item in LibraryFilterCombo.Items.OfType<ComboBoxItem>())
        {
            if (item.Content is string text)
                item.Content = LocalizationService.Translate(text);
        }

        // Alguns textos são gerados em tempo de execução e precisam ser refeitos
        // após uma troca de idioma.
        GameCountText.Text = LocalizedGameCount(_visibleGames.Count);
        UpdateFilterChips();
        UpdateSelectedGamePresentation();
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

        // Superfícies do hero/painel mantêm seus contornos próprios.
        // A cor de seleção do tema é usada somente no foco dos controles.
        var selectedPanelBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#EEF1F5" : "#0A1421");
        var selectedPanelBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#A8B2C0" : "#1E3850");
        var selectedPanelArtworkBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#E2E7ED" : "#07111C");
        var selectedPanelArtworkBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#AAB5C3" : "#1C344C");
        var selectedPanelTextPrimaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#111827" : "#F5F7FA");
        var selectedPanelTextSecondaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#566273" : "#9AAABD");
        var selectedPanelMetaColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#0B638F" : "#82D8FF");
        var selectedPanelDividerColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#BCC5D0" : "#1B3147");

        var tvHeroBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#E7EBF0" : "#07111D");
        var tvHeroTextPrimaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#111827" : "#FFFFFF");
        var tvHeroTextSecondaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#4F5D6E" : "#A9B6C7");
        var tvHeroMetaColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#0B638F" : "#9EDBFF");
        var tvHeroDescriptionColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#263445" : "#D8E0EA");
        var tvHeroPreviewBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#DDE3EA" : "#33101C29");
        var tvHeroPreviewBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#9EABB9" : "#31506D");

        var heroPlayBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#32BDEB" : "#D9153C");
        var heroPlayBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#6EDCFF" : "#FF3158");
        var heroPlayForegroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#07131D" : "#FFFFFF");

        var launchOverlayBackdropColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#BFE9EDF2" : "#D9020509");
        var launchOverlayPanelColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#F8F7F9FB" : "#F20A111B");
        var launchOverlayBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#A7B2C0" : "#31445A");
        var launchOverlayTextPrimaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#101722" : "#FFFFFF");
        var launchOverlayTextSecondaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#566273" : "#AAB7C8");
        var launchOverlayArtworkBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#E5E9EE" : "#07111C");
        var launchOverlayArtworkBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#B3BDC9" : "#26384C");
        var launchProgressTrackColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#CBD2DA" : "#26313D");

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
        SetOrUpdateBrushResource("SelectedPanelBackground", selectedPanelBackgroundColor);
        SetOrUpdateBrushResource("SelectedPanelBorder", selectedPanelBorderColor);
        SetOrUpdateBrushResource("SelectedPanelArtworkBackground", selectedPanelArtworkBackgroundColor);
        SetOrUpdateBrushResource("SelectedPanelArtworkBorder", selectedPanelArtworkBorderColor);
        SetOrUpdateBrushResource("SelectedPanelTextPrimary", selectedPanelTextPrimaryColor);
        SetOrUpdateBrushResource("SelectedPanelTextSecondary", selectedPanelTextSecondaryColor);
        SetOrUpdateBrushResource("SelectedPanelMeta", selectedPanelMetaColor);
        SetOrUpdateBrushResource("SelectedPanelDivider", selectedPanelDividerColor);
        SetOrUpdateBrushResource("TvHeroBackground", tvHeroBackgroundColor);
        SetOrUpdateBrushResource("TvHeroTextPrimary", tvHeroTextPrimaryColor);
        SetOrUpdateBrushResource("TvHeroTextSecondary", tvHeroTextSecondaryColor);
        SetOrUpdateBrushResource("TvHeroMeta", tvHeroMetaColor);
        SetOrUpdateBrushResource("TvHeroDescription", tvHeroDescriptionColor);
        SetOrUpdateBrushResource("TvHeroPreviewBackground", tvHeroPreviewBackgroundColor);
        SetOrUpdateBrushResource("TvHeroPreviewBorder", tvHeroPreviewBorderColor);
        SetOrUpdateBrushResource("LaunchOverlayBackdrop", launchOverlayBackdropColor);
        SetOrUpdateBrushResource("LaunchOverlayPanel", launchOverlayPanelColor);
        SetOrUpdateBrushResource("LaunchOverlayBorder", launchOverlayBorderColor);
        SetOrUpdateBrushResource("LaunchOverlayTextPrimary", launchOverlayTextPrimaryColor);
        SetOrUpdateBrushResource("LaunchOverlayTextSecondary", launchOverlayTextSecondaryColor);
        SetOrUpdateBrushResource("LaunchOverlayArtworkBackground", launchOverlayArtworkBackgroundColor);
        SetOrUpdateBrushResource("LaunchOverlayArtworkBorder", launchOverlayArtworkBorderColor);
        SetOrUpdateBrushResource("LaunchProgressTrack", launchProgressTrackColor);

        var heroPlayBackground = new SolidColorBrush(heroPlayBackgroundColor);
        var heroPlayBorder = new SolidColorBrush(heroPlayBorderColor);
        var heroPlayForeground = new SolidColorBrush(heroPlayForegroundColor);

        SelectedPlayButton.Background = heroPlayBackground;
        SelectedPlayButton.BorderBrush = heroPlayBorder;
        SelectedPlayButton.Foreground = heroPlayForeground;

        TvPlayButton.Background = new SolidColorBrush(heroPlayBackgroundColor);
        TvPlayButton.BorderBrush = new SolidColorBrush(heroPlayBorderColor);
        TvPlayButton.Foreground = new SolidColorBrush(heroPlayForegroundColor);

        TvHeroArtwork.Opacity = lightTheme ? 0.18 : 0.34;
        TvHeroTint.Background = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                lightTheme ? "#A8EEF1F5" : "#B807101B"));
        TvHeroGradientStart.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#F5EEF1F5" : "#F0060B12");
        TvHeroGradientMiddle.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#D8EEF1F5" : "#D00A1420");
        TvHeroGradientEnd.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#A8EEF1F5" : "#900A1420");

        UpdateResponsiveLayout();
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

    private static bool MatchesShortcut(System.Windows.Input.KeyEventArgs e, string? shortcut) =>
        ShortcutGestureService.Matches(e, shortcut);

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (MatchesShortcut(e, _settings.Shortcuts.FocusSearch))
        {
            var targetSearch = _fullscreen ? FullscreenSearchBox : SearchBox;
            targetSearch.Focus();
            targetSearch.SelectAll();
            e.Handled = true;
            return;
        }
        if (MatchesShortcut(e, _settings.Shortcuts.ToggleFullscreen))
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        if (MatchesShortcut(e, _settings.Shortcuts.RefreshLibrary))
        {
            _ = RefreshLibrary("ATUALIZANDO BIBLIOTECA...", forceArtworkRefresh: true);
            e.Handled = true;
            return;
        }
        if (MatchesShortcut(e, _settings.Shortcuts.OpenSettings))
        {
            Settings_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        // Qualquer uso do teclado torna as dicas Enter/Espaço as dicas ativas.
        SetFooterInputMode(FooterInputMode.Keyboard);

        // Equivalentes de teclado para os atalhos globais dos controles.
        // Page Up/Down = LB/RB (coleções); Ctrl+Page Up/Down = LT/RT (plataformas).
        if (e.Key is Key.PageUp or Key.PageDown)
        {
            var delta = e.Key == Key.PageUp ? -1 : 1;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                CycleComboSelection(PlatformFilterCombo, delta);
            else
                CycleComboSelection(LibraryFilterCombo, delta);

            e.Handled = true;
            return;
        }

        // Tab assume o papel de Back/Select nas zonas de jogo:
        // biblioteca -> painel lateral/hero; painel/hero -> biblioteca.
        if (e.Key == Key.Tab)
        {
            if (_sidePanelMode && !_fullscreen)
            {
                ExitSidePanelMode();
                e.Handled = true;
                return;
            }

            if (_tvHeroMode && _fullscreen)
            {
                ExitTvHeroMode();
                e.Handled = true;
                return;
            }

            if (!_controllerToolbarMode &&
                Keyboard.FocusedElement is not TextBox &&
                _visibleGames.Count > 0)
            {
                if (_fullscreen)
                    EnterTvHeroMode();
                else if (SelectedGamePanel.Visibility == Visibility.Visible &&
                         !_settings.HideGameDetailsPanels)
                    EnterSidePanelMode();

                e.Handled = true;
                return;
            }
        }

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
                    e.Handled = true;
                    return;
                case Key.Right when _fullscreen &&
                                        ReferenceEquals(focusedTextBox, FullscreenSearchBox) &&
                                        focusedTextBox.SelectionLength == 0 &&
                                        focusedTextBox.SelectionStart >= focusedTextBox.Text.Length:
                    // No fim da pesquisa em tela cheia, a seta direita continua
                    // a navegação da barra e leva ao botão de sair da tela cheia.
                    _controllerToolbarMode = true;
                    var rightControls = GetToolbarControls();
                    var currentSearchIndex = rightControls
                        .Select((control, index) => new { control, index })
                        .FirstOrDefault(item => ReferenceEquals(item.control, FullscreenSearchBox))
                        ?.index ?? -1;
                    if (currentSearchIndex >= 0 && currentSearchIndex + 1 < rightControls.Count)
                    {
                        _toolbarIndex = currentSearchIndex + 1;
                        FocusToolbarControl();
                        PlayNavigationSound();
                        e.Handled = true;
                    }
                    return;
                case Key.Left when _fullscreen &&
                                       ReferenceEquals(focusedTextBox, FullscreenSearchBox) &&
                                       focusedTextBox.SelectionLength == 0 &&
                                       focusedTextBox.SelectionStart == 0:
                    // No início da pesquisa, a seta esquerda volta ao item anterior
                    // da barra. Dentro do texto, esquerda/direita seguem editando.
                    _controllerToolbarMode = true;
                    var leftControls = GetToolbarControls();
                    var searchIndex = leftControls
                        .Select((control, index) => new { control, index })
                        .FirstOrDefault(item => ReferenceEquals(item.control, FullscreenSearchBox))
                        ?.index ?? -1;
                    if (searchIndex > 0)
                    {
                        _toolbarIndex = searchIndex;
                        MoveToolbarSelection(-1);
                        e.Handled = true;
                    }
                    return;
                // Enquanto houver texto para percorrer, esquerda/direita, Home/End,
                // Backspace/Delete e caracteres continuam disponíveis para edição.
                default:
                    return;
            }
        }

        if (_sidePanelMode && !_fullscreen)
        {
            switch (e.Key)
            {
                case Key.Up:
                    MoveSidePanelSelection(-1);
                    e.Handled = true;
                    return;
                case Key.Down:
                    MoveSidePanelSelection(1);
                    e.Handled = true;
                    return;
                case Key.Left:
                case Key.Escape:
                    ExitSidePanelMode();
                    e.Handled = true;
                    return;
                case Key.Enter:
                    ActivateSidePanelControl();
                    e.Handled = true;
                    return;
                case Key.Space:
                    OpenSelectedDetails();
                    e.Handled = true;
                    return;
                case Key.F:
                    ToggleSelectedFavorite();
                    e.Handled = true;
                    return;
            }
        }

        if (_tvHeroMode && _fullscreen)
        {
            switch (e.Key)
            {
                case Key.Left:
                    MoveTvHeroSelection(-1);
                    e.Handled = true;
                    return;
                case Key.Right:
                    MoveTvHeroSelection(1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    _tvHeroMode = false;
                    EnterToolbarMode();
                    e.Handled = true;
                    return;
                case Key.Down:
                    ExitTvHeroMode();
                    e.Handled = true;
                    return;
                case Key.Escape:
                    ToggleFullscreen();
                    e.Handled = true;
                    return;
                case Key.Enter:
                    ActivateTvHeroControl();
                    e.Handled = true;
                    return;
                case Key.Space:
                    OpenSelectedDetails();
                    e.Handled = true;
                    return;
                case Key.F:
                    ToggleSelectedFavorite();
                    e.Handled = true;
                    return;
            }
        }

        // Quando a barra está ativa, o teclado usa a mesma navegação do controle.
        if (_controllerToolbarMode)
        {
            switch (e.Key)
            {
                case Key.Left:
                    MoveToolbarSelection(-1);
                    e.Handled = true;
                    return;
                case Key.Right:
                    MoveToolbarSelection(1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    AdjustToolbarValue(-1);
                    e.Handled = true;
                    return;
                case Key.Down:
                    AdjustToolbarValue(1);
                    e.Handled = true;
                    return;
                case Key.Enter:
                    ActivateToolbarControl();
                    e.Handled = true;
                    return;
                case Key.Escape:
                    ExitToolbarMode();
                    e.Handled = true;
                    return;
            }
        }

        if (_visibleGames.Count == 0)
        {
            if (e.Key == Key.Up)
            {
                EnterToolbarMode();
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Right:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                if (_selectedIndex < GetColumns())
                    EnterToolbarMode();
                else
                    MoveSelection(-GetColumns());
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(GetColumns());
                e.Handled = true;
                break;
            case Key.Enter:
                _ = LaunchSelectedAsync();
                e.Handled = true;
                break;
            case Key.Space:
                OpenSelectedDetails();
                e.Handled = true;
                break;
            case Key.F:
                ToggleSelectedFavorite();
                e.Handled = true;
                break;
        }
    }
    #endregion
}

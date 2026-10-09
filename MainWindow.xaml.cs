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
        Activated += (_, _) =>
        {
            if (_fullscreen)
                Topmost = false;
        };
        Deactivated += (_, _) =>
        {
            if (_fullscreen)
                Topmost = false;
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

}

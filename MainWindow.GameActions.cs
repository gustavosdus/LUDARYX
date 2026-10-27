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
        if (_visibleGames.Count == 0)
            return;
        var previousIndex = _selectedIndex;
        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _visibleGames.Count - 1);
        if (_selectedIndex == previousIndex)
            return;

        PlayNavigationSound();
        UpdateControllerSelection();

        // IsControllerSelected notifica apenas os cards afetados. Não fazemos mais
        // Items.Refresh() a cada passo, pois isso reconstruía/atualizava a lista
        // inteira e fazia a rolagem ficar para trás durante navegação contínua.
        ScrollSelectedIntoView();
    }

    private void ScrollSelectedIntoView()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _visibleGames.Count)
            return;

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
            if (_selectedIndex < 0 || _selectedIndex >= _visibleGames.Count)
                return;
            if (GameList.ItemContainerGenerator.ContainerFromIndex(_selectedIndex) is FrameworkElement generated)
                generated.BringIntoView();
        }, DispatcherPriority.Input);
    }

    private async Task LaunchSelectedAsync()
    {
        if (_visibleGames.Count == 0)
            return;
        await LaunchGameAsync(_visibleGames[_selectedIndex]);
    }

    private void OpenSelectedDetails()
    {
        if (_visibleGames.Count > 0)
            OpenDetails(_visibleGames[_selectedIndex]);
    }

    private void ToggleSelectedFavorite()
    {
        if (_visibleGames.Count == 0)
            return;
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
        if (sender is Button b && b.Tag is Game game)
        {
            _state.ToggleFavorite(game, _settings);
            ApplyFilter();
        }
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Game game)
            await LaunchGameAsync(game);
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
            StatusText.Text = $"{game.DisplayName} já está em execução";
            ShowToast($"{game.DisplayName} já está em execução.");
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
                StatusText.Text = $"Mantendo sessão atual: {activeGame.DisplayName}";
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
            StatusText.Text = $"Iniciando {game.DisplayName}...";
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
                StatusText.Text = $"Executando: {game.DisplayName}";
                ApplyFilter();

                // Mantém 100% por um instante para tornar a conclusão perceptível.
                await Task.Delay(320, CancellationToken.None);
            }
            else
            {
                // Alguns launchers/jogos não expõem um executável detectável.
                // A barra permanece quase completa, mas não afirma 100% sem confirmação.
                SetGameLaunchProgress(95);
                StatusText.Text = $"Inicialização enviada: {game.DisplayName}";
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
            LudaryxMessageBox.Show(
                LocalizationService.Format("Não foi possível iniciar {0}.\n\n{1}", game.DisplayName, ex.Message),
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
        GameLaunchNameText.Text = game.DisplayName;
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

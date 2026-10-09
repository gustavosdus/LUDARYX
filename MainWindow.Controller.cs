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

        if (GameLaunchOverlay.Visibility == Visibility.Visible)
        {
            if (_gamepad.WasPressed(GamepadButtons.A, state) ||
                _gamepad.WasPressed(GamepadButtons.B, state))
            {
                CancelGameLaunch_Click(CancelGameLaunchButton, new RoutedEventArgs());
            }
            return;
        }

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

        if (GameLaunchOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                CancelGameLaunch_Click(CancelGameLaunchButton, new RoutedEventArgs());
                e.Handled = true;
            }
            return;
        }

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

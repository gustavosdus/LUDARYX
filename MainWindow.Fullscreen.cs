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

            // Fullscreen deve ocupar o monitor inteiro, mas não pode ser
            // "sempre no topo": jogos precisam conseguir assumir a frente normalmente.
            Topmost = false;
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
}

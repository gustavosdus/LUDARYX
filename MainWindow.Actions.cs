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
}

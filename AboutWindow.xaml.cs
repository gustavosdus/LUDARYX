using System.Windows;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        LudaryxThemeService.Apply(new JsonSettingsService().Load());
        LocalizationService.Apply(this);
        DiagnosticsText.Text = DiagnosticLogService.GetSystemSummary();
        VersionText.Text = $"{LocalizationService.Translate("Versão")} {GitHubUpdateService.GetCurrentVersionDisplay()}";
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DiagnosticLogService.OpenLogDirectory();
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException("Could not open log directory", ex);
            MessageBox.Show(ex.Message, "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(DiagnosticLogService.BuildDiagnosticReport());
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException("Could not copy diagnostic summary", ex);
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await GitHubUpdateService.CheckForUpdatesAsync();
            if (!result.IsUpdateAvailable)
            {
                MessageBox.Show(
                    this,
                    $"Nenhuma atualização foi encontrada.\n\nVersão atual: {result.CurrentVersionDisplay}",
                    "LUDARYX",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var shouldClose = await GitHubUpdateService.PromptAndDownloadUpdateAsync(this, result, CancellationToken.None);
            if (shouldClose)
            {
                System.Windows.Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException("Could not check for updates from About window", ex);
            MessageBox.Show(ex.Message, "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
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
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

}

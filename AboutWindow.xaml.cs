using System.Windows;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        DiagnosticsText.Text = DiagnosticLogService.GetSystemSummary();
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
            System.Windows.Clipboard.SetText(DiagnosticLogService.GetSystemSummary());
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException("Could not copy diagnostic summary", ex);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

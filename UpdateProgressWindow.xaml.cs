using System.Windows;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class UpdateProgressWindow : Window
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public UpdateProgressWindow()
    {
        InitializeComponent();
        LocalizationService.Apply(this);
    }

    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    public void Report(UpdateDownloadProgress progress)
    {
        StatusText.Text = progress.Status;
        ProgressText.Text = progress.DisplayText;

        if (progress.Percent is double percent)
        {
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = Math.Clamp(percent, 0, 100);
        }
        else
        {
            DownloadProgressBar.IsIndeterminate = true;
        }
    }

    public void SetCancelling()
    {
        CancelButton.IsEnabled = false;
        StatusText.Text = LocalizationService.Translate("Cancelando...");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        SetCancelling();
        _cancellationTokenSource.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_cancellationTokenSource.IsCancellationRequested)
            _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        base.OnClosed(e);
    }
}

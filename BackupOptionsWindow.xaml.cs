using System.Windows;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class BackupOptionsWindow : Window
{
    public BackupSelection Selection { get; private set; } = new();

    public BackupOptionsWindow()
    {
        InitializeComponent();
        SettingsCheck.Checked += SelectionChanged;
        SettingsCheck.Unchecked += SelectionChanged;
        ArtworkCheck.Checked += SelectionChanged;
        ArtworkCheck.Unchecked += SelectionChanged;
        MetadataCheck.Checked += SelectionChanged;
        MetadataCheck.Unchecked += SelectionChanged;
        Loaded += (_, _) =>
        {
            LocalizationService.Apply(this);
            RefreshEstimate();
        };
    }

    private void SelectionChanged(object sender, RoutedEventArgs e) => RefreshEstimate();

    private BackupSelection ReadSelection() => new()
    {
        SettingsAndLibrary = SettingsCheck.IsChecked == true,
        CustomArtwork = ArtworkCheck.IsChecked == true,
        MetadataCache = MetadataCheck.IsChecked == true
    };

    private void RefreshEstimate()
    {
        var selection = ReadSelection();
        var bytes = BackupService.GetEstimatedBackupSourceSizeBytes(selection);
        EstimatedSizeText.Text = $"Dados de origem estimados: {bytes / 1024d / 1024d:0.0} MB";
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        Selection = ReadSelection();
        if (!Selection.HasAny)
        {
            MessageBox.Show(this, "Selecione pelo menos uma categoria.", "LUDARYX",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

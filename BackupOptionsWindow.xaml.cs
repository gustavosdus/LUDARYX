using System.Windows;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class BackupOptionsWindow : Window
{
    private readonly bool _importMode;
    private readonly string? _sourceZip;

    public BackupSelection Selection { get; private set; } = new();

    public BackupOptionsWindow(bool importMode = false, string? sourceZip = null)
    {
        InitializeComponent();
        LudaryxThemeService.Apply(new JsonSettingsService().Load());
        _importMode = importMode;
        _sourceZip = sourceZip;

        if (_importMode)
        {
            Title = "Restaurar backup";
            HeaderText.Text = "RESTAURAR BACKUP";
            DescriptionText.Text = "Escolha quais categorias deseja restaurar deste backup.";
            ContinueButton.Content = "RESTAURAR";
        }

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
        var bytes = _importMode && !string.IsNullOrWhiteSpace(_sourceZip)
            ? BackupService.GetEstimatedImportSizeBytes(_sourceZip, selection)
            : BackupService.GetEstimatedBackupSourceSizeBytes(selection);

        EstimatedSizeText.Text = _importMode
            ? $"Dados selecionados para restauração: {bytes / 1024d / 1024d:0.0} MB"
            : $"Dados de origem estimados: {bytes / 1024d / 1024d:0.0} MB";
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
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => DialogResult = false;

}

using System.Windows;
using System.Windows.Input;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class SteamGridDbArtworkPickerWindow : Window
{
    private readonly Game _game;
    private readonly LauncherSettings _settings;
    private readonly SteamGridDbService _service;
    private readonly bool _vertical;
    private readonly int _gameId;

    public bool ArtworkChanged { get; private set; }

    public SteamGridDbArtworkPickerWindow(
        Game game,
        LauncherSettings settings,
        SteamGridDbService service,
        bool vertical,
        int gameId)
    {
        InitializeComponent();
        _game = game;
        _settings = settings;
        _service = service;
        _vertical = vertical;
        _gameId = gameId;
        HeaderText.Text = vertical ? "ESCOLHER CAPA VERTICAL" : "ESCOLHER ARTE HORIZONTAL";
        InfoText.Text = vertical
            ? $"{game.Name} • apenas artes 600×900"
            : $"{game.Name} • artes 920×430 ou 460×215";
        LocalizationService.Apply(this);
        Loaded += async (_, _) =>
        {
            LocalizationService.Apply(this);
            await LoadArtworkAsync();
            LocalizationService.Apply(this);
        };
    }

    private async Task LoadArtworkAsync()
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var options = await _service.GetArtworkOptionsAsync(_gameId, _settings.SteamGridDbApiKey!, _vertical);
            ArtworkList.ItemsSource = options;
            if (options.Count == 0)
            {
                MessageBox.Show(
                    "Nenhuma arte compatível foi encontrada para este jogo.\n\nVocê pode tentar corrigir o jogo do SteamGridDB e pesquisar novamente.",
                    "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void ArtworkList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UseSelectedButton.IsEnabled = ArtworkList.SelectedItem is SteamGridDbArtworkOption;
    }

    private void ArtworkImage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Image image && image.DataContext is SteamGridDbArtworkOption artwork)
        {
            ArtworkList.SelectedItem = artwork;
            e.Handled = true;
        }
    }

    private async void Use_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is SteamGridDbArtworkOption artwork)
            await UseArtworkAsync(artwork);
    }

    private async void UseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ArtworkList.SelectedItem is SteamGridDbArtworkOption artwork)
            await UseArtworkAsync(artwork);
    }

    private async Task UseArtworkAsync(SteamGridDbArtworkOption artwork)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            await _service.DownloadArtworkAsync(_game, _settings.SteamGridDbApiKey!, artwork, _vertical);
            ArtworkChanged = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}

using System.Windows;
using System.Windows.Input;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class SteamGridDbGamePickerWindow : Window
{
    private readonly SteamGridDbService _service;
    private readonly string _apiKey;
    public SteamGridDbGameResult? SelectedGame { get; private set; }

    public SteamGridDbGamePickerWindow(string initialQuery, string apiKey, SteamGridDbService service)
    {
        InitializeComponent();
        LudaryxThemeService.Apply(new JsonSettingsService().Load());
        _service = service;
        _apiKey = apiKey;
        LocalizationService.Apply(this);
        SearchBox.Text = initialQuery;
        Loaded += async (_, _) =>
        {
            LocalizationService.Apply(this);
            await SearchAsync();
            LocalizationService.Apply(this);
        };
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            ResultsList.ItemsSource = await _service.SearchGamesAsync(SearchBox.Text, _apiKey);
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

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not SteamGridDbGameResult game)
        {
            MessageBox.Show("Selecione um jogo da lista.", "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedGame = game;
        DialogResult = true;
        Close();
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Use_Click(sender, e);
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}

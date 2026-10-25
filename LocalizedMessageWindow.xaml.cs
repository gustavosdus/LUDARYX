using System.Windows;
using System.Windows.Input;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class LocalizedMessageWindow : Window
{
    private readonly bool _confirmation;

    private LocalizedMessageWindow(Window? owner, string message, bool confirmation)
    {
        InitializeComponent();
        _confirmation = confirmation;

        var settings = new JsonSettingsService().Load();
        LocalizationService.SetLanguage(settings.Language);
        LudaryxThemeService.Apply(settings);

        Owner = owner;
        MessageText.Text = message;

        if (confirmation)
        {
            SecondaryButton.Visibility = Visibility.Visible;
            SecondaryButton.Content = LocalizationService.Translate("Não");
            PrimaryButton.Content = LocalizationService.Translate("Sim");
        }
        else
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
            PrimaryButton.Content = "OK";
        }
    }

    public static void ShowInfo(Window? owner, string message)
    {
        var window = new LocalizedMessageWindow(owner, message, confirmation: false);
        window.ShowDialog();
    }

    public static bool ShowConfirm(Window? owner, string message)
    {
        var window = new LocalizedMessageWindow(owner, message, confirmation: true);
        return window.ShowDialog() == true;
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _confirmation ? false : true;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}

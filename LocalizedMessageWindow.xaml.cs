using System.Windows;
using System.Windows.Input;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class LocalizedMessageWindow : Window
{
    private readonly MessageBoxButton _buttons;
    public MessageBoxResult Result { get; private set; }

    internal LocalizedMessageWindow(Window? owner, string message, string caption,
        MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        _buttons = buttons;
        Result = DismissResult(buttons);
        if (owner is { IsVisible: true })
            Owner = owner;
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = CaptionText.Text = LocalizationService.Translate(caption);
        MessageText.Text = LocalizationService.Translate(message);
        PrimaryButton.Content = LocalizationService.Translate(buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? "Sim" : "OK");
        SecondaryButton.Content = LocalizationService.Translate("Não");
        SecondaryButton.Visibility = buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Content = LocalizationService.Translate("Cancelar");
        CancelButton.Visibility = buttons is MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.IsDefault = buttons == MessageBoxButton.OK;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        Loaded += (_, _) =>
        {
            WindowPlacementService.FitToWorkingArea(this, Owner, margin: 16, center: true);
            if (SecondaryButton.IsVisible) SecondaryButton.Focus();
            else if (CancelButton.IsVisible) CancelButton.Focus();
            else PrimaryButton.Focus();
        };
        _ = new WindowGamepadNavigationService(this, Close);
    }

    public static MessageBoxResult DismissResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.YesNo => MessageBoxResult.No,
        MessageBoxButton.OK => MessageBoxResult.OK,
        MessageBoxButton.YesNoCancel => MessageBoxResult.None,
        _ => MessageBoxResult.Cancel
    };

    public static void ShowInfo(Window? owner, string message) =>
        LudaryxMessageBox.Show(owner, message, "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool ShowConfirm(Window? owner, string message) =>
        LudaryxMessageBox.Show(owner, message, "LUDARYX", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Result = _buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK;
        Close();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e) { Result = MessageBoxResult.No; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { Result = MessageBoxResult.Cancel; Close(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}

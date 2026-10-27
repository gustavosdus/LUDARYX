using System.Windows;

namespace UnifiedGameLauncher.Services;

/// <summary>Shared themed modal messages; never reloads or changes the active language/theme.</summary>
public static class LudaryxMessageBox
{
    public static MessageBoxResult ShowError(Exception exception, string caption = "LUDARYX",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Error) =>
        ShowError(null, exception, caption, buttons, image);

    public static MessageBoxResult ShowError(Window? owner, Exception exception, string caption = "LUDARYX",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Error)
    {
        DiagnosticLogService.LogException(caption, exception);
        return Show(owner, LocalizationService.Translate("Não foi possível concluir esta operação. Tente novamente."),
            caption, buttons, image);
    }

    public static MessageBoxResult Show(string message, string caption = "LUDARYX",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None) =>
        Show(null, message, caption, buttons, image);

    public static MessageBoxResult Show(Window? owner, string message, string caption = "LUDARYX",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        if (System.Windows.Application.Current is { } app && !app.Dispatcher.CheckAccess())
            return app.Dispatcher.Invoke(() => Show(owner, message, caption, buttons, image));
        owner ??= System.Windows.Application.Current?.Windows.Cast<Window>().FirstOrDefault(window => window.IsActive);
        var window = new LocalizedMessageWindow(owner, message, caption, buttons, image);
        window.ShowDialog();
        return window.Result;
    }
}

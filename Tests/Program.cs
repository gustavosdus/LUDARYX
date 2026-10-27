using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            CheckCatalog();
            CheckLanguageSwitching();
            MetadataRegression.RunAsync().GetAwaiter().GetResult();
            CheckGeneratedControlText();
            CheckLegacySettings();
            CheckThemedDialogs();
            Console.WriteLine("Localization, WPF control updates and legacy settings checks passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void CheckCatalog()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var columns = new[] { "PtBr", "PtPt", "EnUs", "EnGb", "Es419", "EsEs", "JaJp" };
        foreach (var fieldName in new[] { "SupplementalTranslations", "Translations" })
        {
            var field = typeof(LocalizationService).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (var entry in (IEnumerable)field.GetValue(null)!)
            {
                string Value(string column) => (string)entry.GetType().GetProperty(column)!.GetValue(entry)!;
                var source = Value("PtBr");
                Require(keys.Add(source), $"Duplicate source text: {source}");
                var placeholders = Placeholders(source);
                foreach (var column in columns)
                {
                    var value = Value(column);
                    Require(!string.IsNullOrWhiteSpace(value), $"Missing {column}: {source}");
                    Require(placeholders.SequenceEqual(Placeholders(value)), $"Changed placeholders in {column}: {source}");
                }
            }
        }
        Require(keys.Count > 400, "The translation catalog is unexpectedly incomplete.");
    }

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, @"\{\d+(?:[^{}]*)\}")
            .Select(match => match.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static void CheckLanguageSwitching()
    {
        var languages = LocalizationService.SupportedLanguages;
        var neverPlayed = new[] { "Nunca jogado", "Nunca jogado", "Never played", "Never played", "Nunca jugado", "Nunca jugado", "未プレイ" };
        var details = new[] { "DETALHES", "DETALHES", "DETAILS", "DETAILS", "DETALLES", "DETALLES", "詳細" };
        for (var index = 0; index < languages.Count; index++)
        {
            LocalizationService.SetLanguage(languages[index].Code);
            Require(LocalizationService.FormatLastPlayed(null) == neverPlayed[index], "Last-played localization failed.");
            Require(LocalizationService.Translate("DETALHES") == details[index], "Details localization failed.");
            Require(LocalizationService.Translate("詳細") == details[index], "Switching from Japanese failed.");
            Require(LocalizationService.Translate("LUDARYX") == "LUDARYX", "Product name was translated.");
        }
    }

    private static void CheckGeneratedControlText()
    {
        LocalizationService.SetLanguage(LocalizationService.EnglishUnitedStates);
        var combo = new ComboBox();
        combo.Items.Add(new ComboBoxItem { Content = "TODOS" });
        combo.Items.Add(new ComboBoxItem { Content = "DUPLICATA" });
        combo.SelectedIndex = 0;
        var button = new Button { Content = "↕" };
        var boundText = new TextBlock();
        BindingOperations.SetBinding(boundText, TextBlock.TextProperty, new Binding("Content") { Source = button });
        var panel = new StackPanel();
        panel.Children.Add(combo);
        panel.Children.Add(button);
        panel.Children.Add(boundText);
        var window = new Window { Content = panel, Width = 300, Height = 150, ShowInTaskbar = false };
        try
        {
            window.Show();
            window.UpdateLayout();
            LocalizationService.Apply(window);
            combo.SelectedIndex = 1;
            button.Content = "↑";
            window.UpdateLayout();
            Require(VisualTexts(combo).Contains("DUPLICATE"), "ComboBox selection text stopped updating.");
            Require(VisualTexts(button).Contains("↑"), "Ascending glyph stopped updating.");
            button.Content = "↓";
            window.UpdateLayout();
            Require(VisualTexts(button).Contains("↓"), "Descending glyph stopped updating.");
            Require(BindingOperations.IsDataBound(boundText, TextBlock.TextProperty), "Localization removed a binding.");
            Require(boundText.Text == "↓", "Bound text stopped updating.");
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<string> VisualTexts(DependencyObject root)
    {
        if (root is TextBlock text)
            yield return text.Text;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var value in VisualTexts(VisualTreeHelper.GetChild(root, index)))
                yield return value;
        }
    }

    private static void CheckThemedDialogs()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/LUDARYX;component/Themes/LudaryxControls.xaml", UriKind.Relative) });
        const string message = "Nenhuma arte compatível foi encontrada para este jogo.\n\nVocê pode tentar corrigir o jogo do SteamGridDB e pesquisar novamente.";
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var language in LocalizationService.SupportedLanguages)
        {
            LocalizationService.SetLanguage(language.Code);
            LudaryxThemeService.Apply(new LauncherSettings { Theme = theme, Language = language.Code });
            var window = (UnifiedGameLauncher.LocalizedMessageWindow)Activator.CreateInstance(
                typeof(UnifiedGameLauncher.LocalizedMessageWindow), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object?[] { null, message, "SteamGridDB", MessageBoxButton.YesNoCancel, MessageBoxImage.Information }, null)!;
            try
            {
                Require(window.WindowStyle == WindowStyle.None, "A dialog uses the Windows title bar.");
                Require(LocalizationService.CurrentLanguage == language.Code, "Opening a dialog reset the active language.");
                var text = (TextBlock)window.FindName("MessageText");
                Require(text.Text == LocalizationService.Translate(message), "Dialog body was not localized.");
                if (!language.Code.StartsWith("pt")) Require(text.Text != message, "Missing dialog translation.");
                Require(((Button)window.FindName("CancelButton")).Visibility == Visibility.Visible, "Three-choice dialog lost Cancel.");
                Require(window.Result == MessageBoxResult.None, "Dismissing update prompt opens GitHub or accepts update.");
                var primary = (Button)window.FindName("PrimaryButton");
                primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(window.Result == MessageBoxResult.Yes, "Confirm result changed.");
            }
            finally { window.Close(); }
        }
        Require(UnifiedGameLauncher.LocalizedMessageWindow.DismissResult(MessageBoxButton.YesNo) == MessageBoxResult.No,
            "Closing confirmation must not accept it.");
        Require(UnifiedGameLauncher.LocalizedMessageWindow.DismissResult(MessageBoxButton.OKCancel) == MessageBoxResult.Cancel,
            "Closing OK/Cancel must cancel.");
    }

    private static void CheckLegacySettings()
    {
        var settings = JsonSerializer.Deserialize<LauncherSettings>(
            "{\"ShowStoreApps\":false,\"ShowOnlyPrimaryDuplicates\":true,\"Language\":\"ja-JP\"}")!;
        Require(settings.ShowOnlyPrimaryDuplicates, "Legacy duplicate preference was lost before migration.");
        Require(settings.Language == "ja-JP", "Unknown legacy fields prevented reading settings.");
        Require(!JsonSerializer.Serialize(settings).Contains("ShowStoreApps"), "Obsolete Store toggle is still written.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class ConcurrentLaunchWindow : Window
{
    private readonly WindowGamepadNavigationService _controllerNavigation;

    public bool AllowConcurrentLaunch { get; private set; }

    public ConcurrentLaunchWindow(Game activeGame, Game requestedGame, LauncherSettings settings)
    {
        InitializeComponent();

        var light = string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        SetBrush("PanelBg", light ? "#F1F4F7" : "#0A111B");
        SetBrush("PanelBorder", light ? "#AAB5C2" : "#2A3A4E");
        SetBrush("PrimaryText", light ? "#111827" : "#FFFFFF");
        SetBrush("SecondaryText", light ? "#566273" : "#A7B3C4");
        SetBrush("Accent", light ? "#FF163D" : "#66F5FF");
        SetBrush("ButtonBg", light ? "#E7EBF0" : "#101B2A");
        SetBrush("ButtonHoverBg", light ? "#DDE3EA" : "#172A3D");
        SetBrush("ButtonPressedBg", light ? "#CDD5DE" : "#0D1622");
        SetBrush("PrimaryButtonBg", light ? "#BDEBFF" : "#153247");
        SetBrush("PrimaryButtonHoverBg", light ? "#A4E2FC" : "#1A405A");
        SetBrush("WarningPanelBg", light ? "#FFF8FAFC" : "#16000000");

        TitleText.Text = requestedGame.Name;
        CurrentGameText.Text = $"Em execução agora: {activeGame.Name}";

        _controllerNavigation = new WindowGamepadNavigationService(
            this,
            KeepCurrent,
            focusControls: new System.Windows.Controls.Control[]
            {
                KeepCurrentButton,
                OpenBothButton
            });

        Loaded += (_, _) =>
        {
            KeepCurrentButton.Focus();
            Keyboard.Focus(KeepCurrentButton);
        };
    }

    private void SetBrush(string key, string color) =>
        Resources[key] = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private void KeepCurrent_Click(object sender, RoutedEventArgs e) => KeepCurrent();

    private void OpenBoth_Click(object sender, RoutedEventArgs e)
    {
        AllowConcurrentLaunch = true;
        DialogResult = true;
        Close();
    }

    private void KeepCurrent()
    {
        AllowConcurrentLaunch = false;
        DialogResult = false;
        Close();
    }
}

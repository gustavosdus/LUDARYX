using System.Windows;
using System.Windows.Media;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Paleta global do LUDARYX 1.2.0. Mantém janelas principais e secundárias
/// visualmente consistentes nos temas claro e escuro.
/// </summary>
public static class LudaryxThemeService
{
    public static bool IsLight(LauncherSettings settings) =>
        string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);

    public static void Apply(LauncherSettings settings)
    {
        var light = IsLight(settings);

        Set("LudaryxWindowBackground", light ? "#EEF2F6" : "#070B12");
        Set("LudaryxPanelBackground", light ? "#F7F9FB" : "#0B1320");
        Set("LudaryxPanelBackgroundAlt", light ? "#E7ECF2" : "#0E1826");
        Set("LudaryxInputBackground", light ? "#FFFFFF" : "#0D1724");
        Set("LudaryxBorder", light ? "#AAB5C2" : "#233A55");
        Set("LudaryxBorderHover", light ? "#FF163D" : "#3C6B93");
        Set("LudaryxTextPrimary", light ? "#111827" : "#F2F6FC");
        Set("LudaryxTextSecondary", light ? "#566273" : "#95A6BB");
        Set("LudaryxDisabled", light ? "#8A94A3" : "#536174");

        Set("LudaryxButtonBackground", light ? "#E1E6EC" : "#111D2C");
        Set("LudaryxButtonHoverBackground", light ? "#D3DAE3" : "#17273A");
        Set("LudaryxButtonPressedBackground", light ? "#C3CCD7" : "#1B324A");
        Set("LudaryxButtonForeground", light ? "#111827" : "#F5F7FA");

        Set("LudaryxTitleBarBackground", light ? "#D7DCE3" : "#070A10");
        Set("LudaryxTitleBarBorder", light ? "#B6C0CB" : "#1A2635");
        Set("LudaryxTitleBarSecondaryText", light ? "#5C6878" : "#7F91A8");

        Set("LudaryxListBackground", light ? "#E4E9EF" : "#151923");
        Set("LudaryxItemBackground", light ? "#F8FAFC" : "#1A1F2A");
        Set("LudaryxItemHoverBackground", light ? "#E8EDF3" : "#202836");
        Set("LudaryxBadgeBackground", light ? "#E5EAF0" : "#0D1D2E");
        Set("LudaryxBadgeForeground", light ? "#17364A" : "#BFEAFF");

        Set("LudaryxFooterChipBackground", light ? "#E1E6EC" : "#07121F");
        Set("LudaryxFooterChipBorder", light ? "#9EABB9" : "#203A55");
        Set("LudaryxFooterChipText", light ? "#111827" : "#F3F7FB");

        Set("SelectionAccentBorder", light ? "#FF163D" : "#66F5FF");
        Set("SearchCaretBrush", light ? "#000000" : "#FFFFFF");
    }

    private static void Set(string key, string color)
    {
        if (Application.Current is null)
            return;

        Application.Current.Resources[key] = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(color));
    }
}

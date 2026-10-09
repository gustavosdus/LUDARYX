using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;

namespace UnifiedGameLauncher;

public partial class MainWindow
{
    #region Theme and visual settings

    private void ApplyLanguage()
    {
        LocalizationService.Apply(this);

        // Os gêneros são objetos gerados em tempo de execução, portanto não são
        // atualizados por LocalizationService.Apply. Reconstrói o filtro no idioma
        // atual preservando o gênero canônico selecionado.
        BuildGenreFilter();

        foreach (var item in LibraryFilterCombo.Items.OfType<ComboBoxItem>())
        {
            if (item.Content is string text)
                item.Content = LocalizationService.Translate(text);
        }

        // Alguns textos são gerados em tempo de execução e precisam ser refeitos
        // após uma troca de idioma.
        GameCountText.Text = LocalizedGameCount(_visibleGames.Count);
        UpdateFilterChips();
        UpdateSelectedGamePresentation();
        UpdateFooterInputHints();

        if (_trayIcon?.ContextMenuStrip is { } menu && menu.Items.Count >= 3)
        {
            menu.Items[0].Text = LocalizationService.Translate("Abrir LUDARYX");
            menu.Items[2].Text = LocalizationService.Translate("Encerrar");
        }
    }

    private static string LocalizedGameCount(int count)
    {
        return LocalizationService.CurrentLanguage switch
        {
            LocalizationService.PortuguesePortugal => $"{count} jogos apresentados",
            LocalizationService.EnglishUnitedStates or LocalizationService.EnglishUnitedKingdom => $"{count} games shown",
            LocalizationService.SpanishLatinAmerica or LocalizationService.SpanishEurope => $"{count} juegos mostrados",
            _ => $"{count} jogos exibidos"
        };
    }

    private void ApplyVisualSettings()
    {
        LudaryxThemeService.Apply(_settings);

        NeonSeparator.Background = _settings.NeonLineColor == "LightBlue"
            ? (Brush)FindResource("NeonBlue")
            : (Brush)FindResource("NeonRed");

        var lightTheme = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        var topBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#666B73" : "#050507"));
        var libraryBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#D9DDE3" : "#07182D"));
        var footerBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#666B73" : "#050507"));
        var selectionBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#FF163D" : "#66F5FF");
        var selectionGameFillColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#FF334F" : "#13DFFF");

        // No tema claro, Launcher e Gênero acompanham o cinza do cabeçalho.
        // Pesquisa e botões de ação usam um cinza levemente mais escuro para
        // ficarem perceptíveis sem destoar. No tema escuro, preservamos as
        // cores originais. O cursor da pesquisa é preto no claro e branco no escuro.
        var toolbarFieldColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#666B73" : "#050507");
        var searchFieldColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#565B62" : "#11151D");
        var navButtonColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#565B62" : "#11151D");
        var navButtonHoverColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#60666E" : "#1A202B");
        var navButtonPressedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#4C5158" : "#252D3A");
        var toolbarFieldHoverColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#737982" : "#0A0D12");
        var toolbarPopupColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#666B73" : "#050507");
        var toolbarItemHoverColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#737982" : "#111A26");
        var toolbarItemSelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#555B63" : "#10283D");
        var searchCaretColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#000000" : "#FFFFFF");

        // Superfícies do hero/painel mantêm seus contornos próprios.
        // A cor de seleção do tema é usada somente no foco dos controles.
        var selectedPanelBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#EEF1F5" : "#0A1421");
        var selectedPanelBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#A8B2C0" : "#1E3850");
        var selectedPanelArtworkBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#E2E7ED" : "#07111C");
        var selectedPanelArtworkBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#AAB5C3" : "#1C344C");
        var selectedPanelTextPrimaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#111827" : "#F5F7FA");
        var selectedPanelTextSecondaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#566273" : "#9AAABD");
        var selectedPanelMetaColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#0B638F" : "#82D8FF");
        var selectedPanelDividerColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#BCC5D0" : "#1B3147");

        var tvHeroBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#E7EBF0" : "#07111D");
        var tvHeroTextPrimaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#111827" : "#FFFFFF");
        var tvHeroTextSecondaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#4F5D6E" : "#A9B6C7");
        var tvHeroMetaColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#0B638F" : "#9EDBFF");
        var tvHeroDescriptionColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#263445" : "#D8E0EA");
        var tvHeroPreviewBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#DDE3EA" : "#33101C29");
        var tvHeroPreviewBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#9EABB9" : "#31506D");

        var actionButtonBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#E1E6EC" : "#111D2C");
        var actionButtonHoverBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#D3DAE3" : "#17273A");
        var actionButtonPressedBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#C3CCD7" : "#1B324A");
        var actionButtonForegroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#111827" : "#F5F7FA");

        var heroPlayBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#32BDEB" : "#D9153C");
        var heroPlayBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#6EDCFF" : "#FF3158");
        var heroPlayForegroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#07131D" : "#FFFFFF");

        var launchOverlayBackdropColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#BFE9EDF2" : "#D9020509");
        var launchOverlayPanelColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#F8F7F9FB" : "#F20A111B");
        var launchOverlayBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#A7B2C0" : "#31445A");
        var launchOverlayTextPrimaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#101722" : "#FFFFFF");
        var launchOverlayTextSecondaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#566273" : "#AAB7C8");
        var launchOverlayArtworkBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#E5E9EE" : "#07111C");
        var launchOverlayArtworkBorderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#B3BDC9" : "#26384C");
        var launchProgressTrackColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            lightTheme ? "#CBD2DA" : "#26313D");
        var launchOverlayNeonColor = selectionBorderColor;
        var launchOverlayNeonSoftColor = System.Windows.Media.Color.FromArgb(
            0x26,
            launchOverlayNeonColor.R,
            launchOverlayNeonColor.G,
            launchOverlayNeonColor.B);
        var launchOverlayNeonMediumColor = System.Windows.Media.Color.FromArgb(
            0x66,
            launchOverlayNeonColor.R,
            launchOverlayNeonColor.G,
            launchOverlayNeonColor.B);

        MainRoot.Background = topBrush;
        HeaderGrid.Background = topBrush;
        ToolbarBorder.Background = topBrush;
        LibraryAreaBorder.Background = libraryBrush;
        FooterBorder.Background = footerBrush;

        SetOrUpdateBrushResource("SelectionAccentBorder", selectionBorderColor);
        SetOrUpdateBrushResource("SelectionAccentGameFill", selectionGameFillColor);
        SetOrUpdateBrushResource("ToolbarFieldBackground", toolbarFieldColor);
        SetOrUpdateBrushResource("SearchFieldBackground", searchFieldColor);
        SetOrUpdateBrushResource("NavButtonBackground", navButtonColor);
        SetOrUpdateBrushResource("NavButtonHoverBackground", navButtonHoverColor);
        SetOrUpdateBrushResource("NavButtonPressedBackground", navButtonPressedColor);
        SetOrUpdateBrushResource("ToolbarFieldHoverBackground", toolbarFieldHoverColor);
        SetOrUpdateBrushResource("ToolbarPopupBackground", toolbarPopupColor);
        SetOrUpdateBrushResource("ToolbarItemHoverBackground", toolbarItemHoverColor);
        SetOrUpdateBrushResource("ToolbarItemSelectedBackground", toolbarItemSelectedColor);
        SetOrUpdateBrushResource("SearchCaretBrush", searchCaretColor);
        SetOrUpdateBrushResource("LudaryxButtonBackground", actionButtonBackgroundColor);
        SetOrUpdateBrushResource("LudaryxButtonHoverBackground", actionButtonHoverBackgroundColor);
        SetOrUpdateBrushResource("LudaryxButtonPressedBackground", actionButtonPressedBackgroundColor);
        SetOrUpdateBrushResource("LudaryxButtonForeground", actionButtonForegroundColor);
        SetOrUpdateBrushResource("SelectedPanelBackground", selectedPanelBackgroundColor);
        SetOrUpdateBrushResource("SelectedPanelBorder", selectedPanelBorderColor);
        SetOrUpdateBrushResource("SelectedPanelArtworkBackground", selectedPanelArtworkBackgroundColor);
        SetOrUpdateBrushResource("SelectedPanelArtworkBorder", selectedPanelArtworkBorderColor);
        SetOrUpdateBrushResource("SelectedPanelTextPrimary", selectedPanelTextPrimaryColor);
        SetOrUpdateBrushResource("SelectedPanelTextSecondary", selectedPanelTextSecondaryColor);
        SetOrUpdateBrushResource("SelectedPanelMeta", selectedPanelMetaColor);
        SetOrUpdateBrushResource("SelectedPanelDivider", selectedPanelDividerColor);
        SetOrUpdateBrushResource("TvHeroBackground", tvHeroBackgroundColor);
        SetOrUpdateBrushResource("TvHeroTextPrimary", tvHeroTextPrimaryColor);
        SetOrUpdateBrushResource("TvHeroTextSecondary", tvHeroTextSecondaryColor);
        SetOrUpdateBrushResource("TvHeroMeta", tvHeroMetaColor);
        SetOrUpdateBrushResource("TvHeroDescription", tvHeroDescriptionColor);
        SetOrUpdateBrushResource("TvHeroPreviewBackground", tvHeroPreviewBackgroundColor);
        SetOrUpdateBrushResource("TvHeroPreviewBorder", tvHeroPreviewBorderColor);
        SetOrUpdateBrushResource("LaunchOverlayBackdrop", launchOverlayBackdropColor);
        SetOrUpdateBrushResource("LaunchOverlayPanel", launchOverlayPanelColor);
        SetOrUpdateBrushResource("LaunchOverlayBorder", launchOverlayBorderColor);
        SetOrUpdateBrushResource("LaunchOverlayTextPrimary", launchOverlayTextPrimaryColor);
        SetOrUpdateBrushResource("LaunchOverlayTextSecondary", launchOverlayTextSecondaryColor);
        SetOrUpdateBrushResource("LaunchOverlayArtworkBackground", launchOverlayArtworkBackgroundColor);
        SetOrUpdateBrushResource("LaunchOverlayArtworkBorder", launchOverlayArtworkBorderColor);
        SetOrUpdateBrushResource("LaunchProgressTrack", launchProgressTrackColor);
        Resources["LaunchOverlayNeonColor"] = launchOverlayNeonColor;
        SetOrUpdateBrushResource("LaunchOverlayNeon", launchOverlayNeonColor);
        SetOrUpdateBrushResource("LaunchOverlayNeonSoft", launchOverlayNeonSoftColor);
        SetOrUpdateBrushResource("LaunchOverlayNeonMedium", launchOverlayNeonMediumColor);

        var heroPlayBackground = new SolidColorBrush(heroPlayBackgroundColor);
        var heroPlayBorder = new SolidColorBrush(heroPlayBorderColor);
        var heroPlayForeground = new SolidColorBrush(heroPlayForegroundColor);

        SelectedPlayButton.Background = heroPlayBackground;
        SelectedPlayButton.BorderBrush = heroPlayBorder;
        SelectedPlayButton.Foreground = heroPlayForeground;

        TvPlayButton.Background = new SolidColorBrush(heroPlayBackgroundColor);
        TvPlayButton.BorderBrush = new SolidColorBrush(heroPlayBorderColor);
        TvPlayButton.Foreground = new SolidColorBrush(heroPlayForegroundColor);

        TvHeroArtwork.Opacity = lightTheme ? 0.18 : 0.34;
        TvHeroTint.Background = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                lightTheme ? "#A8EEF1F5" : "#B807101B"));
        TvHeroGradientStart.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#F5EEF1F5" : "#F0060B12");
        TvHeroGradientMiddle.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#D8EEF1F5" : "#D00A1420");
        TvHeroGradientEnd.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(lightTheme ? "#A8EEF1F5" : "#900A1420");

        UpdateResponsiveLayout();
    }


    private void SetOrUpdateBrushResource(string key, System.Windows.Media.Color color)
    {
        // Brushes declarados em XAML podem ser congelados (Freezable) pelo WPF.
        // Alterar brush.Color nesses casos lança InvalidOperationException e pode
        // encerrar o aplicativo ao trocar o tema. Substituir o recurso inteiro
        // mantém os DynamicResource atualizados sem modificar uma instância congelada.
        Resources[key] = new SolidColorBrush(color);
    }

    #endregion
}

using System.Globalization;
using System.Windows;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class StatisticsWindow : Window
{
    private readonly WindowGamepadNavigationService _controllerNavigation;

    public StatisticsWindow(IEnumerable<Game> games)
    {
        InitializeComponent();
        LudaryxThemeService.Apply(new JsonSettingsService().Load());
        _controllerNavigation = new WindowGamepadNavigationService(this, Close, StatsScrollViewer);
        Loaded += (_, _) =>
        {
            CloseButton.Focus();
            System.Windows.Input.Keyboard.Focus(CloseButton);
        };

        var list = games.ToList();

        var totalLaunches = list.Sum(game => game.PlayCount);
        var totalSeconds = list.Sum(game => game.TotalPlayTimeSeconds);
        var totalTime = LocalizationService.FormatDuration(totalSeconds);

        SummaryText.Text = LocalizationService.FormatStatisticsSummary(list.Count, totalLaunches, totalTime);

        MostPlayedList.ItemsSource = list
            .Where(game => game.PlayCount > 0 || game.TotalPlayTimeSeconds > 0)
            .OrderByDescending(game => game.TotalPlayTimeSeconds)
            .ThenByDescending(game => game.PlayCount)
            .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        PlatformStatsList.ItemsSource = list
            .GroupBy(game => game.PlatformDisplay)
            .Select(group => new PlatformStat(
                group.Key,
                group.Sum(game => game.PlayCount),
                group.Sum(game => game.TotalPlayTimeSeconds)))
            .OrderByDescending(item => item.TotalSeconds)
            .ThenByDescending(item => item.LaunchCount)
            .ThenBy(item => item.Platform, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var settings = new JsonSettingsService().Load();
        var culture = GetCulture(LocalizationService.CurrentLanguage);
        MonthlyStatsList.ItemsSource = settings.MonthlyPlayTimeSeconds
            .Select(pair => CreateMonthlyStat(pair.Key, pair.Value, culture))
            .Where(item => item is not null)
            .Cast<MonthlyStat>()
            .OrderByDescending(item => item.SortKey)
            .Take(12)
            .ToList();

        LocalizationService.Apply(this);
    }

    private static MonthlyStat? CreateMonthlyStat(string key, long seconds, CultureInfo culture)
    {
        if (!DateTime.TryParseExact(key + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var month))
            return null;

        var display = month.ToString("MMMM yyyy", culture);
        if (!string.IsNullOrWhiteSpace(display))
            display = char.ToUpper(display[0], culture) + display[1..];

        return new MonthlyStat(display, key, seconds);
    }

    private static CultureInfo GetCulture(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch { return CultureInfo.InvariantCulture; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record PlatformStat(string Platform, int LaunchCount, long TotalSeconds)
    {
        public string Launches => LocalizationService.FormatLaunchCount(LaunchCount);
        public string PlayTime => LocalizationService.FormatDuration(TotalSeconds);
    }

    private sealed record MonthlyStat(string Month, string SortKey, long TotalSeconds)
    {
        public string PlayTime => LocalizationService.FormatDuration(TotalSeconds);
    }
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && ResizeMode != ResizeMode.NoResize)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

}

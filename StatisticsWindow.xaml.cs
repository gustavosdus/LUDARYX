using System.Globalization;
using System.Windows;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class StatisticsWindow : Window
{
    public StatisticsWindow(IEnumerable<Game> games)
    {
        InitializeComponent();
        var list = games.ToList();

        var totalLaunches = list.Sum(game => game.PlayCount);
        var totalSeconds = list.Sum(game => game.TotalPlayTimeSeconds);
        var totalTime = FormatDuration(totalSeconds);

        SummaryText.Text = $"{list.Count} jogos • {totalLaunches} inicializações • {totalTime} de tempo registrado";

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

    private static string FormatDuration(long seconds)
    {
        if (seconds <= 0) return "0 min";
        var duration = TimeSpan.FromSeconds(seconds);
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes:D2}min";
        return $"{Math.Max(1, duration.Minutes)} min";
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
        try { return CultureInfo.GetCultureInfo(language); }
        catch { return CultureInfo.InvariantCulture; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record PlatformStat(string Platform, int LaunchCount, long TotalSeconds)
    {
        public string Launches => LaunchCount == 1 ? "1 inicialização" : $"{LaunchCount} inicializações";
        public string PlayTime => FormatDuration(TotalSeconds);
    }

    private sealed record MonthlyStat(string Month, string SortKey, long TotalSeconds)
    {
        public string PlayTime => FormatDuration(TotalSeconds);
    }
}

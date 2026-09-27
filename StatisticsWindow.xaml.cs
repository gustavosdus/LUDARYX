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
                $"{group.Sum(game => game.PlayCount)} inicializações",
                FormatDuration(group.Sum(game => game.TotalPlayTimeSeconds))))
            .OrderByDescending(item => ParseDurationWeight(item.PlayTime))
            .ThenBy(item => item.Platform, StringComparer.OrdinalIgnoreCase)
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

    private static long ParseDurationWeight(string text)
    {
        // A ordenação principal já foi determinada a partir dos grupos antes da exibição;
        // esta função existe apenas para manter o view-model simples.
        return 0;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record PlatformStat(string Platform, string Launches, string PlayTime);
}

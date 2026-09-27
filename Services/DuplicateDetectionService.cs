using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed class DuplicateDetectionService
{
    public void Detect(IEnumerable<Game> games)
    {
        var list = games.ToList();
        foreach (var g in list)
        {
            g.IsDuplicate = false;
            g.CanonicalGameId = Normalize(g.Name);
            g.DuplicatePlatformsDisplay = "";
        }

        foreach (var group in list.Where(g => !string.IsNullOrWhiteSpace(g.CanonicalGameId)).GroupBy(g => g.CanonicalGameId, StringComparer.OrdinalIgnoreCase))
        {
            var entries = group.ToList();
            if (entries.Count < 2) continue;

            var platforms = entries.Select(g => g.Platform).Distinct().ToList();
            var display = platforms.Count > 1
                ? string.Join(" • ", platforms.Select(x => x.ToString()))
                : $"{platforms[0]} • {entries.Count} versões";

            foreach (var g in entries)
            {
                g.IsDuplicate = true;
                g.DuplicatePlatformsDisplay = display;
            }
        }
    }

    public static string Normalize(string value)
    {
        var text = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in text)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        text = sb.ToString().Normalize(NormalizationForm.FormC);
        text = Regex.Replace(text, @"\b(goty|game of the year|deluxe|ultimate|definitive|complete|edition|remastered|remake|hd|collection|bundle)\b", " ");
        text = Regex.Replace(text, @"[^a-z0-9]+", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}

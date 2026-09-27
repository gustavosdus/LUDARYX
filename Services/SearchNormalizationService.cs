using System.Globalization;
using System.Text;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public static class SearchNormalizationService
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static bool Matches(Game game, string query)
    {
        var normalizedQuery = Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return true;

        var fields = new[]
        {
            game.Name,
            game.PlatformDisplay,
            game.Metadata.Developer,
            game.Metadata.Publisher,
            string.Join(" ", game.Metadata.Genres),
            game.Metadata.ReleaseYear?.ToString()
        };

        return fields.Any(field => Normalize(field).Contains(normalizedQuery, StringComparison.Ordinal));
    }
}

using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public static class AgeRatingService
{
    private static readonly IReadOnlyDictionary<string, string[]> PreferredSystems =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["pt-BR"] = ["dejus", "classind", "pegi", "esrb"],
            ["pt-PT"] = ["pegi", "esrb", "dejus"],
            ["en-US"] = ["esrb", "pegi", "dejus"],
            ["en-GB"] = ["pegi", "esrb", "dejus"],
            ["es-ES"] = ["pegi", "esrb", "dejus"],
            // es-419 representa uma região, não um único país. Não convertemos notas
            // entre órgãos: mostramos a primeira classificação realmente fornecida
            // pela fonte, priorizando ESRB por ser comum em catálogos latino-americanos.
            ["es-419"] = ["esrb", "dejus", "pegi"]
        };

    public static string GetDisplay(GameMetadata metadata, string language)
    {
        metadata.AgeRatings ??= new(StringComparer.OrdinalIgnoreCase);

        if (!PreferredSystems.TryGetValue(language, out var systems))
            systems = ["esrb", "pegi", "dejus"];

        foreach (var system in systems)
        {
            if (TryGet(metadata.AgeRatings, system, out var value))
                return $"{GetSystemLabel(system)} {NormalizeValue(system, value)}";
        }

        foreach (var pair in metadata.AgeRatings)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
                return $"{GetSystemLabel(pair.Key)} {pair.Value.Trim()}";
        }

        return LocalizationService.Translate("Não informado");
    }

    public static string GetPreferredSystemKey(string language)
    {
        return PreferredSystems.TryGetValue(language, out var systems) && systems.Length > 0
            ? NormalizeSystem(systems[0])
            : "esrb";
    }

    public static string GetPreferredSystemLabel(string language)
        => GetSystemLabel(GetPreferredSystemKey(language));

    private static bool TryGet(Dictionary<string, string> ratings, string key, out string value)
    {
        foreach (var pair in ratings)
        {
            if (NormalizeSystem(pair.Key).Equals(NormalizeSystem(key), StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pair.Value))
            {
                value = pair.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public static string NormalizeSystem(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        if (normalized is "dejus" or "djctq" or "classind" or "brazil")
            return "dejus";
        if (normalized.Contains("pegi"))
            return "pegi";
        if (normalized.Contains("esrb"))
            return "esrb";

        return normalized;
    }

    private static string GetSystemLabel(string system) => NormalizeSystem(system) switch
    {
        "dejus" => "ClassInd",
        "pegi" => "PEGI",
        "esrb" => "ESRB",
        _ => system.ToUpperInvariant()
    };

    private static string NormalizeValue(string system, string value)
    {
        var raw = value.Trim();
        if (NormalizeSystem(system) != "esrb")
            return raw;

        return raw.ToUpperInvariant() switch
        {
            "E" or "EVERYONE" => "E",
            "E10" or "E10+" or "EVERYONE10+" => "E10+",
            "T" or "TEEN" => "T",
            "M" or "MATURE" or "MATURE17+" => "M",
            "AO" or "ADULTSONLY" or "ADULTSONLY18+" => "AO",
            "RP" or "RATINGPENDING" => "RP",
            _ => raw
        };
    }
}

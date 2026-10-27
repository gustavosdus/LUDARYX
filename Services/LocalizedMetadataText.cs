using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

/// <summary>Locale-tagged display text. Untagged legacy text is never assumed to be English.</summary>
public static class LocalizedMetadataText
{
    public const int CacheVersion = 2;

    public static void Prepare(GameMetadata metadata)
    {
        if (metadata.LocalizedTextVersion != CacheVersion)
        {
            // Older entries could mix manual titles with automatic titles, or contain
            // a provider's English fallback labelled as
            // Japanese/Portuguese. Re-fetch text only; keep artwork and other metadata.
            // Version 1 descriptions already have verified locale tags. Only titles
            // require migration when separating manual and automatic names.
            if (metadata.LocalizedTextVersion < 1)
                metadata.LocalizedDescriptions = new(StringComparer.OrdinalIgnoreCase);
            metadata.LocalizedNames = new(StringComparer.OrdinalIgnoreCase);
            metadata.LocalizedTextVersion = CacheVersion;
        }
        metadata.LocalizedDescriptionSources ??= new(StringComparer.OrdinalIgnoreCase);
        metadata.LocalizedDescriptions ??= new(StringComparer.OrdinalIgnoreCase);
        metadata.LocalizedNames ??= new(StringComparer.OrdinalIgnoreCase);
    }

    public static int DescriptionPriority(string? source) => source switch
    {
        "Steam" => 0,
        "Wikipedia" => 1,
        "GOG" => 2,
        _ => 3
    };

    public static void StoreDescription(GameMetadata metadata, string language, string text, string source)
    {
        Prepare(metadata);
        var existing = Exact(metadata.LocalizedDescriptions, language);
        if (!string.IsNullOrWhiteSpace(existing) &&
            DescriptionPriority(Exact(metadata.LocalizedDescriptionSources, language)) < DescriptionPriority(source))
            return;
        metadata.LocalizedDescriptions[language] = text;
        metadata.LocalizedDescriptionSources[language] = source;
    }

    public static string? Exact(IReadOnlyDictionary<string, string>? values, string language) =>
        values?.FirstOrDefault(pair => pair.Key.Equals(language, StringComparison.OrdinalIgnoreCase)).Value;

    public static string? Select(IReadOnlyDictionary<string, string>? values, string language)
    {
        if (values is null)
            return null;
        foreach (var key in new[] { language, "en-US", "en-GB" }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var value = values.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    public static string? GetDescription(GameMetadata metadata, string language) =>
        metadata.LocalizedTextVersion >= 1 ? Select(metadata.LocalizedDescriptions, language) : null;

    public static string? GetName(GameMetadata metadata, string language) =>
        metadata.LocalizedTextVersion == CacheVersion ? Select(metadata.LocalizedNames, language) : null;
}

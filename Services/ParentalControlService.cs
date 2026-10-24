using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public static class ParentalControlService
{
    public const string Default = "Default";
    public const string Age3To9 = "Age3To9";
    public const string Age10To13 = "Age10To13";
    public const string Age14To17 = "Age14To17";
    public const string Over18 = "Over18";

    private static bool _sessionUnlocked;

    public static bool IsSessionUnlocked => _sessionUnlocked;

    public static void UnlockSession() => _sessionUnlocked = true;

    public static string NormalizeMode(string? mode) => mode switch
    {
        Age3To9 or "Age3To5" => Age3To9,
        Age10To13 or "Age6To11" => Age10To13,
        Age14To17 or "Age12To15" => Age14To17,
        Over18 or "Age16To18" => Over18,
        _ => Default
    };

    public static bool HasPin(LauncherSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.ProtectedParentalPin);

    public static void SetPin(LauncherSettings settings, string pin)
    {
        ValidatePin(pin);
        settings.ProtectedParentalPin = SecretProtectionService.Protect(CreatePinRecord(pin));
        UnlockSession();
    }

    public static bool VerifyPin(LauncherSettings settings, string pin)
    {
        if (!IsValidPin(pin) || string.IsNullOrWhiteSpace(settings.ProtectedParentalPin))
            return false;

        var record = SecretProtectionService.Unprotect(settings.ProtectedParentalPin);
        if (string.IsNullOrWhiteSpace(record))
            return false;

        var parts = record.Split('$');
        if (parts.Length != 4 ||
            parts[0] != "v1" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
            iterations < 10_000)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                pin,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsGameAllowed(GameMetadata metadata, string? mode)
    {
        var normalizedMode = NormalizeMode(mode);
        if (normalizedMode == Default)
            return true;

        var allowedLevel = normalizedMode switch
        {
            Age3To9 => 1,
            Age10To13 => 2,
            Age14To17 => 3,
            _ => 4
        };

        metadata.AgeRatings ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var levels = new List<int>();
        foreach (var pair in metadata.AgeRatings)
        {
            var system = AgeRatingService.NormalizeSystem(pair.Key);
            if (system is not ("dejus" or "esrb" or "pegi"))
                continue;

            var result = ParseRatingLevel(system, pair.Value);
            if (result.Kind == RatingParseKind.PendingOrUnrated)
                continue;

            if (result.Kind == RatingParseKind.UnknownText)
                return false;

            if (result.Level.HasValue)
                levels.Add(result.Level.Value);
        }

        if (levels.Count == 0)
            return false;

        return levels.Max() <= allowedLevel;
    }

    private static RatingParseResult ParseRatingLevel(string system, string? value)
    {
        var raw = value?.Trim() ?? "";
        if (raw.Length == 0)
            return RatingParseResult.Unknown;

        if (system == "esrb")
        {
            var normalized = AgeRatingService.NormalizeValue(system, raw);
            var compact = Regex.Replace(
                normalized.ToUpperInvariant(),
                @"[^A-Z0-9+]",
                "");

            if (compact is "NR" or "NOTRATED" or "RP" or "RATINGPENDING")
                return RatingParseResult.Pending;

            return compact switch
            {
                "EC" or "EARLYCHILDHOOD" => RatingParseResult.Known(1),
                "E" or "EVERYONE" => RatingParseResult.Known(1),
                "E10+" or "EVERYONE10+" or "EVERYONE10PLUS" => RatingParseResult.Known(2),
                "T" or "TEEN" => RatingParseResult.Known(3),
                "M" or "MATURE" or "MATURE17+" or "MATURE17PLUS" => RatingParseResult.Known(4),
                "AO" or "AO18+" or "ADULTSONLY" or "ADULTSONLY18+" or "ADULTSONLY18PLUS" => RatingParseResult.Known(4),
                _ when compact.Any(char.IsLetter) => RatingParseResult.Unknown,
                _ => ParseNumericFallback(raw)
            };
        }

        if (system == "dejus")
        {
            var normalized = AgeRatingService.NormalizeValue(system, raw);
            if (normalized.Equals("L", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("LIVRE", StringComparison.OrdinalIgnoreCase))
            {
                return RatingParseResult.Known(1);
            }

            if (TryExtractNumber(normalized, out var classIndAge))
            {
                return classIndAge switch
                {
                    <= 9 => RatingParseResult.Known(1),
                    <= 13 => RatingParseResult.Known(2),
                    <= 17 => RatingParseResult.Known(3),
                    _ => RatingParseResult.Known(4)
                };
            }

            return RatingParseResult.Unknown;
        }

        if (TryExtractNumber(raw, out var pegiAge))
        {
            return pegiAge switch
            {
                <= 7 => RatingParseResult.Known(1),
                <= 12 => RatingParseResult.Known(2),
                <= 16 => RatingParseResult.Known(3),
                _ => RatingParseResult.Known(4)
            };
        }

        return RatingParseResult.Unknown;
    }

    private static RatingParseResult ParseNumericFallback(string raw)
    {
        if (!TryExtractNumber(raw, out var age))
            return RatingParseResult.Unknown;

        return age switch
        {
            <= 9 => RatingParseResult.Known(1),
            <= 13 => RatingParseResult.Known(2),
            <= 17 => RatingParseResult.Known(3),
            _ => RatingParseResult.Known(4)
        };
    }

    private static bool TryExtractNumber(string raw, out int age)
    {
        var match = Regex.Match(raw, @"\d+");
        if (!match.Success || !int.TryParse(match.Value, out age))
        {
            age = 0;
            return false;
        }

        age = Math.Max(0, age);
        return true;
    }

    private static string CreatePinRecord(string pin)
    {
        const int iterations = 120_000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);

        return string.Join("$", "v1", iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    private static void ValidatePin(string pin)
    {
        if (!IsValidPin(pin))
            throw new ArgumentException("O PIN deve conter exatamente 4 números.", nameof(pin));
    }

    public static bool IsValidPin(string? pin) =>
        pin is { Length: 4 } && pin.All(char.IsDigit);

    private enum RatingParseKind
    {
        Known,
        PendingOrUnrated,
        UnknownText
    }

    private readonly record struct RatingParseResult(RatingParseKind Kind, int? Level)
    {
        public static RatingParseResult Known(int level) => new(RatingParseKind.Known, level);
        public static RatingParseResult Pending => new(RatingParseKind.PendingOrUnrated, null);
        public static RatingParseResult Unknown => new(RatingParseKind.UnknownText, null);
    }
}

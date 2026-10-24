using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public static class ParentalControlService
{
    public const string Default = "Default";
    public const string Age3To5 = "Age3To5";
    public const string Age6To11 = "Age6To11";
    public const string Age12To15 = "Age12To15";
    public const string Age16To18 = "Age16To18";

    private static bool _sessionUnlocked;

    public static bool IsSessionUnlocked => _sessionUnlocked;

    public static void UnlockSession() => _sessionUnlocked = true;

    public static string NormalizeMode(string? mode) => mode switch
    {
        Age3To5 => Age3To5,
        Age6To11 => Age6To11,
        Age12To15 => Age12To15,
        Age16To18 => Age16To18,
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

        var maxAge = normalizedMode switch
        {
            Age3To5 => 5,
            Age6To11 => 11,
            Age12To15 => 15,
            _ => int.MaxValue
        };

        metadata.AgeRatings ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var ages = new List<int>();
        foreach (var pair in metadata.AgeRatings)
        {
            var system = AgeRatingService.NormalizeSystem(pair.Key);
            if (system is not ("dejus" or "esrb" or "pegi"))
                continue;

            var result = ParseRating(system, pair.Value);
            if (result.Kind == RatingParseKind.PendingOrUnrated)
                continue;

            if (result.Kind == RatingParseKind.UnknownText)
                return false;

            if (result.Age.HasValue)
                ages.Add(result.Age.Value);
        }

        if (ages.Count == 0)
            return false;

        return ages.Max() <= maxAge;
    }

    private static RatingParseResult ParseRating(string system, string? value)
    {
        var raw = value?.Trim() ?? "";
        if (raw.Length == 0)
            return RatingParseResult.Unknown;

        var upper = raw.ToUpperInvariant();

        if (system == "esrb")
        {
            var compact = Regex.Replace(upper, @"[^A-Z0-9+]", "");
            if (compact is "NR" or "NOTRATED" or "RP" or "RATINGPENDING")
                return RatingParseResult.Pending;

            return compact switch
            {
                "EC" or "EARLYCHILDHOOD" => RatingParseResult.Known(3),
                "E" or "EVERYONE" => RatingParseResult.Known(5),
                "E10+" or "EVERYONE10+" or "EVERYONE10PLUS" => RatingParseResult.Known(10),
                "T" or "TEEN" => RatingParseResult.Known(13),
                "M" or "MATURE" or "MATURE17+" or "MATURE17PLUS" => RatingParseResult.Known(17),
                "AO" or "AO18+" or "ADULTSONLY" or "ADULTSONLY18+" or "ADULTSONLY18PLUS" => RatingParseResult.Known(18),
                _ when compact.Any(char.IsLetter) => RatingParseResult.Unknown,
                _ => ParseNumericFallback(raw)
            };
        }

        if (system == "dejus")
        {
            if (upper is "L" or "LIVRE")
                return RatingParseResult.Known(0);

            return ParseNumericFallback(raw);
        }

        return ParseNumericFallback(raw);
    }

    private static RatingParseResult ParseNumericFallback(string raw)
    {
        var match = Regex.Match(raw, @"\d+");
        if (!match.Success || !int.TryParse(match.Value, out var age))
            return RatingParseResult.Unknown;

        return RatingParseResult.Known(Math.Max(0, Math.Min(age, 18)));
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

    private readonly record struct RatingParseResult(RatingParseKind Kind, int? Age)
    {
        public static RatingParseResult Known(int age) => new(RatingParseKind.Known, age);
        public static RatingParseResult Pending => new(RatingParseKind.PendingOrUnrated, null);
        public static RatingParseResult Unknown => new(RatingParseKind.UnknownText, null);
    }
}

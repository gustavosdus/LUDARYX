using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Localização centralizada da interface. Os textos-base são mantidos em pt-BR
/// para que futuras edições manuais continuem simples e previsíveis.
/// </summary>
public static partial class LocalizationService
{
    public const string PortugueseBrazil = "pt-BR";
    public const string PortuguesePortugal = "pt-PT";
    public const string EnglishUnitedStates = "en-US";
    public const string EnglishUnitedKingdom = "en-GB";
    // es-419 remains the persisted compatibility code; its translation column uses
    // Mexican Spanish as the baseline for Latin-American wording.
    public const string SpanishLatinAmerica = "es-419";
    public const string SpanishEurope = "es-ES";
    public const string JapaneseJapan = "ja-JP";

    public static string CurrentLanguage { get; private set; } = PortugueseBrazil;

    // Labels consumed by DataTemplates are resolved when each visual item is created.
    // This avoids leaving template-only buttons in the base pt-BR text.
    public static string OpenClientLabel => Translate("ABRIR CLIENTE");
    public static string VerifiedLabel => Translate("VERIFICADO");
    public static string UseArtworkLabel => Translate("USAR ESTA ARTE");
    public static string DuplicateLabel => Translate("DUPLICATA");

    public static string DisplayProfileName(string? name) =>
        string.Equals(name, "Padrão", StringComparison.OrdinalIgnoreCase)
            ? Translate("Padrão")
            : name ?? string.Empty;

    public static IReadOnlyList<(string Code, string Name)> SupportedLanguages
    {
        get;
    } =
    [
        (PortugueseBrazil, "Português (Brasil)"),
        (PortuguesePortugal, "Português (Portugal)"),
        (EnglishUnitedStates, "English (US)"),
        (EnglishUnitedKingdom, "English (UK)"),
        (SpanishLatinAmerica, "Español (Latinoamérica — México)"),
        (SpanishEurope, "Español (España)"),
        (JapaneseJapan, "日本語")
    ];

    public static void SetLanguage(string? languageCode)
    {
        CurrentLanguage = SupportedLanguages.Any(x => x.Code.Equals(languageCode, StringComparison.OrdinalIgnoreCase))
            ? languageCode!
            : PortugueseBrazil;
    }

    public static string Translate(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var trimmed = text.Trim();
        if (trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            var suffix = trimmed[1..].Trim();
            var translatedSuffix = Translate(suffix);
            return $"  /  {translatedSuffix}";
        }

        if (TryTranslateSupplemental(text, out var supplemental))
            return supplemental;

        var translation = FindTranslation(text);
        return translation is null ? text : SelectLanguage(translation);
    }

    public static string DisplayMetadataSources(string sources) =>
        string.Join(" + ", sources.Split(" + ", StringSplitOptions.TrimEntries)
            .Select(Translate));

    public static string Format(string baseText, params object[] args) =>
        string.Format(Translate(baseText), args);

    public static string FormatPlayCount(int count) => CurrentLanguage switch
    {
        JapaneseJapan => $"{count}回",
        EnglishUnitedStates or EnglishUnitedKingdom => count == 1 ? "1 launch" : $"{count} launches",
        SpanishLatinAmerica or SpanishEurope => count == 1 ? "1 partida" : $"{count} partidas",
        _ => count == 1 ? "1 partida" : $"{count} partidas"
    };

    public static string FormatLaunchCount(int count) => CurrentLanguage switch
    {
        JapaneseJapan => $"{count}回起動",
        EnglishUnitedStates or EnglishUnitedKingdom => count == 1 ? "1 launch" : $"{count} launches",
        SpanishLatinAmerica or SpanishEurope => count == 1 ? "1 inicio" : $"{count} inicios",
        _ => count == 1 ? "1 inicialização" : $"{count} inicializações"
    };

    public static string FormatDuration(long seconds)
    {
        if (seconds <= 0)
            return CurrentLanguage == JapaneseJapan ? "0分" : "0 min";

        var duration = TimeSpan.FromSeconds(seconds);
        if (CurrentLanguage == JapaneseJapan)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}時間 {duration.Minutes}分";
            return $"{Math.Max(1, duration.Minutes)}分";
        }

        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes:D2}min";
        return $"{Math.Max(1, duration.Minutes)} min";
    }

    public static string FormatLastPlayed(DateTime? value)
    {
        if (!value.HasValue)
            return Translate("Nunca jogado");

        var cultureName = CurrentLanguage switch
        {
            JapaneseJapan => "ja-JP",
            PortuguesePortugal => "pt-PT",
            EnglishUnitedStates => "en-US",
            EnglishUnitedKingdom => "en-GB",
            SpanishLatinAmerica => "es-MX",
            SpanishEurope => "es-ES",
            _ => "pt-BR"
        };

        try
        {
            return value.Value.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo(cultureName));
        }
        catch
        {
            return value.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        }
    }

    public static string FormatStatisticsSummary(int games, int launches, string totalTime) => CurrentLanguage switch
    {
        JapaneseJapan => $"{games}本のゲーム • {launches}回起動 • 記録時間 {totalTime}",
        EnglishUnitedStates or EnglishUnitedKingdom => $"{games} games • {launches} launches • {totalTime} recorded",
        SpanishLatinAmerica or SpanishEurope => $"{games} juegos • {launches} inicios • {totalTime} registrados",
        PortuguesePortugal => $"{games} jogos • {launches} inicializações • {totalTime} registados",
        _ => $"{games} jogos • {launches} inicializações • {totalTime} de tempo registrado"
    };

    public static void Apply(DependencyObject root)
    {
        // Translate authored controls only. ContentPresenter creates visual text
        // whose value is maintained by WPF; writing to it freezes ComboBox
        // selections and button glyphs when their source content changes.
        ApplyToObject(root);

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyObject)
                Apply(dependencyObject);
        }
    }

    private static void ApplyToObject(DependencyObject item)
    {
        if (item is Window window)
            window.Title = Translate(window.Title);

        if (item is TextBlock textBlock &&
            !BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty) &&
            textBlock.Inlines.Count <= 1)
        {
            textBlock.Text = Translate(textBlock.Text);
        }

        if (item is ContentControl contentControl && contentControl.Content is string content)
            contentControl.Content = Translate(content);

        if (item is HeaderedContentControl headered && headered.Header is string header)
            headered.Header = Translate(header);

        if (item is FrameworkElement frameworkElement && frameworkElement.ToolTip is string tooltip)
            frameworkElement.ToolTip = Translate(tooltip);
    }

    private static bool TryTranslateSupplemental(string text, out string translated)
    {
        var normalized = text.Trim();
        foreach (var item in SupplementalTranslations)
        {
            if (!MatchesSupplemental(item, normalized))
                continue;

            translated = CurrentLanguage switch
            {
                PortuguesePortugal => item.PtPt,
                EnglishUnitedStates => item.EnUs,
                EnglishUnitedKingdom => item.EnGb,
                SpanishLatinAmerica => item.Es419,
                SpanishEurope => item.EsEs,
                JapaneseJapan => item.JaJp,
                _ => item.PtBr
            };
            return true;
        }

        translated = string.Empty;
        return false;
    }

    private static bool MatchesSupplemental(Translation item, string text) =>
        text.Equals(item.PtBr, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(item.PtPt, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(item.EnUs, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(item.EnGb, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(item.Es419, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(item.EsEs, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(item.JaJp, StringComparison.Ordinal);

    private static Translation? FindTranslation(string text)
    {
        var normalizedText = text.Trim();

        // Permite sair do japonês para qualquer outro idioma sem deixar textos
        // já traduzidos presos na interface.
        // Japanese has no upper/lower case. Preserve the established choice
        // when multiple Portuguese labels share the same Japanese wording.
        var preferredSource = normalizedText switch
        {
            "ゲームを追加" => "Adicionar jogo",
            "情報 / クレジット" => "Sobre / Créditos",
            "起動設定を編集" => "EDITAR INICIALIZAÇÃO",
            "閉じる" => "FECHAR",
            "非表示" => "OCULTAR",
            _ => null
        };
        var japaneseMatch = Translations.FirstOrDefault(item =>
            preferredSource is not null
                ? item.PtBr.Equals(preferredSource, StringComparison.Ordinal)
                : normalizedText.Equals(item.JaJp, StringComparison.Ordinal));
        if (japaneseMatch is not null)
            return japaneseMatch;

        // Mantém a intenção visual de rótulos em caixa alta quando existem
        // traduções específicas para eles. O fallback sem diferenciar maiúsculas
        // continua útil para textos legados e conteúdo já traduzido.
        var exact = Translations.FirstOrDefault(x =>
            normalizedText.Equals(x.PtBr, StringComparison.Ordinal) ||
            normalizedText.Equals(x.PtPt, StringComparison.Ordinal) ||
            normalizedText.Equals(x.EnUs, StringComparison.Ordinal) ||
            normalizedText.Equals(x.EnGb, StringComparison.Ordinal) ||
            normalizedText.Equals(x.Es419, StringComparison.Ordinal) ||
            normalizedText.Equals(x.EsEs, StringComparison.Ordinal));

        if (exact is not null)
            return exact;

        return Translations.FirstOrDefault(x =>
            normalizedText.Equals(x.PtBr, StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Equals(x.PtPt, StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Equals(x.EnUs, StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Equals(x.EnGb, StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Equals(x.Es419, StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Equals(x.EsEs, StringComparison.OrdinalIgnoreCase));
    }

    private static string SelectLanguage(Translation translation) => CurrentLanguage switch
    {
        PortuguesePortugal => translation.PtPt,
        EnglishUnitedStates => translation.EnUs,
        EnglishUnitedKingdom => translation.EnGb,
        SpanishLatinAmerica => translation.Es419,
        SpanishEurope => translation.EsEs,
        JapaneseJapan => translation.JaJp,
        _ => translation.PtBr
    };
}

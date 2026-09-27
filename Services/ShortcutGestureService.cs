using System.Windows.Input;

namespace UnifiedGameLauncher.Services;

public static class ShortcutGestureService
{
    public static bool IsValid(string? shortcut) => TryParse(shortcut, out _, out _);

    public static bool Matches(KeyEventArgs e, string? shortcut)
    {
        if (!TryParse(shortcut, out var expectedKey, out var expectedModifiers))
            return false;

        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        return actualKey == expectedKey && Keyboard.Modifiers == expectedModifiers;
    }

    public static bool TryParse(string? shortcut, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;

        if (string.IsNullOrWhiteSpace(shortcut))
            return false;

        var value = shortcut.Trim();

        // Atalhos com pontuação como Ctrl+, são ambíguos para KeyGestureConverter
        // em alguns layouts/idiomas do Windows. Fazemos o parsing explicitamente
        // para que a tecla física de vírgula (OemComma) funcione em pt-BR também.
        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        var keyToken = parts[^1];
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    return false;
            }
        }

        if (!TryParseKeyToken(keyToken, out key))
            return false;

        return key != Key.None;
    }

    private static bool TryParseKeyToken(string token, out Key key)
    {
        key = token switch
        {
            "," => Key.OemComma,
            "." => Key.OemPeriod,
            ";" => Key.OemSemicolon,
            "/" => Key.OemQuestion,
            "'" => Key.OemQuotes,
            "[" => Key.OemOpenBrackets,
            "]" => Key.OemCloseBrackets,
            "\\" => Key.OemBackslash,
            "-" => Key.OemMinus,
            "=" => Key.OemPlus,
            "`" => Key.OemTilde,
            _ => Key.None
        };

        if (key != Key.None)
            return true;

        if (token.Length == 1 && char.IsLetter(token[0]))
            return Enum.TryParse(token.ToUpperInvariant(), ignoreCase: true, out key);

        if (token.Length == 1 && char.IsDigit(token[0]))
            return Enum.TryParse("D" + token, ignoreCase: true, out key);

        return Enum.TryParse(token, ignoreCase: true, out key);
    }
}

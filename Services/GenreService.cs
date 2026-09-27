using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Normaliza gêneros vindos de fontes diferentes. Mantém um valor canônico em inglês
/// para evitar duplicatas entre idiomas e separa gêneros reais de classificações etárias,
/// plataformas, categorias de loja e outros marcadores que não pertencem ao campo gênero.
/// </summary>
public static class GenreService
{
    private static readonly Dictionary<string, string> Aliases = BuildAliases();

    private static readonly HashSet<string> RejectedExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "games", "video game", "video games",
        "teen", "teen 13+", "t", "mature", "mature 17+", "m", "everyone", "everyone 10+", "e", "e10+",
        "adults only", "adults only 18+", "ao", "rating pending", "rp", "esrb",
        "not rated", "unrated", "pegi", "usk", "cero",
        "not approved for young persons aged under 18",
        "not approved for young persons under 18",
        "approved without age restriction",
        "windows", "pc", "mac", "linux", "steam", "epic games", "gog", "xbox", "playstation",
        "single-player", "single player", "multiplayer", "multi-player", "co-op", "coop", "online co-op",
        "controller", "full controller support", "steam achievements", "steam cloud", "remote play"
    };

    private static readonly Regex RatingPattern = new(
        @"^(?:pegi\s*)?(?:3|7|12|16|18)(?:\+)?$|^(?:usk\s*)?(?:0|6|12|16|18)$|^(?:ma\s*15\+|r\s*18\+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void NormalizeInPlace(GameMetadata metadata)
    {
        metadata.Genres = NormalizeMany(metadata.Genres).ToList();
    }

    public static IReadOnlyList<string> NormalizeMany(IEnumerable<string>? values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (values is null)
            return result;

        foreach (var value in values)
        {
            foreach (var part in SplitValue(value))
            {
                var normalized = Normalize(part);
                if (normalized is null || !seen.Add(normalized))
                    continue;

                result.Add(normalized);
            }
        }

        return result;
    }

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var clean = Regex.Replace(value.Trim(), @"\s+", " ");
        if (clean.Length < 2 || IsRejected(clean))
            return null;

        var key = Fold(clean);
        if (Aliases.TryGetValue(key, out var canonical))
            return canonical;

        // Wikidata e outras fontes costumam usar nomes como "fighting game" ou
        // "role-playing video game". Remove apenas esse sufixo classificatório e
        // reaplica o mapa; não altera nomes desconhecidos arbitrariamente.
        foreach (var suffix in new[] { " video game", " game" })
        {
            if (!key.EndsWith(suffix, StringComparison.Ordinal))
                continue;

            var withoutSuffix = key[..^suffix.Length].Trim();
            if (Aliases.TryGetValue(withoutSuffix, out canonical))
                return canonical;
        }

        // Segurança de dados: metadados externos misturam tags, classificações etárias,
        // categorias e gêneros. Só aceitamos termos reconhecidos pelo mapa acima.
        // Isso evita que valores como "Teen", "Managerial" ou categorias de loja
        // reapareçam como se fossem gêneros depois de uma atualização.
        return null;
    }

    public static string Display(string genre) =>
        LocalizationService.Translate(Normalize(genre) ?? genre);

    public static string DisplayMany(IEnumerable<string>? genres) =>
        string.Join(" • ", NormalizeMany(genres).Select(Display));

    public static string DisplayManyCommaSeparated(IEnumerable<string>? genres) =>
        string.Join(", ", NormalizeMany(genres).Select(Display));

    private static IEnumerable<string> SplitValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        // Vírgula, ponto e vírgula e pipe são separadores comuns nas APIs.
        // Hífens NÃO são separadores: gêneros como "Point-and-click" dependem deles.
        foreach (var part in value.Split(new[] { ',', ';', '|' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            yield return part;
    }

    private static bool IsRejected(string value)
    {
        if (RejectedExact.Contains(value) || RatingPattern.IsMatch(value))
            return true;

        var folded = Fold(value);
        return folded.StartsWith("esrb ", StringComparison.Ordinal) ||
               folded.StartsWith("pegi ", StringComparison.Ordinal) ||
               folded.StartsWith("usk ", StringComparison.Ordinal) ||
               folded.Contains("age rating", StringComparison.Ordinal) ||
               folded.Contains("content rating", StringComparison.Ordinal) ||
               folded.Contains("not approved for young persons", StringComparison.Ordinal) ||
               folded.StartsWith("approved for children aged", StringComparison.Ordinal) ||
               folded.StartsWith("approved for persons aged", StringComparison.Ordinal) ||
               folded.StartsWith("approved without age restriction", StringComparison.Ordinal);
    }

    private static Dictionary<string, string> BuildAliases()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Add(map, "Action", "Action", "Ação", "Acao", "Acción", "Accion");
        Add(map, "Action-Adventure", "Action-Adventure", "Action Adventure", "Action-adventure game", "Ação e aventura", "Acción y aventura");
        Add(map, "Adventure", "Adventure", "Aventura");
        Add(map, "Arcade", "Arcade");
        Add(map, "Casual", "Casual");
        Add(map, "Indie", "Indie", "Independent");
        Add(map, "RPG", "RPG", "Role-playing", "Role Playing", "Role-playing game", "Role Playing Game");
        Add(map, "Strategy", "Strategy", "Estratégia", "Estrategia");
        Add(map, "Simulation", "Simulation", "Simulação", "Simulacao", "Simulación", "Simulacion", "Simulator", "Simulador");
        Add(map, "Sports", "Sports", "Sport", "Esportes", "Esporte", "Desporto", "Deportes", "Deporte");
        Add(map, "Racing", "Racing", "Corrida", "Corridas", "Carreras");
        Add(map, "Puzzle", "Puzzle", "Quebra-cabeça", "Quebra-cabeças", "Puzles");
        Add(map, "Platformer", "Platformer", "Platform", "Plataforma", "Plataformas");
        Add(map, "Shooter", "Shooter", "Tiro", "Disparos");
        Add(map, "FPS", "FPS", "FPP", "First-person shooter", "First Person Shooter");
        Add(map, "Third-Person Shooter", "Third-Person Shooter", "Third Person Shooter", "Tiro em terceira pessoa", "Tiro na terceira pessoa", "Disparos en tercera persona");
        Add(map, "Fighting", "Fighting", "Luta", "Lucha", "Combat Fighting", "Arena Fighter", "arena fighter");
        Add(map, "Horror", "Horror", "Terror");
        Add(map, "Survival", "Survival", "Sobrevivência", "Sobrevivencia", "Supervivencia");
        Add(map, "Stealth", "Stealth", "Furtividade", "Sigilo");
        Add(map, "Open World", "Open World", "Mundo aberto", "Mundo abierto");
        Add(map, "Massively Multiplayer", "Massively Multiplayer", "Massive Multiplayer", "Massively multiplayer online game", "Multijogador massivo", "Multijugador masivo");
        Add(map, "MMORPG", "MMORPG", "Massively multiplayer online role-playing game");
        Add(map, "Family", "Family", "Família", "Familia", "Familiar");
        Add(map, "Card Game", "Card Game", "Digital collectible card game", "Collectible card game", "Collectible card video game", "Trading card game", "Jogo de cartas", "Juego de cartas");
        Add(map, "Board Game", "Board Game", "Jogo de tabuleiro", "Juego de mesa");
        Add(map, "Music", "Music", "Música", "Musica");
        Add(map, "Party", "Party", "Festa", "Fiesta");
        Add(map, "Visual Novel", "Visual Novel", "Romance visual", "Novela visual");
        Add(map, "Roguelike", "Roguelike");
        Add(map, "Roguelite", "Roguelite");
        Add(map, "Metroidvania", "Metroidvania");
        Add(map, "Souls-like", "Souls-like", "Soulslike");
        Add(map, "Hack and Slash", "Hack and Slash", "Hack & Slash");
        Add(map, "Beat 'em up", "Beat 'em up", "Beat em up", "Briga de rua", "Yo contra el barrio");
        Add(map, "Free to Play", "Free to Play", "Free-to-play", "Grátis para jogar", "Gratis para jogar", "Gratuito para jogar", "Gratuito");
        Add(map, "Early Access", "Early Access", "Acesso antecipado", "Acceso anticipado");
        Add(map, "Point-and-click", "Point-and-click", "Point and Click", "Apontar e clicar", "Apuntar y hacer clic");
        Add(map, "Real Time Strategy (RTS)", "Real Time Strategy (RTS)", "Real-time strategy", "RTS", "Estratégia em tempo real (RTS)", "Estrategia en tiempo real (RTS)");
        Add(map, "Turn-based strategy (TBS)", "Turn-based strategy (TBS)", "Turn-based strategy", "Turn-based", "TBS", "Estratégia por turnos (TBS)", "Estrategia por turnos (TBS)");
        Add(map, "Tactical", "Tactical", "Tático", "Tatico", "Táctico", "Tactico");
        Add(map, "Quiz/Trivia", "Quiz/Trivia", "Trivia", "Perguntas e respostas", "Preguntas y respuestas");
        Add(map, "Pinball", "Pinball");
        Add(map, "Card & Board Game", "Card & Board Game", "Cartas e tabuleiro", "Cartas y juegos de mesa");
        Add(map, "MOBA", "MOBA", "Multiplayer online battle arena");
        Add(map, "Historical", "Historical", "Histórico", "Historico", "Histórico/a");
        Add(map, "Combat", "Combat", "Combate", "Crowd-combat video game", "Crowd combat video game");
        Add(map, "Fantasy", "Fantasy", "Fantasia", "Fantasía", "Fantasia medieval");
        Add(map, "Simulation", "Management", "Managerial", "Management game", "Business simulation");
        Add(map, "Strategy", "Auto Battler", "Auto-battler", "Autobattler");

        return map;
    }

    private static void Add(Dictionary<string, string> map, string canonical, params string[] aliases)
    {
        foreach (var alias in aliases.Append(canonical))
            map[Fold(alias)] = canonical;
    }

    private static string Fold(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(c));
        }

        return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
    }
}

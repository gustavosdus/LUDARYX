using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher.Providers;

public sealed class RiotClientProvider : ClientShortcutProvider
{
    public override string Name => "Riot Client";
    public override GamePlatform Platform => GamePlatform.RiotClient;
    protected override string ProcessName => "RiotClientServices";
    protected override string[] LauncherExecutables => new[]
    {
        @"C:\Riot Games\Riot Client\RiotClientServices.exe",
        @"C:\Program Files\Riot Games\Riot Client\RiotClientServices.exe"
    };
    protected override string[] ShortcutDirectoryNames => new[] { "Riot Games", "Riot Client" };
    protected override string[] RegistrySubKeys => new[] { @"Software\Riot Games", @"Software\WOW6432Node\Riot Games" };
    protected override string[] ExcludedShortcutNames => new[] { "Riot Client", "Cliente Riot", "Riot Vanguard", "Vanguard", "Uninstall" };

    // InstallLocation do registro pode apontar para qualquer unidade/pasta escolhida pelo usuário.
    protected override IEnumerable<Game> DiscoverInstalledGamesWithoutShortcuts() =>
        InstalledGameDiscoveryService.FromRegistryUninstall(
            Platform,
            new[] { "Riot Games" },
            new[] { "Riot Client", "Riot Vanguard", "Vanguard" });


    public override async Task<List<Game>> GetGamesAsync(CancellationToken cancellationToken = default)
    {
        var games = await base.GetGamesAsync(cancellationToken);

        return games
            .Where(game =>
            {
                var product = GetRiotProduct(game);

                // Produtos conhecidos da Riot só entram na biblioteca quando houver
                // evidência da instalação real. Um .lnk antigo apontando apenas para o
                // Riot Client não é suficiente, pois esses atalhos podem sobreviver à
                // desinstalação do jogo.
                return product is null || IsRiotProductInstalled(product, game);
            })
            .ToList();
    }

    public override async Task LaunchGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        var product = GetRiotProduct(game);

        if (product is not null && !IsRiotProductInstalled(product, game))
        {
            throw new InvalidOperationException(
                $"{game.Name} não parece mais estar instalado. Atualize a biblioteca para remover o item antigo.");
        }

        if (product is not null)
        {
            var launcher = FindLauncherExecutable();
            if (launcher is not null)
            {
                // Em 2026 a Riot alterou o fluxo dos atalhos para abrir primeiro a página
                // do jogo no Riot Client. --allow-direct-launch restaura o lançamento
                // direto quando o produto aceita essa opção (confirmado para League).
                // Também tentamos a mesma opção para os demais produtos; se a versão do
                // Riot Client ignorá-la, o próprio cliente tratará o argumento normalmente.
                var directArgs = $"--launch-product={product} --launch-patchline=live --allow-direct-launch";
                ProcessService.Start(launcher, directArgs);
                return;
            }
        }

        // Se não reconhecermos o produto, preserve o atalho oficial da Riot.
        if (!string.IsNullOrWhiteSpace(game.LaunchUri) &&
            game.LaunchUri.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryValidateDetectedShortcut(game.LaunchUri, out var shortcut))
                throw new InvalidOperationException("O atalho detectado da Riot não é mais considerado seguro para execução.");
            ProcessService.StartShellFile(shortcut);
            return;
        }

        // Último fallback: abre o Riot Client, sem tentar executar diretamente
        // LeagueClient.exe/VALORANT.exe fora do contexto esperado pela Riot.
        await StartClientAsync(cancellationToken);
    }

    private static bool IsRiotProductInstalled(string product, Game game)
    {
        if (HasExistingProductExecutable(game, product))
            return true;

        if (TryGetInstallPathFromRiotMetadata(product, out var metadataInstallPath) &&
            HasExpectedProductExecutable(metadataInstallPath, product))
        {
            return true;
        }

        foreach (var root in EnumerateCommonRiotInstallRoots())
        {
            if (HasExpectedProductExecutable(root, product))
                return true;
        }

        return false;
    }

    private static bool HasExistingProductExecutable(Game game, string product)
    {
        if (!string.IsNullOrWhiteSpace(game.Executable) &&
            File.Exists(game.Executable) &&
            !Path.GetFileName(game.Executable).Equals("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(game.LaunchUri) &&
            game.LaunchUri.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
            WindowsShortcutService.TryRead(game.LaunchUri, out var shortcut) &&
            shortcut is not null)
        {
            if (!string.IsNullOrWhiteSpace(shortcut.TargetPath) &&
                File.Exists(shortcut.TargetPath) &&
                !Path.GetFileName(shortcut.TargetPath).Equals("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(shortcut.WorkingDirectory) &&
                HasExpectedProductExecutable(shortcut.WorkingDirectory, product))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetInstallPathFromRiotMetadata(string product, out string installPath)
    {
        installPath = string.Empty;

        try
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(programData))
                return false;

            var metadataRoot = Path.Combine(programData, "Riot Games", "Metadata", product);
            if (!Directory.Exists(metadataRoot))
                return false;

            foreach (var file in Directory.EnumerateFiles(metadataRoot, "*.product_settings.yaml", SearchOption.TopDirectoryOnly).Take(8))
            {
                string text;
                try
                {
                    text = SafeFileReadService.ReadAllText(file, 256 * 1024, "metadados de instalação da Riot");
                }
                catch
                {
                    continue;
                }

                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("product_install_full_path:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = trimmed[(trimmed.IndexOf(':') + 1)..].Trim().Trim('"', '\'');
                    value = Environment.ExpandEnvironmentVariables(value.Replace('/', Path.DirectorySeparatorChar));

                    if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                    {
                        installPath = value;
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Metadados da Riot são apenas uma fonte auxiliar de detecção.
        }

        return false;
    }

    private static IEnumerable<string> EnumerateCommonRiotInstallRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
        {
            var baseCandidates = new[]
            {
                Path.Combine(drive.RootDirectory.FullName, "Riot Games"),
                Path.Combine(drive.RootDirectory.FullName, "Program Files", "Riot Games")
            };

            foreach (var candidate in baseCandidates)
            {
                if (seen.Add(candidate))
                    yield return candidate;
            }
        }
    }

    private static bool HasExpectedProductExecutable(string root, string product)
    {
        try
        {
            string[] candidates = product switch
            {
                "league_of_legends" => new[]
                {
                    Path.Combine(root, "LeagueClient.exe"),
                    Path.Combine(root, "League of Legends", "LeagueClient.exe")
                },
                "valorant" => new[]
                {
                    Path.Combine(root, "live", "VALORANT.exe"),
                    Path.Combine(root, "VALORANT", "live", "VALORANT.exe")
                },
                "bacon" => new[]
                {
                    Path.Combine(root, "LoR.exe"),
                    Path.Combine(root, "Legends of Runeterra", "LoR.exe")
                },
                _ => Array.Empty<string>()
            };

            return candidates.Any(candidate =>
                File.Exists(candidate) &&
                LaunchTargetValidator.TryValidateDetectedExecutable(candidate, out _, out _));
        }
        catch
        {
            return false;
        }
    }

    private static string? GetRiotProduct(Game game)
    {
        var text = $"{game.Name} {game.InstallPath} {game.Executable} {game.LaunchUri}";

        if (text.Contains("League of Legends", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("LeagueClient", StringComparison.OrdinalIgnoreCase))
            return "league_of_legends";

        if (text.Contains("VALORANT", StringComparison.OrdinalIgnoreCase))
            return "valorant";

        // Nome interno usado pelo Riot Client para Legends of Runeterra.
        if (text.Contains("Legends of Runeterra", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Runeterra", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("\\LoR\\", StringComparison.OrdinalIgnoreCase))
            return "bacon";

        return null;
    }
}

using Microsoft.Win32;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher.Providers;

public sealed class EAAppProvider : ClientShortcutProvider
{
    private static readonly string[] KnownEaGameRoots =
    {
        @"C:\Program Files\EA Games",
        @"C:\Program Files (x86)\EA Games",
        @"D:\Games",
        @"D:\EA Games",
        @"D:\Program Files\EA Games",
        @"D:\Program Files (x86)\EA Games"
    };

    public override string Name => "EA app";
    public override GamePlatform Platform => GamePlatform.EAApp;
    protected override string ProcessName => "EADesktop";
    protected override string[] LauncherExecutables => new[]
    {
        @"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe",
        @"C:\Program Files (x86)\Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe"
    };

    // Mantemos somente a pasta específica de atalhos do EA para não importar
    // jogos de outros clientes.
    protected override string[] ShortcutDirectoryNames => new[] { "EA Games" };

    protected override string[] RegistrySubKeys => new[]
    {
        @"Software\Electronic Arts\EA Desktop",
        @"Software\WOW6432Node\Electronic Arts\EA Desktop"
    };

    protected override string[] ExcludedShortcutNames => new[] { "EA app", "EA Desktop" };

    public override Task StartClientAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartEaClientAllowingTrustedLocalReparse();
        return Task.CompletedTask;
    }

    // Se o EA app estava fechado, não basta esperar EADesktop.exe aparecer:
    // o processo nasce antes de a interface e os serviços do cliente estarem
    // realmente prontos para receber o comando de iniciar um jogo.
    public override async Task LaunchGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        var wasRunning = IsRunning();

        if (!wasRunning)
        {
            StartEaClientAllowingTrustedLocalReparse();
            await WaitForEaClientReadyAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(game.LaunchUri))
        {
            if (!TryValidateDetectedShortcut(game.LaunchUri, out var shortcut))
                throw new InvalidOperationException("O atalho detectado do EA app não é mais considerado seguro para execução.");
            ProcessService.StartShellFile(shortcut);
        }
        else if (!string.IsNullOrWhiteSpace(game.Executable))
        {
            ProcessService.StartTrustedDetectedLocalReparse(game.Executable);
        }
    }


    private void StartEaClientAllowingTrustedLocalReparse()
    {
        if (IsRunning()) return;

        var executable = FindLauncherExecutable();
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("O EA app não foi encontrado neste computador.");

        // Algumas instalações do próprio EA Desktop também ficam sob diretórios
        // marcados como junction/reparse point. É a mesma exceção controlada usada
        // para os jogos detectados pelo provedor EA: somente caminho local, arquivo
        // .exe existente e executável final não sendo um link.
        ProcessService.StartTrustedDetectedLocalReparse(executable);
    }

    private static async Task WaitForEaClientReadyAsync(CancellationToken cancellationToken)
    {
        // Primeiro esperamos o processo principal existir. Em máquinas mais
        // lentas, atualização automática/login podem fazer isso demorar.
        var processDeadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < processDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (System.Diagnostics.Process.GetProcessesByName("EADesktop").Length > 0)
                break;
            await Task.Delay(500, cancellationToken);
        }

        // Depois exigimos que a janela principal exista e permaneça responsiva
        // por alguns ciclos consecutivos. Isso evita confundir "processo abriu"
        // com "EA app terminou de carregar".
        var readyDeadline = DateTime.UtcNow.AddSeconds(90);
        var stableCycles = 0;
        while (DateTime.UtcNow < readyDeadline && stableCycles < 6)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ready = false;

            foreach (var process in System.Diagnostics.Process.GetProcessesByName("EADesktop"))
            {
                try
                {
                    process.Refresh();
                    if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero && process.Responding)
                    {
                        ready = true;
                        break;
                    }
                }
                catch { }
                finally { process.Dispose(); }
            }

            stableCycles = ready ? stableCycles + 1 : 0;
            if (stableCycles < 6)
                await Task.Delay(1000, cancellationToken);
        }

        // A janela já estar responsiva ainda pode anteceder login, biblioteca e
        // sincronização. Damos uma margem final antes de encaminhar o jogo.
        await Task.Delay(TimeSpan.FromSeconds(12), cancellationToken);
    }

    protected override IEnumerable<Game> DiscoverInstalledGamesWithoutShortcuts()
    {
        var results = new List<Game>();

        // 1) Registro específico do EA Games: é a fonte mais confiável e
        // continua evitando confundir jogos Steam publicados pela EA.
        foreach (var valueName in new[] { "Install Dir", "InstallDir", "InstallLocation" })
        {
            results.AddRange(InstalledGameDiscoveryService.FromRegistryInstallKeys(
                Platform,
                new[]
                {
                    @"SOFTWARE\Electronic Arts\EA Games",
                    @"SOFTWARE\WOW6432Node\Electronic Arts\EA Games"
                },
                valueName,
                (_, install) => Path.GetFileName(install.TrimEnd('\\'))));
        }

        // 2) Pastas padrão e personalizadas. A pasta C:\Program Files\EA Games
        // é uma pasta de instalação real do EA app e, por isso, pode ser usada
        // como fonte mesmo quando um jogo antigo não deixou uma chave de registro
        // utilizável (caso do Medal of Honor Pacific Assault).
        foreach (var root in GetEaInstallRoots())
        {
            results.AddRange(ScanEaInstallRoot(root));
        }

        return results
            .Where(x => !string.IsNullOrWhiteSpace(x.InstallPath))
            .GroupBy(x => NormalizePath(x.InstallPath), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First());
    }

    private static IEnumerable<Game> ScanEaInstallRoot(string root)
    {
        var results = new List<Game>();
        if (!Directory.Exists(root)) return results;

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name) || IsNonGameName(name)) continue;

                // Em D:\Games não podemos considerar qualquer pasta com .exe como EA,
                // pois o usuário também pode ter jogos Steam ali. Exigimos marcadores
                // característicos do EA quando a raiz é genérica.
                var rootIsGeneric = IsGenericCustomRoot(root);
                if (rootIsGeneric && !IsLikelyEaDirectory(directory)) continue;

                var exe = InstalledGameDiscoveryService.FindLikelyGameExe(directory, name);
                if (exe is null) continue;

                results.Add(new Game
                {
                    Id = $"ea-folder:{NormalizePath(directory)}",
                    Name = name.Replace("_", " ").Trim(),
                    Platform = GamePlatform.EAApp,
                    InstallPath = directory,
                    Executable = exe,
                    Installed = true
                });
            }
        }
        catch { }

        return results;
    }

    private static IEnumerable<string> GetEaInstallRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in KnownEaGameRoots)
            roots.Add(root);

        // Se o EA app estiver configurado para uma pasta personalizada, tentamos
        // descobrir o diretório configurado nos arquivos de configuração locais.
        foreach (var root in ReadConfiguredEaInstallRoots())
            roots.Add(root);

        return roots;
    }

    private static IEnumerable<string> ReadConfiguredEaInstallRoots()
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var configRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Electronic Arts",
                "EA Desktop");

            if (!Directory.Exists(configRoot))
                return results;

            foreach (var file in Directory.EnumerateFiles(configRoot, "*.ini", SearchOption.TopDirectoryOnly))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }

                foreach (var line in lines)
                {
                    const string key = "user.downloadinplacedir=";
                    var index = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) continue;

                    var value = line[(index + key.Length)..].Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    value = Environment.ExpandEnvironmentVariables(value).TrimEnd('\\');
                    if (Directory.Exists(value))
                        results.Add(value);
                }
            }
        }
        catch { }

        return results;
    }

    private static bool IsGenericCustomRoot(string root) =>
        root.Equals(@"D:\Games", StringComparison.OrdinalIgnoreCase);

    private static bool IsLikelyEaDirectory(string directory)
    {
        try
        {
            // Marcadores encontrados em instalações modernas do EA app.
            if (Directory.Exists(Path.Combine(directory, "__Installer")))
                return true;

            if (File.Exists(Path.Combine(directory, "EAAntiCheat.GameServiceLauncher.exe")))
                return true;

            if (Directory.Exists(Path.Combine(directory, "__Installer", "EAAntiCheat")))
                return true;

            // Alguns jogos EA mais antigos, como Medal of Honor Pacific Assault,
            // não possuem os marcadores modernos. Nesses casos o nome do executável
            // e a estrutura da pasta ajudam, mas continuamos exigindo um executável
            // antes de adicionar o jogo.
            var exeNames = new[]
            {
                "mohpa.exe",
                "mohpa_demo.exe",
                "bf1942.exe",
                "bf2.exe"
            };

            return exeNames.Any(name => File.Exists(Path.Combine(directory, name)));
        }
        catch { return false; }
    }

    private static bool IsNonGameName(string name) =>
        name.Contains("EA Desktop", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("EA app", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("EA AntiCheat", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("installer", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("update", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string? value) =>
        (value ?? string.Empty).TrimEnd('\\').ToLowerInvariant();
}

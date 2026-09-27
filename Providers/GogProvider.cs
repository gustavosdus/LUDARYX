using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher.Providers;

public sealed class GogProvider : IGameProvider
{
    private const string CustomGamesRoot = @"D:\Games";

    public string Name => "GOG";
    public GamePlatform Platform => GamePlatform.GOG;
    private string? GalaxyRoot => FindGalaxyRoot();

    public bool IsInstalled() =>
        !string.IsNullOrWhiteSpace(GalaxyRoot) ||
        FindDatabasePath() is not null ||
        FindGameRoots().Any(Directory.Exists);

    public bool IsRunning() => ProcessService.IsRunning("GalaxyClient");

    public Task StartClientAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsRunning()) return Task.CompletedTask;

        var exe = FindGalaxyClientExecutable();
        if (string.IsNullOrWhiteSpace(exe))
            throw new InvalidOperationException("O GOG Galaxy não foi encontrado neste computador.");

        ProcessService.StartTrustedDetectedLocalReparse(exe);
        return Task.CompletedTask;
    }

    public Task LaunchGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Jogos GOG instalados pelo Galaxy devem ser iniciados pelo Galaxy, e não
        // diretamente pelo .exe. Isso preserva o fluxo do launcher, incluindo os
        // recursos que dependem dele. O comando /command=runGame é o formato usado
        // pelos atalhos do próprio Galaxy.
        var galaxyExe = FindGalaxyClientExecutable();
        var gameId = GetGogGameId(game);

        if (galaxyExe is not null && !string.IsNullOrWhiteSpace(gameId) &&
            !string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
        {
            var arguments = $"/command=runGame /gameId={gameId} /path=\"{game.InstallPath}\"";
            ProcessService.Start(galaxyExe, arguments);
            return Task.CompletedTask;
        }

        throw new InvalidOperationException($"Não foi possível localizar o GOG Galaxy ou os dados necessários para iniciar \"{game.Name}\" pelo launcher.");
    }

    private static string? GetGogGameId(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.Id) && !game.Id.Equals(game.InstallPath, StringComparison.OrdinalIgnoreCase))
            return game.Id;

        if (string.IsNullOrWhiteSpace(game.InstallPath)) return null;
        return ReadGogId(game.InstallPath);
    }

    private static string? FindGalaxyClientExecutable()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registryRoot = FindGalaxyRoot();
        if (!string.IsNullOrWhiteSpace(registryRoot)) roots.Add(registryRoot);

        roots.Add(@"C:\Program Files (x86)\GOG Galaxy");
        roots.Add(@"C:\Program Files\GOG Galaxy");

        foreach (var root in roots)
        {
            var exe = Path.Combine(root, "GalaxyClient.exe");
            if (File.Exists(exe)) return exe;
        }

        return null;
    }

    public async Task<List<Game>> GetGamesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Game>();

        // O banco só é usado quando conseguimos confirmar que o registro
        // aponta para uma instalação real. Isso evita importar jogos de
        // integrações (por exemplo, Steam) como se fossem GOG.
        var dbPath = FindDatabasePath();
        if (dbPath is not null)
            result.AddRange(await ReadInstalledGalaxyDatabaseAsync(dbPath, cancellationToken));

        // Instalações fora do local padrão: D:\Games e raízes GOG conhecidas.
        result.AddRange(ScanGameDirectories());

        return result
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => Normalize(x.Name), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.Name)
            .ToList();
    }

    private async Task<List<Game>> ReadInstalledGalaxyDatabaseAsync(string dbPath, CancellationToken cancellationToken)
    {
        var result = new List<Game>();
        try
        {
            await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
            await connection.OpenAsync(cancellationToken);

            var tables = await GetTablesAsync(connection, cancellationToken);
            foreach (var table in tables)
            {
                var columns = await GetColumnsAsync(connection, table, cancellationToken);
                var id = FindColumn(columns, "productId", "product_id", "id");
                var title = FindColumn(columns, "title", "name", "gameName", "game_name");
                var installPath = FindColumn(columns,
                    "installationPath", "installation_path", "installPath", "install_path",
                    "installLocation", "install_location", "path", "gamePath", "game_path");

                // Sem caminho de instalação, o registro pode representar uma
                // biblioteca/integração e não um jogo GOG instalado.
                if (id is null || title is null || installPath is null) continue;

                var sql = $"SELECT \"{id}\", \"{title}\", \"{installPath}\" FROM \"{EscapeIdentifier(table)}\"";
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    var gameId = reader.IsDBNull(0) ? "" : reader.GetValue(0)?.ToString() ?? "";
                    var name = reader.IsDBNull(1) ? "" : reader.GetValue(1)?.ToString() ?? "";
                    var install = reader.IsDBNull(2) ? "" : reader.GetValue(2)?.ToString() ?? "";

                    if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(install))
                        continue;
                    if (IsNonGameName(name)) continue;

                    install = Environment.ExpandEnvironmentVariables(install.Trim().TrimEnd('\\'));
                    if (!Directory.Exists(install)) continue;
                    if (!IsLikelyGogDirectory(install)) continue;

                    var exe = InstalledGameDiscoveryService.FindLikelyGameExe(install, name);
                    if (exe is null) continue;

                    result.Add(new Game
                    {
                        Id = gameId,
                        Name = name.Trim(),
                        Platform = Platform,
                        InstallPath = install,
                        Executable = exe,
                        LaunchUri = $"goggalaxy://openGameView/{Uri.EscapeDataString(gameId)}",
                        Installed = true
                    });
                }
            }
        }
        catch
        {
            // Se o banco estiver indisponível, a detecção por arquivos continua.
        }

        return result;
    }

    private IEnumerable<Game> ScanGameDirectories()
    {
        var result = new List<Game>();

        foreach (var root in FindGameRoots().Where(Directory.Exists))
        {
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!IsLikelyGogDirectory(directory)) continue;

                    var folderName = Path.GetFileName(directory);
                    if (string.IsNullOrWhiteSpace(folderName) || IsNonGameName(folderName)) continue;

                    var name = ReadGogName(directory) ?? folderName.Replace("_", " ").Trim();
                    var exe = InstalledGameDiscoveryService.FindLikelyGameExe(directory, name);
                    if (exe is null) continue;

                    var gameId = ReadGogId(directory) ?? directory;
                    result.Add(new Game
                    {
                        Id = gameId,
                        Name = name,
                        Platform = Platform,
                        InstallPath = directory,
                        Executable = exe,
                        Installed = true,
                        LaunchUri = null
                    });
                }
            }
            catch { }
        }

        return result;
    }

    private static bool IsLikelyGogDirectory(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "goggame-*.info", SearchOption.TopDirectoryOnly).Any();
        }
        catch { return false; }
    }

    private static string? ReadGogName(string directory)
    {
        try
        {
            var file = Directory.EnumerateFiles(directory, "goggame-*.info", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (file is null) return null;
            using var doc = JsonDocument.Parse(SafeFileReadService.ReadAllText(file, SafeFileReadService.ManifestMaxBytes, "manifesto GOG"));
            if (doc.RootElement.TryGetProperty("name", out var name)) return name.GetString();
        }
        catch { }
        return null;
    }

    private static string? ReadGogId(string directory)
    {
        try
        {
            var file = Directory.EnumerateFiles(directory, "goggame-*.info", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (file is null) return null;
            using var doc = JsonDocument.Parse(SafeFileReadService.ReadAllText(file, SafeFileReadService.ManifestMaxBytes, "manifesto GOG"));
            if (doc.RootElement.TryGetProperty("gameId", out var id)) return id.ToString();
        }
        catch { }
        return null;
    }

    private static string? FindDatabasePath()
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GOG.com", "Galaxy", "storage", "galaxy-2.0.db"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GOG.com", "Galaxy", "storage", "galaxy-2.0.db")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> FindGameRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CustomGamesRoot,
            @"D:\GOG Games",
            @"D:\GOG\Games",
            @"D:\GOG"
        };

        foreach (var basePath in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            if (!string.IsNullOrWhiteSpace(basePath))
            {
                roots.Add(Path.Combine(basePath, "GOG Games"));
                roots.Add(Path.Combine(basePath, "GOG.com", "Games"));
            }
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "GOG Games"));
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "GOG.com", "Games"));
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "Games", "GOG"));
        }

        return roots;
    }

    private static async Task<List<string>> GetTablesAsync(SqliteConnection c, CancellationToken token)
    {
        var list = new List<string>();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(token);
        while (await r.ReadAsync(token)) list.Add(r.GetString(0));
        return list;
    }

    private static async Task<List<string>> GetColumnsAsync(SqliteConnection c, string table, CancellationToken token)
    {
        var list = new List<string>();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{EscapeIdentifier(table)}\")";
        await using var r = await cmd.ExecuteReaderAsync(token);
        while (await r.ReadAsync(token)) list.Add(r.GetString(1));
        return list;
    }

    private static string? FindColumn(IEnumerable<string> columns, params string[] names) =>
        names.Select(n => columns.FirstOrDefault(c => c.Equals(n, StringComparison.OrdinalIgnoreCase)))
             .FirstOrDefault(c => c is not null);

    private static string EscapeIdentifier(string value) => value.Replace("\"", "\"\"");

    private static bool IsNonGameName(string name) =>
        name.Contains("galaxy", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("installer", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("update", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("redist", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string? FindGalaxyRoot()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var sub in new[] { @"Software\WOW6432Node\GOG.com\GalaxyClient", @"Software\GOG.com\GalaxyClient" })
        {
            using var key = hive.OpenSubKey(sub);
            var path = key?.GetValue("path")?.ToString() ?? key?.GetValue("clientPath")?.ToString();
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return path;
        }
        return null;
    }
}

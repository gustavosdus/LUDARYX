using System.Diagnostics;
using System.Text.Json;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher.Providers;

public sealed class XboxProvider : IGameProvider
{
    public string Name => "Xbox / Microsoft Store";
    public GamePlatform Platform => GamePlatform.Xbox;

    private static readonly HashSet<string> NonGamePackageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.GamingApp",
        "Microsoft.XboxApp",
        "Microsoft.XboxIdentityProvider",
        "Microsoft.XboxGamingOverlay",
        "Microsoft.XboxGameOverlay",
        "Microsoft.XboxSpeechToTextOverlay",
        "Microsoft.Xbox.TCUI",
        "MicrosoftCorporationII.XboxGameBar",
        "Microsoft.WindowsStore",
        "Microsoft.StorePurchaseApp",
        "Microsoft.WindowsCalculator",
        "Microsoft.WindowsCamera",
        "Microsoft.WindowsPhotos",
        "Microsoft.MSPaint",
        "Microsoft.Paint",
        "Microsoft.WindowsNotepad",
        "Microsoft.WindowsTerminal",
        "Microsoft.WindowsAlarms",
        "Microsoft.WindowsSoundRecorder",
        "Microsoft.ScreenSketch",
        "Microsoft.GetHelp",
        "Microsoft.Getstarted",
        "Microsoft.People",
        "Microsoft.YourPhone",
        "Microsoft.WindowsCommunicationsApps",
        "Microsoft.MicrosoftStickyNotes",
        "Microsoft.ZuneMusic",
        "Microsoft.ZuneVideo",
        "Microsoft.BingWeather",
        "Microsoft.BingNews",
        "Microsoft.WindowsMaps",
        "Microsoft.Todos",
        "Microsoft.PowerAutomateDesktop",
        "Microsoft.OutlookForWindows",
        "Microsoft.Windows.DevHome",
        "MicrosoftCorporationII.QuickAssist",
        "MicrosoftCorporationII.MicrosoftFamily",
        "MicrosoftCorporationII.WindowsSubsystemForLinux",
        "Clipchamp.Clipchamp",
        "MSTeams",
        "MicrosoftTeams",
        "Microsoft.Office.OneNote",
        "Microsoft.MicrosoftOfficeHub",
        "Microsoft.Copilot",
        "Microsoft.XboxDevices",
        "Microsoft.XboxInsider",
        "Microsoft.WindowsFeedbackHub",
        "Microsoft.MixedReality.Portal",
        "Microsoft.Microsoft3DViewer",
        "Microsoft.MicrosoftPCManager",
        "Microsoft.SkypeApp",
        "AppleInc.AppleTVWin",
        "AppleInc.AppleDevices",
        "Disney.37853FC22B2CE",
        "4DF9E0F8.Netflix",
        "NVIDIACorp.NVIDIAControlPanel",
        "AmazonVideo.PrimeVideo",
        "SpotifyAB.SpotifyMusic",
        "5319275A.WhatsAppDesktop",
        "1203rocksdanister.LivelyWallpaper",
        "Microsoft.549981C3F5F10", // Cortana
        "AvastSoftware.AvastFreeAntivirus",
        "AvastSoftware.AvastAntivirus",
        "AVGTechnologies.AVGAntivirus",
        "DolbyLaboratories.DolbyAccess",
        "DTSInc.DTSSoundUnbound"
    };

    private static readonly string[] NonGamePackageFragments =
    {
        "xboxdevices", "xboxinsider", "windowsfeedbackhub", "feedbackhub",
        "mixedreality.portal", "microsoft3dviewer", "microsoftpcmanager",
        "skypeapp", "appletv", "appledevices", "netflix", "primevideo",
        "spotifymusic", "whatsappdesktop", "nvidiacontrolpanel",
        "livelywallpaper", "steamgriddbforxbox", "disney",
        "549981c3f5f10", "cortana",
        "avast", "avgantivirus",
        "dolbyaccess", "dtssoundunbound",
        "realtekaudiocontrol", "realtekaudioconsole",
        "intelgraphicscommandcenter", "amdradeonsoftware"
    };

    private static readonly string[] NonGameNameFragments =
    {
        "calculator", "calculadora",
        "camera", "câmera",
        "clock", "relógio", "alarms", "alarmes",
        "notepad", "bloco de notas",
        "paint",
        "photos", "fotos",
        "terminal",
        "microsoft store",
        "store purchase",
        "xbox game bar", "barra de jogo xbox", "xbox identity", "provedor de identidade xbox",
        "xbox console companion", "companheiro do console xbox",
        "feedback hub", "central de comentários",
        "get help", "obter ajuda",
        "tips", "dicas",
        "phone link", "vincular ao celular", "seu telefone",
        "quick assist", "assistência rápida",
        "snipping tool", "ferramenta de captura",
        "sound recorder", "gravador de som", "gravador de voz",
        "sticky notes", "notas autoadesivas",
        "windows maps", "mapas",
        "weather", "clima",
        "microsoft 365", "office hub",
        "outlook", "microsoft teams", "clipchamp",
        "power automate", "dev home", "copilot",
        "acessórios xbox", "acessorios xbox", "xbox accessories",
        "apple tv", "disney+", "disney plus", "dispositivos apple", "apple devices",
        "game bar battery widget", "battery widget",
        "hub de comentários", "hub de comentarios", "feedback hub",
        "lively wallpaper", "mixed reality portal", "portal de realidade misturada",
        "netflix", "nvidia control panel", "painel de controle nvidia",
        "pc manager", "prime video", "skype", "spotify",
        "steamgriddb for xbox", "steamgriddb",
        "visualizador 3d", "3d viewer", "whatsapp",
        "xbox insider hub", "xbox insider",
        "cortana",
        "avast free antivirus", "avast antivirus", "avast premium security",
        "avg antivirus", "avg internet security",
        "norton 360", "norton security", "mcafee", "bitdefender",
        "kaspersky", "malwarebytes",
        "dolby access", "dts sound unbound",
        "realtek audio console", "realtek audio control",
        "intel graphics command center", "amd software",
        "expressvpn", "nordvpn", "surfshark", "proton vpn"
    };

    private static readonly string[] NonGamePublisherFragments =
    {
        "Avast",
        "AVG Technologies",
        "NortonLifeLock",
        "Gen Digital",
        "McAfee",
        "Bitdefender",
        "Kaspersky",
        "Malwarebytes",
        "Dolby Laboratories",
        "DTS, Inc.",
        "Realtek Semiconductor"
    };

    private static readonly HashSet<string> ExactNonGameDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cortana",
        "Avast Free Antivirus",
        "Avast Antivirus",
        "Avast Premium Security",
        "AVG AntiVirus",
        "AVG Internet Security",
        "Dolby Access",
        "DTS Sound Unbound",
        "Realtek Audio Console",
        "Realtek Audio Control",
        "Intel Graphics Command Center",
        "AMD Software",
        "NVIDIA Control Panel",
        "Microsoft PC Manager"
    };

    private static readonly string[] KnownGameNameFragments =
    {
        "forza", "halo", "minecraft", "age of empires", "age of mythology",
        "gears", "killer instinct", "flight simulator", "state of decay",
        "sea of thieves", "grounded", "psychonauts", "ori and the",
        "hellblade", "doom", "quake", "dishonored", "fallout", "skyrim",
        "elder scrolls", "solitaire", "mahjong", "minesweeper", "sudoku"
    };

    public bool IsInstalled() => OperatingSystem.IsWindows();
    public bool IsRunning() => ProcessService.IsRunning("XboxPcApp") || ProcessService.IsRunning("GamingApp");

    public Task StartClientAsync(CancellationToken cancellationToken = default)
    {
        try { ProcessService.StartUri("xbox:"); } catch { }
        return Task.CompletedTask;
    }

    // O Xbox/Microsoft Store administra o caminho físico do pacote; não presumimos C:.
    // Além do nome do atalho, coletamos metadados do pacote para não confundir apps do
    // Windows/Microsoft Store com jogos instalados.
    public async Task<List<Game>> GetGamesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Game>();
        var script = @"
$ErrorActionPreference='SilentlyContinue'
$packages = @{}
Get-AppxPackage -PackageTypeFilter Main | Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage } | ForEach-Object {
    $packages[$_.PackageFamilyName] = $_
}
Get-StartApps | Where-Object { $_.AppID -like '*!*' } | ForEach-Object {
    $family = ($_.AppID -split '!')[0]
    $pkg = $packages[$family]
    [pscustomobject]@{
        Name=$_.Name
        AppID=$_.AppID
        PackageFamilyName=$family
        PackageName=if ($pkg) { $pkg.Name } else { $null }
        Publisher=if ($pkg) { $pkg.Publisher } else { $null }
        NonRemovable=if ($pkg) { [bool]$pkg.NonRemovable } else { $false }
    }
} | ConvertTo-Json -Compress
";
        try
        {
            var powerShellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powerShellPath)) return result;

            var psi = new ProcessStartInfo
            {
                FileName = powerShellPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(powerShellPath)!
            };
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);

            using var process = Process.Start(psi);
            if (process is null) return result;
            var output = await ReadBoundedOutputAsync(process.StandardOutput, 4 * 1024 * 1024, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(output)) return result;

            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind == JsonValueKind.Object) Add(doc.RootElement, result);
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var item in doc.RootElement.EnumerateArray()) Add(item, result);
        }
        catch { }
        return result;
    }

    private static async Task<string> ReadBoundedOutputAsync(StreamReader reader, int maxChars, CancellationToken cancellationToken)
    {
        var builder = new System.Text.StringBuilder(Math.Min(maxChars, 64 * 1024));
        var buffer = new char[8192];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            if (builder.Length + read > maxChars)
                throw new InvalidOperationException("A saída do PowerShell excedeu o limite permitido.");
            builder.Append(buffer, 0, read);
        }
        return builder.ToString();
    }

    private static void Add(JsonElement item, List<Game> result)
    {
        var name = ReadString(item, "Name");
        var appId = ReadString(item, "AppID");
        var packageFamilyName = ReadString(item, "PackageFamilyName");
        var packageName = ReadString(item, "PackageName");
        var publisher = ReadString(item, "Publisher");
        var nonRemovable = item.TryGetProperty("NonRemovable", out var nr) &&
                           (nr.ValueKind == JsonValueKind.True ||
                            (nr.ValueKind == JsonValueKind.String && bool.TryParse(nr.GetString(), out var parsed) && parsed));

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId)) return;
        if (IsClearlyNonGame(name, packageName, packageFamilyName, appId, publisher, nonRemovable)) return;

        result.Add(new Game
        {
            Id = appId,
            Name = NormalizeXboxDisplayName(name),
            Platform = GamePlatform.Xbox,
            AppUserModelId = appId,
            PackageFamilyName = packageFamilyName,
            LaunchUri = $"shell:AppsFolder\\{appId}"
        });
    }


    private static string NormalizeXboxDisplayName(string name)
    {
        var value = name.Trim();

        // Alguns atalhos do Xbox adicionam qualificadores que não fazem parte do
        // título comercial (ex.: "Doom Eternal - PC"). Mantê-los quebra buscas
        // exatas em Steam, PCGamingWiki e SteamGridDB. Removemos apenas sufixos
        // claramente técnicos, sem alterar títulos legítimos no restante do nome.
        var technicalSuffixes = new[]
        {
            " - PC",
            " (PC)",
            " - Windows",
            " (Windows)"
        };

        foreach (var suffix in technicalSuffixes)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^suffix.Length].TrimEnd();
                break;
            }
        }

        return string.IsNullOrWhiteSpace(value) ? name.Trim() : value;
    }

    private static string? ReadString(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool IsClearlyNonGame(
        string displayName,
        string? packageName,
        string? packageFamilyName,
        string appId,
        string? publisher,
        bool nonRemovable)
    {
        var trimmedName = displayName.Trim();
        var normalizedName = trimmedName.ToLowerInvariant();

        if (ExactNonGameDisplayNames.Contains(trimmedName))
            return true;

        // Alguns jogos publicados pela própria Microsoft também podem ter nomes de pacote
        // começando com "Microsoft". Uma pequena allowlist de franquias conhecidas impede
        // que o filtro de componentes do Windows remova esses jogos por engano.
        if (KnownGameNameFragments.Any(fragment => normalizedName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrWhiteSpace(packageName) && NonGamePackageNames.Contains(packageName))
            return true;

        var packageIdentity = string.Join("|", new[] { packageName, packageFamilyName, appId }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        if (NonGamePackageFragments.Any(fragment =>
                packageIdentity.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (NonGameNameFragments.Any(fragment => normalizedName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (!string.IsNullOrWhiteSpace(publisher) &&
            NonGamePublisherFragments.Any(fragment =>
                publisher.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Pacotes Microsoft não removíveis são, em regra, componentes do Windows e não jogos.
        // Só aplicamos esta regra ao publisher da Microsoft para não afetar jogos de terceiros.
        if (nonRemovable && !string.IsNullOrWhiteSpace(publisher) &&
            publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public async Task LaunchGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        // Abre/traz o Xbox app para frente antes de iniciar o pacote do jogo.
        // Isso deixa o comportamento consistente com os demais launchers: o
        // usuário vê primeiro a interface da plataforma e só depois o jogo.
        await StartClientAsync(cancellationToken);
        await WaitForClientReadyAsync(cancellationToken);

        ProcessService.StartUri(game.LaunchUri ?? "xbox:");
    }

    private async Task WaitForClientReadyAsync(CancellationToken cancellationToken)
    {
        // Em uma inicialização fria o processo XboxPcApp pode levar alguns segundos
        // para aparecer. Quando ele já está aberto, ainda damos um pequeno tempo
        // para que o protocolo xbox: restaure/traga a janela para frente.
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline && !IsRunning())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken);
        }

        await Task.Delay(IsRunning() ? 1200 : 1800, cancellationToken);
    }
}

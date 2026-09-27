using System.Text.Json.Serialization;

namespace UnifiedGameLauncher.Models;

public sealed class LauncherSettings
{
    public bool ShowStoreApps { get; set; } = true;
    public bool StartClientsAutomatically { get; set; } = true;
    public bool KeepLauncherOpen { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool StartFullscreen { get; set; } = false;
    public bool ShowHiddenApps { get; set; } = false;
    public List<string> HiddenGameIds { get; set; } = new();
    public List<string> ExcludedGameIds { get; set; } = new();
    public List<string> FavoriteGameIds { get; set; } = new();
    public Dictionary<string, DateTime> LastPlayedUtc { get; set; } = new();
    public Dictionary<string, int> PlayCounts { get; set; } = new();
    public Dictionary<string, ManualGameMetadata> ManualMetadata { get; set; } = new();
    public string CoverMode { get; set; } = "Auto";
    public bool EnrichMetadataAutomatically { get; set; } = true;
    public bool UseSteamStoreMetadata { get; set; } = true;
    public bool UseIgdbMetadata { get; set; } = false;
    public List<ManualGameDefinition> ManualGames { get; set; } = new();
    [JsonIgnore]
    public string? IgdbClientId { get; set; }
    [JsonIgnore]
    public string? IgdbClientSecret { get; set; }
    [JsonIgnore]
    public string? SteamGridDbApiKey { get; set; }

    // Segredos persistidos somente em formato protegido pela DPAPI do Windows.
    public string? ProtectedIgdbClientId { get; set; }
    public string? ProtectedIgdbClientSecret { get; set; }
    public string? ProtectedSteamGridDbApiKey { get; set; }

    // Personalização visual
    public string NeonLineColor { get; set; } = "Red";
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "pt-BR";
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }
    public Dictionary<string, int> SteamGridDbGameIds { get; set; } = new();
}

public sealed class ManualGameMetadata
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string> Genres { get; set; } = new();
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public int? ReleaseYear { get; set; }
    public string? HorizontalCoverUrl { get; set; }
    public string? VerticalCoverUrl { get; set; }
    public bool DisableAutomaticSteamGridDbVertical { get; set; }
    public bool DisableAutomaticSteamGridDbHorizontal { get; set; }
}

public sealed class ManualGameDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Novo jogo";
    public string? Executable { get; set; }
    public string? Arguments { get; set; }
    public string? LaunchUri { get; set; }
    public string? CoverPath { get; set; }
}

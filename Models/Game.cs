using System.ComponentModel;
using System.Runtime.CompilerServices;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher.Models;

public sealed class Game : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    // Manual titles belong to settings; automatic titles stay in the metadata cache.
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, string> ManualLocalizedNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string DisplayName
    {
        get
        {
            var manual = LocalizedMetadataText.Exact(ManualLocalizedNames, LocalizationService.CurrentLanguage);
            return !string.IsNullOrWhiteSpace(manual) ? manual :
                LocalizedMetadataText.GetName(Metadata, LocalizationService.CurrentLanguage) ?? Name;
        }
    }
    public GamePlatform Platform
    {
        get; set;
    }
    public bool Installed { get; set; } = true;
    public string? InstallPath
    {
        get; set;
    }
    public string? Executable
    {
        get; set;
    }
    public string? LaunchArguments
    {
        get; set;
    }
    public string? CoverImage
    {
        get; set;
    }
    public string? LaunchUri
    {
        get; set;
    }
    public string? AppUserModelId
    {
        get; set;
    }
    public string? PackageFamilyName
    {
        get; set;
    }
    public string? StoreId
    {
        get; set;
    }
    public GameMetadata Metadata { get; set; } = new();

    public bool IsFavorite
    {
        get; set;
    }
    public DateTime? LastPlayedUtc
    {
        get; set;
    }
    public int PlayCount
    {
        get; set;
    }
    public long TotalPlayTimeSeconds
    {
        get; set;
    }
    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value)
                return;
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RunningDisplay));
        }
    }
    public bool IsHidden
    {
        get; set;
    }
    public bool IsExcluded
    {
        get; set;
    }
    public string CanonicalGameId { get; set; } = "";
    public bool IsDuplicate
    {
        get; set;
    }
    public string DuplicatePlatformsDisplay { get; set; } = "";

    public string GenresDisplay => string.Join(" • ", Metadata.Genres);
    public string ReleaseYearDisplay => Metadata.ReleaseYear?.ToString() ?? "";
    public string LastPlayedDisplay => LocalizationService.FormatLastPlayed(LastPlayedUtc);
    public string PlayCountDisplay => LocalizationService.FormatPlayCount(PlayCount);
    public string TotalPlayTimeDisplay => LocalizationService.FormatDuration(TotalPlayTimeSeconds);
    public string DuplicateBadgeLabel => LocalizationService.DuplicateLabel;
    public string RunningDisplay => IsRunning ? LocalizationService.Translate("Jogando") : "";
    public string PlatformDisplay => LocalizationService.Translate(Platform.ToString());
    public string ProviderId => $"{Platform}:{Id}";
    public string DisplayCover => CoverImage ?? "";
    public string HeroArtwork => Metadata.CustomHorizontalCoverLocalPath
        ?? Metadata.HorizontalCoverLocalPath
        ?? Metadata.CustomVerticalCoverLocalPath
        ?? Metadata.VerticalCoverLocalPath
        ?? Metadata.CoverLocalPath
        ?? CoverImage
        ?? "";
    public bool IsManual => Platform == GamePlatform.Manual;
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public bool HasHorizontalCover => !string.IsNullOrWhiteSpace(HorizontalCover);
    public bool HasVerticalCover => !string.IsNullOrWhiteSpace(VerticalCover);
    public string HorizontalCover => Metadata.CustomHorizontalCoverLocalPath
        ?? Metadata.HorizontalCoverLocalPath
        ?? (string.IsNullOrWhiteSpace(Metadata.VerticalCoverLocalPath) ? Metadata.CoverLocalPath : null)
        ?? CoverImage
        ?? "";
    public string VerticalCover => Metadata.CustomVerticalCoverLocalPath
        ?? Metadata.VerticalCoverLocalPath
        ?? (string.IsNullOrWhiteSpace(Metadata.HorizontalCoverLocalPath) ? Metadata.CoverLocalPath : null)
        ?? "";
    private bool _isControllerSelected;
    public bool IsControllerSelected
    {
        get => _isControllerSelected;
        set
        {
            if (_isControllerSelected == value)
                return;
            _isControllerSelected = value;
            OnPropertyChanged();
        }
    }
    public double CoverWidth { get; set; } = 220;
    public double CoverHeight { get; set; } = 124;

    public void RefreshLocalizedLabels()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(PlatformDisplay));
        OnPropertyChanged(nameof(LastPlayedDisplay));
        OnPropertyChanged(nameof(PlayCountDisplay));
        OnPropertyChanged(nameof(TotalPlayTimeDisplay));
        OnPropertyChanged(nameof(RunningDisplay));
        OnPropertyChanged(nameof(DuplicateBadgeLabel));
        OnPropertyChanged(nameof(DuplicatePlatformsDisplay));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}


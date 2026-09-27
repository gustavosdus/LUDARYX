using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UnifiedGameLauncher.Models;

public sealed class Game : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public GamePlatform Platform { get; set; }
    public bool Installed { get; set; } = true;
    public string? InstallPath { get; set; }
    public string? Executable { get; set; }
    public string? LaunchArguments { get; set; }
    public string? CoverImage { get; set; }
    public string? LaunchUri { get; set; }
    public string? AppUserModelId { get; set; }
    public string? PackageFamilyName { get; set; }
    public string? StoreId { get; set; }
    public GameMetadata Metadata { get; set; } = new();

    public bool IsFavorite { get; set; }
    public DateTime? LastPlayedUtc { get; set; }
    public int PlayCount { get; set; }
    public long TotalPlayTimeSeconds { get; set; }
    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RunningDisplay));
        }
    }
    public bool IsHidden { get; set; }
    public bool IsExcluded { get; set; }
    public string CanonicalGameId { get; set; } = "";
    public bool IsDuplicate { get; set; }
    public string DuplicatePlatformsDisplay { get; set; } = "";

    public string GenresDisplay => string.Join(" • ", Metadata.Genres);
    public string ReleaseYearDisplay => Metadata.ReleaseYear?.ToString() ?? "";
    public string LastPlayedDisplay => LastPlayedUtc.HasValue ? LastPlayedUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "Nunca jogado";
    public string PlayCountDisplay => PlayCount == 1 ? "1 partida" : $"{PlayCount} partidas";
    public string TotalPlayTimeDisplay
    {
        get
        {
            if (TotalPlayTimeSeconds <= 0) return "0 min";
            var duration = TimeSpan.FromSeconds(TotalPlayTimeSeconds);
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes:D2}min";
            return $"{Math.Max(1, duration.Minutes)} min";
        }
    }
    public string RunningDisplay => IsRunning ? "Jogando" : "";
    public string PlatformDisplay => Platform.ToString();
    public string ProviderId => $"{Platform}:{Id}";
    public string DisplayCover => CoverImage ?? "";
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
            if (_isControllerSelected == value) return;
            _isControllerSelected = value;
            OnPropertyChanged();
        }
    }
    public double CoverWidth { get; set; } = 220;
    public double CoverHeight { get; set; } = 124;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}


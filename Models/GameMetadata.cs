namespace UnifiedGameLauncher.Models;

public sealed class GameMetadata
{
    public string CanonicalName { get; set; } = "";
    public string? Description { get; set; }
    public Dictionary<string, string> LocalizedDescriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Genres { get; set; } = new();
    public List<string> Platforms { get; set; } = new();
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public int? ReleaseYear { get; set; }
    public Dictionary<string, string> AgeRatings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? CoverUrl { get; set; }
    public string? CoverLocalPath { get; set; }
    public string? HorizontalCoverUrl { get; set; }
    public string? HorizontalCoverLocalPath { get; set; }
    public string? VerticalCoverUrl { get; set; }
    public string? VerticalCoverLocalPath { get; set; }
    public string? CustomHorizontalCoverLocalPath { get; set; }
    public string? CustomVerticalCoverLocalPath { get; set; }
    public string? Source { get; set; }
    public string? ExternalId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

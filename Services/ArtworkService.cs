using Microsoft.Win32;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed class ArtworkService
{
    private readonly string _directory;

    public ArtworkService()
    {
        _directory = Path.Combine(AppDataService.RootDirectory, "custom-covers");
        Directory.CreateDirectory(_directory);
    }

    public bool ChooseAndSave(Game game, bool vertical)
    {
        var dialog = new OpenFileDialog
        {
            Title = vertical ? "Escolher capa vertical" : "Escolher arte horizontal",
            Filter = "Imagens suportadas|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return false;

        var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".bmp"
        };
        if (!allowedExtensions.Contains(extension))
            throw new InvalidOperationException("Formato de imagem não suportado.");

        // Usa exatamente os bytes validados, evitando reabrir a origem depois da checagem.
        var validatedBytes = SafeImageDownloadService.ReadAndValidateLocalImage(dialog.FileName);
        var safeId = string.Concat(game.ProviderId.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var target = Path.Combine(_directory, $"{safeId}_{(vertical ? "vertical" : "horizontal")}{extension}");
        File.WriteAllBytes(target, validatedBytes);

        if (vertical) game.Metadata.CustomVerticalCoverLocalPath = target;
        else game.Metadata.CustomHorizontalCoverLocalPath = target;
        return true;
    }

    public void Restore(Game game, bool vertical)
    {
        var path = vertical ? game.Metadata.CustomVerticalCoverLocalPath : game.Metadata.CustomHorizontalCoverLocalPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
        if (vertical) game.Metadata.CustomVerticalCoverLocalPath = null;
        else game.Metadata.CustomHorizontalCoverLocalPath = null;
    }
}

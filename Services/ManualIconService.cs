using System.Drawing;
using System.Drawing.Imaging;

namespace UnifiedGameLauncher.Services;

public static class ManualIconService
{
    public static string SaveIconCopy(string sourcePath, string manualGameId)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("O arquivo de ícone não foi encontrado.", sourcePath);

        var directory = Path.Combine(AppDataService.RootDirectory, "custom-covers", "manual-icons");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"{manualGameId}_icon.png");

        var extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
        {
            using var icon = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                ? Icon.ExtractAssociatedIcon(sourcePath)
                : new Icon(sourcePath);

            if (icon is null)
                throw new InvalidDataException("Não foi possível extrair um ícone válido.");

            using var bitmap = icon.ToBitmap();
            bitmap.Save(target, ImageFormat.Png);
            return target;
        }

        using var image = Image.FromFile(sourcePath);
        using var bitmapCopy = new Bitmap(image);
        bitmapCopy.Save(target, ImageFormat.Png);
        return target;
    }
}

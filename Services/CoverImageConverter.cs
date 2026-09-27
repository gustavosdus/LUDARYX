using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace UnifiedGameLauncher.Services;

/// <summary>Carrega somente capas locais já validadas, reduzindo CPU, memória e acesso indireto à rede.</summary>
public sealed class CoverImageConverter : IValueConverter
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, BitmapImage> Cache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxCachedImages = 180;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            // A interface nunca busca imagens diretamente da Internet/UNC. Toda arte remota
            // precisa passar antes pelo SafeImageDownloadService e virar um arquivo local.
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme != Uri.UriSchemeFile)
                return null;

            var fullPath = Path.GetFullPath(path);
            if (!LaunchTargetValidator.IsLocalFilePath(fullPath) || !File.Exists(fullPath)) return null;

            // O caminho sozinho não identifica a versão da arte. Capas oficiais da
            // Steam podem ser substituídas no mesmo URL/arquivo; inclui timestamp e
            // tamanho no cache para o WPF recarregar o bitmap quando o arquivo mudar.
            var info = new FileInfo(fullPath);
            var key = $"{fullPath}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
            lock (Sync)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
            }

            var bytes = SafeImageDownloadService.ReadAndValidateLocalImage(fullPath);
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 320;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            if (bitmap.CanFreeze) bitmap.Freeze();

            lock (Sync)
            {
                if (Cache.Count >= MaxCachedImages)
                {
                    var first = Cache.Keys.FirstOrDefault();
                    if (first is not null) Cache.Remove(first);
                }
                Cache[key] = bitmap;
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

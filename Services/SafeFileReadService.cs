using System.Text;

namespace UnifiedGameLauncher.Services;

public static class SafeFileReadService
{
    public const int SettingsMaxBytes = 4 * 1024 * 1024;
    public const int MetadataCacheMaxBytes = 16 * 1024 * 1024;
    public const int ManifestMaxBytes = 8 * 1024 * 1024;

    public static string ReadAllText(string path, int maxBytes, string description)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        if (stream.Length > maxBytes)
            throw new InvalidOperationException($"O arquivo de {description} excede o limite permitido.");

        using var memory = new MemoryStream((int)Math.Min(stream.Length, maxBytes));
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total += read;
            if (total > maxBytes)
                throw new InvalidOperationException($"O arquivo de {description} excede o limite permitido.");
            memory.Write(buffer, 0, read);
        }

        memory.Position = 0;
        using var reader = new StreamReader(memory, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

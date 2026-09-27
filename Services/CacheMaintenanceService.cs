namespace UnifiedGameLauncher.Services;

public static class CacheMaintenanceService
{
    public static long GetCacheSizeBytes()
    {
        return GetDirectorySize(Path.Combine(AppDataService.RootDirectory, "covers"));
    }

    public static long CleanupToLimit(int maxSizeMb)
    {
        var directory = Path.Combine(AppDataService.RootDirectory, "covers");
        if (!Directory.Exists(directory))
            return 0;

        var limitBytes = Math.Max(128, maxSizeMb) * 1024L * 1024L;
        var files = new DirectoryInfo(directory)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .OrderBy(file => file.LastAccessTimeUtc)
            .ThenBy(file => file.LastWriteTimeUtc)
            .ToList();

        var total = files.Sum(file => SafeLength(file));
        if (total <= limitBytes)
            return total;

        foreach (var file in files)
        {
            if (total <= limitBytes)
                break;

            try
            {
                var length = file.Length;
                file.Delete();
                total -= length;
            }
            catch
            {
            }
        }

        return Math.Max(0, total);
    }

    public static void ClearDownloadCache()
    {
        var directory = Path.Combine(AppDataService.RootDirectory, "covers");
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static long GetDirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
            return 0;

        long total = 0;
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories))
            total += SafeLength(file);
        return total;
    }

    private static long SafeLength(FileInfo file)
    {
        try { return file.Length; }
        catch { return 0; }
    }
}

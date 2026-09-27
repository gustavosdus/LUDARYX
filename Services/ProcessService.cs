using System.Diagnostics;

namespace UnifiedGameLauncher.Services;

public static class ProcessService
{
    public static bool IsRunning(string processName)
    {
        var name = Path.GetFileNameWithoutExtension(processName);
        return Process.GetProcessesByName(name).Length > 0;
    }

    public static Process? Start(string fileName, string? arguments = null)
    {
        if (!LaunchTargetValidator.TryValidateDetectedExecutable(fileName, out var executable, out var error) ||
            string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException(error ?? "O executável não pôde ser validado para inicialização.");

        return Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments ?? "",
            UseShellExecute = true
        });
    }


    public static Process? StartTrustedDetectedLocalReparse(string fileName, string? arguments = null)
    {
        if (!LaunchTargetValidator.TryValidateDetectedExecutableAllowingTrustedLocalReparse(
                fileName, out var executable, out var error) ||
            string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException(error ?? "O executável não pôde ser validado para inicialização.");

        return Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments ?? "",
            UseShellExecute = true
        });
    }

    public static Process? StartUri(string uri)
    {
        if (!LaunchTargetValidator.TryValidateUri(uri, out var normalized, out var error) ||
            string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException(error ?? "A URI de inicialização não é permitida.");

        return Process.Start(new ProcessStartInfo
        {
            FileName = normalized,
            UseShellExecute = true
        });
    }

    public static Process? StartShellFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("O atalho está vazio.");

        var fullPath = Path.GetFullPath(path);
        if (!LaunchTargetValidator.IsSafeLocalFilePath(fullPath) ||
            !fullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
            throw new InvalidOperationException("O atalho não é um arquivo .lnk local seguro.");

        return Process.Start(new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true
        });
    }
}

using System.Runtime.InteropServices;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Lê atalhos do Windows sem executá-los. Usado apenas para validar atalhos
/// descobertos automaticamente antes de adicioná-los à biblioteca.
/// </summary>
public static class WindowsShortcutService
{
    public sealed record ShortcutInfo(string TargetPath, string Arguments, string WorkingDirectory);

    public static bool TryRead(string shortcutPath, out ShortcutInfo? info)
    {
        info = null;

        try
        {
            var fullPath = Path.GetFullPath(shortcutPath);
            if (!File.Exists(fullPath) ||
                !fullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                !LaunchTargetValidator.IsSafeLocalFilePath(fullPath))
            {
                return false;
            }

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return false;

            object? shell = null;
            object? shortcut = null;

            try
            {
                shell = Activator.CreateInstance(shellType);
                if (shell is null)
                    return false;

                dynamic dynamicShell = shell;
                shortcut = dynamicShell.CreateShortcut(fullPath);
                if (shortcut is null)
                    return false;

                dynamic dynamicShortcut = shortcut;
                var target = Environment.ExpandEnvironmentVariables((string?)dynamicShortcut.TargetPath ?? string.Empty).Trim();
                var arguments = ((string?)dynamicShortcut.Arguments ?? string.Empty).Trim();
                var workingDirectory = Environment.ExpandEnvironmentVariables((string?)dynamicShortcut.WorkingDirectory ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(target))
                    return false;

                info = new ShortcutInfo(target, arguments, workingDirectory);
                return true;
            }
            finally
            {
                if (shortcut is not null && Marshal.IsComObject(shortcut))
                    Marshal.FinalReleaseComObject(shortcut);

                if (shell is not null && Marshal.IsComObject(shell))
                    Marshal.FinalReleaseComObject(shell);
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool PointsToExistingLocalTarget(string shortcutPath)
    {
        if (!TryRead(shortcutPath, out var info) || info is null)
            return false;

        try
        {
            var target = Path.GetFullPath(info.TargetPath);
            return File.Exists(target) && LaunchTargetValidator.IsSafeLocalFilePath(target);
        }
        catch
        {
            return false;
        }
    }
}

using Microsoft.Win32;

namespace UnifiedGameLauncher.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LUDARYX";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Não foi possível acessar a inicialização automática do Windows.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        key.SetValue(ValueName, BuildStartupCommand(), RegistryValueKind.String);
    }

    private static string BuildStartupCommand()
    {
        var processPath = Environment.ProcessPath;

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            if (!File.Exists(processPath) || !LaunchTargetValidator.IsSafeLocalFilePath(processPath))
                throw new InvalidOperationException("O executável atual não está em um caminho local seguro.");

            var fileName = Path.GetFileName(processPath);
            if (fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                var appBaseDirectory = AppContext.BaseDirectory;
                var entryDll = Path.Combine(appBaseDirectory, "LUDARYX.dll");

                if (!File.Exists(entryDll) || !LaunchTargetValidator.IsSafeLocalFilePath(entryDll))
                    throw new InvalidOperationException("O assembly do LUDARYX não está em um caminho local seguro.");

                return $"\"{processPath}\" \"{entryDll}\"";
            }

            return $"\"{processPath}\"";
        }

        var fallbackExe = Path.Combine(AppContext.BaseDirectory, "LUDARYX.exe");
        if (File.Exists(fallbackExe) && LaunchTargetValidator.IsSafeLocalFilePath(fallbackExe))
            return $"\"{fallbackExe}\"";

        throw new InvalidOperationException("Não foi possível determinar com segurança o executável do LUDARYX.");
    }
}

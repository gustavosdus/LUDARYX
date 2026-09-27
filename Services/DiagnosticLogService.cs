using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Log local e minimalista para diagnóstico. Não registra configurações, biblioteca,
/// API keys ou tokens. Caminhos conhecidos do perfil do Windows são reduzidos para
/// marcadores antes da gravação para facilitar o compartilhamento seguro do log.
/// </summary>
public static class DiagnosticLogService
{
    private static readonly object Sync = new();
    private const int MaxLogFileBytes = 2 * 1024 * 1024;

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UnifiedGameLauncher",
        "Logs");

    public static string CurrentLogPath => Path.Combine(LogDirectory, "ludaryx.log");

    public static void LogInfo(string message) => Write("INFO", message);

    public static void LogException(string context, Exception exception)
    {
        var text = $"{context}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}";
        Write("ERROR", text);
    }

    public static string GetSystemSummary()
    {
        var assembly = Assembly.GetExecutingAssembly().GetName();
        var version = assembly.Version?.ToString(3) ?? "1.0.0";
        var architecture = Environment.Is64BitProcess ? "x64" : "x86";

        return string.Join(Environment.NewLine,
            $"LUDARYX: {version}",
            $"Windows: {Environment.OSVersion.VersionString}",
            $".NET: {Environment.Version}",
            $"Processo: {architecture}");
    }

    public static void OpenLogDirectory()
    {
        Directory.CreateDirectory(LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{LogDirectory}\"",
            UseShellExecute = true
        });
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            lock (Sync)
            {
                RotateIfNeeded();
                var sanitized = Sanitize(message);
                File.AppendAllText(
                    CurrentLogPath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {sanitized}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnóstico nunca pode impedir o uso do launcher.
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(CurrentLogPath)) return;
        var info = new FileInfo(CurrentLogPath);
        if (info.Length < MaxLogFileBytes) return;

        var previous = Path.Combine(LogDirectory, "ludaryx.previous.log");
        File.Move(CurrentLogPath, previous, true);
    }

    private static string Sanitize(string value)
    {
        var result = value ?? string.Empty;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        result = ReplacePath(result, localAppData, "%LOCALAPPDATA%");
        result = ReplacePath(result, appData, "%APPDATA%");
        result = ReplacePath(result, profile, "%USERPROFILE%");

        // Defesa adicional para mensagens de bibliotecas externas que eventualmente
        // incluam um segredo em texto. Não depende de conhecer o valor real da chave.
        result = Regex.Replace(
            result,
            @"(?i)(api[_ -]?key|client[_ -]?secret|access[_ -]?token|authorization|bearer)(\s*[:=]\s*|\s+)[^\s,;&]+",
            "$1$2[REDACTED]");
        return result;
    }

    private static string ReplacePath(string value, string path, string replacement) =>
        string.IsNullOrWhiteSpace(path)
            ? value
            : value.Replace(path, replacement, StringComparison.OrdinalIgnoreCase);
}

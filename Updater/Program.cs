using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;

namespace LUDARYX.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            if (!options.TryGetValue("wait-pid", out var waitPidText) ||
                !int.TryParse(waitPidText, NumberStyles.None, CultureInfo.InvariantCulture, out var waitPid) ||
                waitPid <= 0)
                throw new ArgumentException("PID do LUDARYX inválido.");

            if (!options.TryGetValue("installer", out var installerPath) ||
                string.IsNullOrWhiteSpace(installerPath))
                throw new ArgumentException("Caminho do instalador não informado.");

            installerPath = Path.GetFullPath(installerPath);
            if (!File.Exists(installerPath) ||
                !installerPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("O instalador da atualização não foi encontrado.", installerPath);

            WaitForProcessExit(waitPid, TimeSpan.FromSeconds(60));

            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Environment.CurrentDirectory
            });

            if (options.TryGetValue("cleanup-dir", out var cleanupDirectory) &&
                !string.IsNullOrWhiteSpace(cleanupDirectory))
            {
                ScheduleSelfCleanup(cleanupDirectory);
            }

            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível iniciar a atualização do LUDARYX.\n\n{ex.Message}",
                "LUDARYX Updater",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (!current.StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                continue;

            result[current[2..]] = args[++i];
        }
        return result;
    }

    private static void WaitForProcessExit(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return;

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                throw new TimeoutException("O LUDARYX não encerrou a tempo. Feche-o completamente e tente novamente.");
        }
        catch (ArgumentException)
        {
            // O processo já encerrou entre o lançamento do updater e esta verificação.
        }
    }

    private static void ScheduleSelfCleanup(string cleanupDirectory)
    {
        var fullCleanupDirectory = Path.GetFullPath(cleanupDirectory);
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (!fullCleanupDirectory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
            return;

        var cmd = Environment.GetEnvironmentVariable("COMSPEC");
        if (string.IsNullOrWhiteSpace(cmd) || !File.Exists(cmd))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = $"/d /c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"{fullCleanupDirectory}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}

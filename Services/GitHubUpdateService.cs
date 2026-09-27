using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace UnifiedGameLauncher.Services;

public static class GitHubUpdateService
{
    private const string RepositoryOwner = "gustavosdus";
    private const string RepositoryName = "LUDARYX";
    private const int MaxChecksumFileBytes = 1024 * 1024;
    private static readonly Uri LatestReleaseApiUri = new($"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest");

    public static string GetCurrentVersionDisplay()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public static async Task<GitHubUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUri);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var tagName = ReadString(root, "tag_name") ?? string.Empty;
        var releaseName = ReadString(root, "name") ?? tagName;
        var htmlUrl = ReadString(root, "html_url");
        var releaseNotes = ReadString(root, "body");

        var latestVersionDisplay = NormalizeVersionDisplay(tagName);
        var currentVersionDisplay = NormalizeVersionDisplay(GetCurrentVersionDisplay());
        var currentVersion = ParseVersion(currentVersionDisplay);
        var latestVersion = ParseVersion(latestVersionDisplay);

        string? installerName = null;
        string? installerDownloadUrl = null;
        string? checksumDownloadUrl = null;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = ReadString(asset, "name");
                var url = ReadString(asset, "browser_download_url");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                    continue;

                if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                {
                    checksumDownloadUrl = url;
                    continue;
                }

                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (installerDownloadUrl is null || name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                {
                    installerName = name;
                    installerDownloadUrl = url;
                }
            }
        }

        return new GitHubUpdateInfo(
            IsUpdateAvailable: latestVersion > currentVersion,
            CurrentVersionDisplay: currentVersionDisplay,
            LatestVersionDisplay: latestVersionDisplay,
            ReleaseName: releaseName,
            ReleaseNotes: releaseNotes,
            ReleasePageUrl: htmlUrl,
            InstallerName: installerName,
            InstallerDownloadUrl: installerDownloadUrl,
            ChecksumDownloadUrl: checksumDownloadUrl);
    }

    public static async Task<bool> PromptAndDownloadUpdateAsync(
        Window owner,
        GitHubUpdateInfo updateInfo,
        CancellationToken cancellationToken)
    {
        if (!updateInfo.IsUpdateAvailable)
            return false;

        var notes = BuildReleaseNotesPreview(updateInfo.ReleaseNotes);
        var initialChoice = MessageBox.Show(
            owner,
            $"A versão {updateInfo.LatestVersionDisplay} está disponível no GitHub.\n\n" +
            (string.IsNullOrWhiteSpace(notes) ? "" : $"Novidades:\n{notes}\n\n") +
            "Sim: baixar e instalar agora.\nNão: lembrar mais tarde.\nCancelar: abrir as novidades no GitHub.",
            "Atualização do LUDARYX",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Information);

        if (initialChoice == MessageBoxResult.Cancel)
        {
            OpenReleasePageIfAvailable(updateInfo);
            return false;
        }

        if (initialChoice != MessageBoxResult.Yes)
            return false;

        if (string.IsNullOrWhiteSpace(updateInfo.InstallerDownloadUrl) ||
            string.IsNullOrWhiteSpace(updateInfo.InstallerName))
        {
            OpenReleasePageIfAvailable(updateInfo);
            MessageBox.Show(
                owner,
                "Nenhum instalador compatível foi encontrado na release mais recente. A página da release será aberta no navegador.",
                "Atualização do LUDARYX",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (string.IsNullOrWhiteSpace(updateInfo.ChecksumDownloadUrl))
        {
            OpenReleasePageIfAvailable(updateInfo);
            MessageBox.Show(
                owner,
                "A release mais recente não contém SHA256SUMS.txt. Por segurança, a instalação automática foi bloqueada. A página da release será aberta para download manual.",
                "Atualização do LUDARYX",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var progressWindow = new UpdateProgressWindow { Owner = owner };
        progressWindow.Show();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            progressWindow.CancellationToken);

        var progress = new Progress<UpdateDownloadProgress>(value => progressWindow.Report(value));
        string downloadedPath;

        try
        {
            downloadedPath = await DownloadAndVerifyInstallerAsync(updateInfo, progress, linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            progressWindow.Close();
            return false;
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            DiagnosticLogService.LogException("Could not download or validate update installer", ex);

            var openReleaseChoice = MessageBox.Show(
                owner,
                $"A atualização automática não pôde ser concluída com segurança.\n\n{ex.Message}\n\nDeseja abrir a página da release no GitHub?",
                "Atualização do LUDARYX",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (openReleaseChoice == MessageBoxResult.Yes)
                OpenReleasePageIfAvailable(updateInfo);

            return false;
        }

        progressWindow.Close();

        var runInstallerChoice = MessageBox.Show(
            owner,
            $"A atualização {updateInfo.LatestVersionDisplay} foi baixada e o SHA-256 foi validado com sucesso.\n\nDeseja fechar o LUDARYX e iniciar a atualização agora?",
            "Atualização do LUDARYX",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (runInstallerChoice != MessageBoxResult.Yes)
            return false;

        LaunchExternalUpdater(downloadedPath);
        return true;
    }

    private static async Task<string> DownloadAndVerifyInstallerAsync(
        GitHubUpdateInfo updateInfo,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var expectedHash = await DownloadExpectedSha256Async(updateInfo, cancellationToken);
        if (string.IsNullOrWhiteSpace(expectedHash))
            throw new InvalidDataException($"SHA-256 do arquivo {updateInfo.InstallerName} não foi encontrado em SHA256SUMS.txt.");

        var updatesDir = Path.Combine(
            AppDataService.RootDirectory,
            "Updates",
            updateInfo.LatestVersionDisplay);
        Directory.CreateDirectory(updatesDir);

        var safeFileName = Path.GetFileName(updateInfo.InstallerName!);
        if (!safeFileName.Equals(updateInfo.InstallerName, StringComparison.Ordinal) ||
            !safeFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Nome de instalador inválido recebido da release.");

        var finalPath = Path.Combine(updatesDir, safeFileName);
        var tempPath = finalPath + ".part";

        if (File.Exists(finalPath))
        {
            progress?.Report(new UpdateDownloadProgress("Validando arquivo já baixado...", null, string.Empty));
            if (await VerifySha256Async(finalPath, expectedHash, cancellationToken))
                return finalPath;

            try { File.Delete(finalPath); } catch { }
        }

        if (File.Exists(tempPath))
        {
            try { File.Delete(tempPath); } catch { }
        }

        using var client = CreateHttpClient(TimeSpan.FromMinutes(15));
        using var response = await client.GetAsync(
            updateInfo.InstallerDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var buffer = new byte[128 * 1024];
        long received = 0;

        progress?.Report(new UpdateDownloadProgress("Baixando instalador do GitHub...", 0, FormatProgress(0, totalBytes)));

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                    break;

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;

                double? percent = totalBytes is > 0
                    ? received * 100d / totalBytes.Value
                    : null;

                progress?.Report(new UpdateDownloadProgress(
                    "Baixando instalador do GitHub...",
                    percent,
                    FormatProgress(received, totalBytes)));
            }
        }

        progress?.Report(new UpdateDownloadProgress("Validando SHA-256...", 100, "Verificando integridade"));

        if (!await VerifySha256Async(tempPath, expectedHash, cancellationToken))
        {
            try { File.Delete(tempPath); } catch { }
            throw new InvalidDataException("O SHA-256 do instalador baixado não corresponde ao valor publicado em SHA256SUMS.txt.");
        }

        File.Move(tempPath, finalPath, true);
        progress?.Report(new UpdateDownloadProgress("Download concluído e verificado.", 100, "100%"));
        return finalPath;
    }

    private static async Task<string?> DownloadExpectedSha256Async(
        GitHubUpdateInfo updateInfo,
        CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient(TimeSpan.FromSeconds(30));
        using var response = await client.GetAsync(updateInfo.ChecksumDownloadUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > MaxChecksumFileBytes)
            throw new InvalidDataException("SHA256SUMS.txt excede o tamanho máximo permitido.");

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.Length > MaxChecksumFileBytes)
            throw new InvalidDataException("SHA256SUMS.txt excede o tamanho máximo permitido.");

        foreach (var rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length < 64)
                continue;

            var hash = line[..64];
            if (!hash.All(Uri.IsHexDigit))
                continue;

            var remainder = line[64..].TrimStart(' ', '\t', '*');
            if (remainder.Equals(updateInfo.InstallerName, StringComparison.OrdinalIgnoreCase))
                return hash.ToLowerInvariant();
        }

        return null;
    }

    private static async Task<bool> VerifySha256Async(string path, string expectedHash, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            useAsync: true);

        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual),
            Convert.FromHexString(expectedHash));
    }

    private static void LaunchExternalUpdater(string installerPath)
    {
        var installedUpdaterPath = Path.Combine(AppContext.BaseDirectory, "LUDARYX.Updater.exe");
        if (!File.Exists(installedUpdaterPath))
            throw new FileNotFoundException(
                "O componente LUDARYX.Updater.exe não foi encontrado. Reinstale o LUDARYX ou execute o instalador baixado manualmente.",
                installedUpdaterPath);

        var tempUpdaterDirectory = Path.Combine(
            Path.GetTempPath(),
            "LUDARYX-Updater",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUpdaterDirectory);

        var tempUpdaterPath = Path.Combine(tempUpdaterDirectory, "LUDARYX.Updater.exe");
        File.Copy(installedUpdaterPath, tempUpdaterPath, true);

        var currentProcess = Process.GetCurrentProcess();
        var psi = new ProcessStartInfo
        {
            FileName = tempUpdaterPath,
            UseShellExecute = true,
            WorkingDirectory = tempUpdaterDirectory
        };
        psi.ArgumentList.Add("--wait-pid");
        psi.ArgumentList.Add(currentProcess.Id.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--installer");
        psi.ArgumentList.Add(installerPath);
        psi.ArgumentList.Add("--cleanup-dir");
        psi.ArgumentList.Add(tempUpdaterDirectory);

        Process.Start(psi);
    }

    private static void OpenReleasePageIfAvailable(GitHubUpdateInfo updateInfo)
    {
        if (!string.IsNullOrWhiteSpace(updateInfo.ReleasePageUrl))
            ProcessService.StartUri(updateInfo.ReleasePageUrl);
    }

    private static HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{RepositoryName}-Updater/{GetCurrentVersionDisplay()}");
        return client;
    }

    private static string NormalizeVersionDisplay(string? versionText)
    {
        var value = (versionText ?? string.Empty).Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            value = value[1..];
        return string.IsNullOrWhiteSpace(value) ? "0.0.0" : value;
    }

    private static Version ParseVersion(string? versionText)
    {
        var normalized = NormalizeVersionDisplay(versionText);
        return Version.TryParse(normalized, out var version)
            ? version
            : new Version(0, 0, 0);
    }

    private static string FormatProgress(long receivedBytes, long? totalBytes)
    {
        var received = FormatBytes(receivedBytes);
        return totalBytes is > 0
            ? $"{received} / {FormatBytes(totalBytes.Value)}"
            : received;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private static string BuildReleaseNotesPreview(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return string.Empty;

        var lines = notes
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("#"))
            .Take(5)
            .Select(line => line.TrimStart('-', '*', ' '))
            .Where(line => !string.IsNullOrWhiteSpace(line));

        var preview = string.Join(Environment.NewLine, lines);
        return preview.Length <= 700 ? preview : preview[..700] + "…";
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

public sealed record GitHubUpdateInfo(
    bool IsUpdateAvailable,
    string CurrentVersionDisplay,
    string LatestVersionDisplay,
    string ReleaseName,
    string? ReleaseNotes,
    string? ReleasePageUrl,
    string? InstallerName,
    string? InstallerDownloadUrl,
    string? ChecksumDownloadUrl);

public sealed record UpdateDownloadProgress(
    string Status,
    double? Percent,
    string DisplayText);

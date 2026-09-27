using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public static class LaunchTargetValidator
{
    private static readonly HashSet<string> AllowedUriSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam",
        "com.epicgames.launcher",
        "uplay",
        "goggalaxy",
        "riotclient",
        "xbox",
        "shell"
    };

    public static bool TryValidateExecutable(string? executable, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(executable)) return true;

        try
        {
            var fullPath = Path.GetFullPath(executable.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(fullPath))
            {
                error = "Use o caminho completo do executável.";
                return false;
            }
            if (IsNetworkPath(fullPath))
            {
                error = "Por segurança, jogos manuais precisam usar um executável em um disco local.";
                return false;
            }
            if (!fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                error = "Jogos manuais só podem apontar para arquivos .exe.";
                return false;
            }
            if (!File.Exists(fullPath))
            {
                error = "O executável informado não existe.";
                return false;
            }
            if (HasReparsePointInPath(fullPath))
            {
                error = "O caminho do executável atravessa um link/junction e foi bloqueado por segurança.";
                return false;
            }
            normalized = fullPath;
            return true;
        }
        catch
        {
            error = "O caminho do executável é inválido.";
            return false;
        }
    }

    public static bool TryValidateDetectedExecutable(string? executable, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(executable))
        {
            error = "O executável está vazio.";
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(executable.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(fullPath) || IsNetworkPath(fullPath))
            {
                error = "Por segurança, somente executáveis locais podem ser iniciados.";
                return false;
            }
            if (!fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                error = "O executável detectado não existe ou não é um arquivo .exe válido.";
                return false;
            }
            if (HasReparsePointInPath(fullPath))
            {
                error = "O caminho do executável detectado atravessa um link/junction e foi bloqueado por segurança.";
                return false;
            }
            normalized = fullPath;
            return true;
        }
        catch
        {
            error = "O caminho do executável detectado é inválido.";
            return false;
        }
    }


    public static bool TryValidateDetectedExecutableAllowingTrustedLocalReparse(
        string? executable,
        out string? normalized,
        out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(executable))
        {
            error = "O executável está vazio.";
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(executable.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(fullPath) || IsNetworkPath(fullPath))
            {
                error = "Por segurança, somente executáveis locais podem ser iniciados.";
                return false;
            }

            if (!fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                error = "O executável detectado não existe ou não é um arquivo .exe válido.";
                return false;
            }

            // O próprio arquivo executável nunca pode ser um symlink/reparse point.
            // A exceção abaixo existe apenas para diretórios junction usados por
            // instalações legítimas do EA app.
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                error = "O executável detectado é um link/reparse point e foi bloqueado por segurança.";
                return false;
            }

            if (!AreAllDirectoryReparsePointsLocal(fullPath, out error))
                return false;

            normalized = fullPath;
            return true;
        }
        catch
        {
            error = "O caminho do executável detectado é inválido.";
            return false;
        }
    }

    private static bool AreAllDirectoryReparsePointsLocal(string fullPath, out string? error)
    {
        error = null;
        try
        {
            DirectoryInfo? directory = new FileInfo(fullPath).Directory;
            while (directory is not null)
            {
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    FileSystemInfo? target = directory.ResolveLinkTarget(returnFinalTarget: true);
                    if (target is null)
                    {
                        error = "Não foi possível validar o destino de um link/junction do executável.";
                        return false;
                    }

                    var targetPath = Path.GetFullPath(target.FullName);
                    if (!Path.IsPathFullyQualified(targetPath) || IsNetworkPath(targetPath))
                    {
                        error = "Um link/junction do executável aponta para um local de rede e foi bloqueado por segurança.";
                        return false;
                    }
                }

                directory = directory.Parent;
            }

            return true;
        }
        catch
        {
            error = "Não foi possível validar com segurança um link/junction no caminho do executável.";
            return false;
        }
    }

    public static bool IsLocalFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            return Path.IsPathFullyQualified(fullPath) && !IsNetworkPath(fullPath);
        }
        catch { return false; }
    }

    public static bool IsSafeLocalFilePath(string? path)
    {
        if (!IsLocalFilePath(path) || string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            return !HasReparsePointInPath(fullPath);
        }
        catch { return false; }
    }

    private static bool HasReparsePointInPath(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                return true;

            DirectoryInfo? directory = File.Exists(fullPath)
                ? new FileInfo(fullPath).Directory
                : new DirectoryInfo(fullPath);

            while (directory is not null)
            {
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    return true;
                directory = directory.Parent;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsNetworkPath(string fullPath)
    {
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal)) return true;

        try
        {
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root)) return true;
            return new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch
        {
            return true;
        }
    }

    public static bool TryValidateUri(string? value, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        var raw = value.Trim();
        const string shellPrefix = "shell:AppsFolder\\";
        if (raw.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            if (!raw.StartsWith(shellPrefix, StringComparison.OrdinalIgnoreCase))
            {
                error = "Somente destinos shell:AppsFolder do Xbox/Microsoft Store são permitidos.";
                return false;
            }

            var appId = raw[shellPrefix.Length..];
            if (string.IsNullOrWhiteSpace(appId) || appId.Length > 512 ||
                !System.Text.RegularExpressions.Regex.IsMatch(
                    appId,
                    @"^[A-Za-z0-9][A-Za-z0-9._-]{0,255}![A-Za-z0-9][A-Za-z0-9._-]{0,255}$",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            {
                error = "O AppUserModelId do Xbox/Microsoft Store é inválido.";
                return false;
            }

            normalized = shellPrefix + appId;
            return true;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            error = "A URI de inicialização é inválida.";
            return false;
        }
        if (!AllowedUriSchemes.Contains(uri.Scheme))
        {
            error = $"O protocolo '{uri.Scheme}' não é permitido para inicialização.";
            return false;
        }

        normalized = uri.AbsoluteUri;
        return true;
    }

    public static void ValidateForLaunch(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.LaunchUri))
        {
            if (!TryValidateUri(game.LaunchUri, out var uri, out var error))
                throw new InvalidOperationException(error);
            game.LaunchUri = uri;
            return;
        }

        if (!TryValidateExecutable(game.Executable, out var exe, out var exeError) || string.IsNullOrWhiteSpace(exe))
            throw new InvalidOperationException(exeError ?? "O jogo manual não possui um executável válido.");
        game.Executable = exe;

        if (!string.IsNullOrEmpty(game.LaunchArguments) && game.LaunchArguments.Length > 4096)
            throw new InvalidOperationException("Os argumentos de inicialização são maiores que o limite permitido.");
    }
}

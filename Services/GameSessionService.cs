using System.Diagnostics;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed class GameSessionService : IDisposable
{
    private readonly Dictionary<string, CancellationTokenSource> _tracking = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly GameStateService _state = new();

    public bool IsRunning(Game game)
    {
        var processName = GetProcessName(game);
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        try
        {
            return Process.GetProcessesByName(processName).Any(process => !process.HasExited);
        }
        catch
        {
            return false;
        }
    }

    public async Task RefreshRunningStateAsync(IEnumerable<Game> games)
    {
        var snapshot = games.ToList();
        var states = await Task.Run(() =>
            snapshot.Select(game => (Game: game, Running: IsRunning(game))).ToList());

        foreach (var state in states)
            SetRunning(state.Game, state.Running);
    }

    public void TrackAfterLaunch(Game game, LauncherSettings settings)
    {
        lock (_sync)
        {
            if (_tracking.ContainsKey(game.ProviderId))
                return;

            var cts = new CancellationTokenSource();
            _tracking[game.ProviderId] = cts;
            _ = Task.Run(() => TrackSessionAsync(game, settings, cts.Token));
        }
    }

    private async Task TrackSessionAsync(Game game, LauncherSettings settings, CancellationToken token)
    {
        try
        {
            var processName = GetProcessName(game);
            if (string.IsNullOrWhiteSpace(processName))
                return;

            Process? process = null;
            var waitUntil = DateTime.UtcNow.AddMinutes(2);

            while (!token.IsCancellationRequested && DateTime.UtcNow < waitUntil)
            {
                process = FindProcess(processName);
                if (process is not null)
                    break;
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }

            if (process is null)
                return;

            SetRunning(game, true);
            var lastTick = DateTime.UtcNow;
            var unsaved = TimeSpan.Zero;

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);

                if (process.HasExited)
                    break;

                var now = DateTime.UtcNow;
                var delta = now - lastTick;
                lastTick = now;
                unsaved += delta;

                if (unsaved >= TimeSpan.FromSeconds(30))
                {
                    _state.AddPlayTime(game, settings, unsaved);
                    unsaved = TimeSpan.Zero;
                }
            }

            if (unsaved > TimeSpan.Zero)
                _state.AddPlayTime(game, settings, unsaved);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException($"Session tracking failed for {game.Name}", ex);
        }
        finally
        {
            SetRunning(game, false);
            lock (_sync)
            {
                if (_tracking.Remove(game.ProviderId, out var cts))
                    cts.Dispose();
            }
        }
    }

    private static Process? FindProcess(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName)
                .FirstOrDefault(process =>
                {
                    try { return !process.HasExited; }
                    catch { return false; }
                });
        }
        catch
        {
            return null;
        }
    }

    private static string? GetProcessName(Game game)
    {
        var executable = game.Executable;

        if (string.IsNullOrWhiteSpace(executable) &&
            !string.IsNullOrWhiteSpace(game.InstallPath) &&
            Directory.Exists(game.InstallPath))
        {
            // Steam/Epic e alguns outros providers iniciam por URI e não armazenam
            // o executável no modelo. Para estatísticas locais, tenta descobrir o
            // executável principal dentro da pasta instalada sem alterar o modo de launch.
            executable = InstalledGameDiscoveryService.FindLikelyGameExe(game.InstallPath, game.Name);
        }

        if (string.IsNullOrWhiteSpace(executable))
            return null;

        try
        {
            return Path.GetFileNameWithoutExtension(executable);
        }
        catch
        {
            return null;
        }
    }

    private static void SetRunning(Game game, bool running)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            game.IsRunning = running;
            return;
        }

        dispatcher.BeginInvoke(() => game.IsRunning = running);
    }

    public void Dispose()
    {
        List<CancellationTokenSource> sources;
        lock (_sync)
        {
            sources = _tracking.Values.ToList();
            _tracking.Clear();
        }

        foreach (var source in sources)
        {
            source.Cancel();
            source.Dispose();
        }
    }
}

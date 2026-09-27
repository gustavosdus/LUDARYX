using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Providers;

namespace UnifiedGameLauncher.Services;

public sealed class LibraryService
{
    private static readonly TimeSpan ProviderDiscoveryTimeout = TimeSpan.FromSeconds(15);
    private readonly List<IGameProvider> _providers;

    public LibraryService()
    {
        _providers = new()
        {
            new SteamProvider(),
            new EpicProvider(),
            new GogProvider(),
            new XboxProvider(),
            new EAAppProvider(),
            new UbisoftConnectProvider(),
            new BattleNetProvider(),
            new RiotClientProvider(),
            new ManualProvider()
        };
    }

    public IReadOnlyList<IGameProvider> Providers => _providers;

    public async Task<List<Game>> LoadLibraryAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _providers
            .Where(p => p.IsInstalled())
            .Select(async p =>
            {
                try
                {
                    using var providerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    providerCts.CancelAfter(ProviderDiscoveryTimeout);
                    return await p.GetGamesAsync(providerCts.Token)
                        .WaitAsync(ProviderDiscoveryTimeout, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return new List<Game>();
                }
                catch
                {
                    return new List<Game>();
                }
            });

        var groups = await Task.WhenAll(tasks);
        return groups.SelectMany(x => x)
            .GroupBy(g => g.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Name)
            .ToList();
    }

    public IGameProvider? GetProvider(GamePlatform platform) => _providers.FirstOrDefault(p => p.Platform == platform);
}

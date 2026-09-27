using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher.Providers;

public sealed class BattleNetProvider : ClientShortcutProvider
{
    public override string Name => "Battle.net";
    public override GamePlatform Platform => GamePlatform.BattleNet;
    protected override string ProcessName => "Battle.net";
    protected override string[] LauncherExecutables => new[] { @"C:\Program Files (x86)\Battle.net\Battle.net.exe", @"C:\Program Files\Battle.net\Battle.net.exe" };
    protected override string[] ShortcutDirectoryNames => new[] { "Battle.net", "Blizzard Entertainment", "Blizzard" };
    protected override string[] RegistrySubKeys => new[] { @"Software\Blizzard Entertainment", @"Software\WOW6432Node\Blizzard Entertainment" };
    protected override string[] ExcludedShortcutNames => new[] { "Battle.net", "Blizzard Battle.net" };

    // InstallLocation do registro pode apontar para qualquer unidade/pasta escolhida pelo usuário.
    protected override IEnumerable<Game> DiscoverInstalledGamesWithoutShortcuts() =>
        InstalledGameDiscoveryService.FromRegistryUninstall(Platform, new[] { "Blizzard", "Activision" }, new[] { "Battle.net", "Blizzard Update Agent" });
}

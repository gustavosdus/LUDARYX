using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed record IntegrationStatusItem(string Name, string Status, bool Available);

public static class IntegrationStatusService
{
    public static IReadOnlyList<IntegrationStatusItem> GetStatuses(LauncherSettings settings)
    {
        var library = new LibraryService();
        var items = library.Providers
            .Where(provider => provider.Platform != GamePlatform.Manual)
            .Select(provider =>
            {
                var available = false;
                try { available = provider.IsInstalled(); } catch { }
                return new IntegrationStatusItem(
                    provider.Name,
                    LocalizationService.Translate(available ? "Detectado" : "Não encontrado"),
                    available);
            })
            .ToList();

        items.Add(new IntegrationStatusItem(
            "SteamGridDB",
            LocalizationService.Translate(string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey) ? "API não configurada" : "API configurada"),
            !string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey)));

        items.Add(new IntegrationStatusItem("GitHub Updates", LocalizationService.Translate("Disponível"), true));
        return items;
    }
}

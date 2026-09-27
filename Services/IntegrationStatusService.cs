using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed record IntegrationStatusItem(
    string Name,
    string Status,
    bool Available,
    GamePlatform? Platform = null,
    bool CanOpenClient = false,
    string? Details = null,
    bool IsRunning = false);

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
                var running = false;
                try { available = provider.IsInstalled(); } catch { }
                try { running = available && provider.IsRunning(); } catch { }

                var diagnostic = IntegrationDiagnosticService.Get(provider.Platform);
                var status = !available
                    ? LocalizationService.Translate("Não encontrado")
                    : running
                        ? LocalizationService.Translate("Em execução")
                        : LocalizationService.Translate("Detectado");

                return new IntegrationStatusItem(
                    provider.Name,
                    status,
                    available,
                    provider.Platform,
                    available,
                    diagnostic.Summary,
                    running);
            })
            .ToList();

        items.Add(new IntegrationStatusItem(
            "SteamGridDB",
            LocalizationService.Translate(string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey) ? "API não configurada" : "API configurada"),
            !string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey),
            Details: "API de capas opcional"));

        items.Add(new IntegrationStatusItem(
            "GitHub Updates",
            LocalizationService.Translate("Disponível"),
            true,
            Details: "GitHub Releases"));

        return items;
    }
}

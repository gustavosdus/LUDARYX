using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed class GameLaunchService
{
    private readonly LibraryService _library;

    public GameLaunchService(LibraryService library) => _library = library;

    public async Task LaunchAsync(Game game, CancellationToken cancellationToken = default)
    {
        var provider = _library.GetProvider(game.Platform)
            ?? throw new InvalidOperationException($"A integração {game.Platform} não foi encontrada pelo LUDARYX.");

        try
        {
            if (game.Platform != GamePlatform.Manual && !provider.IsInstalled())
                throw new InvalidOperationException(
                    $"O cliente {provider.Name} não foi encontrado neste computador. Verifique a instalação do launcher e tente novamente.");

            await provider.LaunchGameAsync(game, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException($"Launch failed: {provider.Name} / {game.Name}", ex);

            if (ex is InvalidOperationException)
                throw;

            throw new InvalidOperationException(
                $"Falha ao iniciar {game.Name} pela integração {provider.Name}. " +
                "Confira o Status das integrações em Configurações e tente novamente.\n\n" +
                $"Detalhe: {ex.Message}", ex);
        }
    }
}

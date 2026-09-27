using System.Threading;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Garante que exista apenas uma instância do LUDARYX por sessão do Windows.
/// Uma segunda execução apenas sinaliza a instância existente para restaurar a janela.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\LUDARYX.SingleInstance.1.0.0";
    private const string ActivationEventName = @"Local\LUDARYX.Activate.1.0.0";

    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _activationEvent;
    private readonly CancellationTokenSource? _cancellation;

    public bool IsPrimaryInstance { get; }

    public SingleInstanceService(Action activateExistingWindow)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsPrimaryInstance = createdNew;

        if (!createdNew)
        {
            SignalPrimaryInstance();
            return;
        }

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _cancellation = new CancellationTokenSource();

        _ = Task.Run(() => ListenForActivationRequests(activateExistingWindow, _cancellation.Token));
    }

    private void ListenForActivationRequests(Action activateExistingWindow, CancellationToken cancellationToken)
    {
        if (_activationEvent is null)
            return;

        var handles = new WaitHandle[] { _activationEvent, cancellationToken.WaitHandle };

        while (!cancellationToken.IsCancellationRequested)
        {
            var signaled = WaitHandle.WaitAny(handles);
            if (signaled == 1)
                break;

            try
            {
                activateExistingWindow();
            }
            catch
            {
                // A ativação é apenas uma conveniência. Nunca encerra a instância principal.
            }
        }
    }

    private static void SignalPrimaryInstance()
    {
        // Há uma pequena janela de tempo entre a criação do mutex e do evento.
        // Fazemos algumas tentativas curtas para cobrir um duplo clique muito rápido.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                activationEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        try { _cancellation?.Cancel(); } catch { }
        try { _activationEvent?.Dispose(); } catch { }

        if (IsPrimaryInstance)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
        }

        try { _mutex?.Dispose(); } catch { }
        try { _cancellation?.Dispose(); } catch { }
    }
}

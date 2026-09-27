using System.Windows;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        // O processo deve continuar ativo enquanto o LUDARYX estiver na bandeja.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            DiagnosticLogService.LogException("Unhandled dispatcher exception", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                DiagnosticLogService.LogException("Unhandled application exception", exception);
        };
        DiagnosticLogService.LogInfo("Application startup. " + DiagnosticLogService.GetSystemSummary().Replace(Environment.NewLine, " | "));

        _singleInstance = new SingleInstanceService(() =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is UnifiedGameLauncher.MainWindow mainWindow)
                    mainWindow.RestoreFromExternalLaunch();
            });
        });

        if (!_singleInstance.IsPrimaryInstance)
        {
            Shutdown(0);
            return;
        }

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException("Startup failed", ex);
            System.Windows.MessageBox.Show(
                $"O LUDARYX não conseguiu abrir.\n\n{ex}",
                "LUDARYX - erro de inicialização",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

}

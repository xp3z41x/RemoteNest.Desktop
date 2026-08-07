using System.Windows;
using RemoteNest.Data;
using RemoteNest.Localization;
using RemoteNest.Services;
using RemoteNest.ViewModels;
using RemoteNest.Views;

namespace RemoteNest;

public partial class App : Application
{
    private RdpLauncherService? _rdpLauncher;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Wire global exception handlers FIRST so early startup failures are captured.
        WireGlobalExceptionHandlers();

        Log.Initialize();

        // Elevated helper mode: apply one machine-wide registry change and exit without UI.
        // (The normal instance runs unelevated and shells out to this via the UAC prompt.)
        if (e.Args.Length == 2 && e.Args[0] == "--rdp-policy")
        {
            Shutdown(RdpWarningPolicyService.RunElevatedOperation(e.Args[1]));
            return;
        }

        Log.Info("RemoteNest starting up");

        try
        {
            LanguageManager.Initialize();
            AcrylicHelper.Initialize();
            Services.ThemeManager.Initialize();

            // Schema creation takes a few milliseconds — run it inline so the window
            // never races the database.
            var database = new Database(GetDefaultDbPath());
            database.EnsureCreated();

            IEncryptionService encryptionService = new EncryptionService();
            IConnectionService connectionService = new ConnectionService(database);
            _rdpLauncher = new RdpLauncherService();
            IDialogService dialogService = new DialogService();

            var mainVm = new MainViewModel(
                connectionService,
                encryptionService,
                _rdpLauncher,
                dialogService);

            var mainWindow = new MainWindow(mainVm);
            AcrylicHelper.Track(mainWindow);
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Critical("Startup failed", ex);
            MessageBox.Show(
                $"{TranslationSource.Get("StartupErrorMessage")}\n\n{ex.Message}",
                TranslationSource.Get("StartupError"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info($"RemoteNest shutting down (exit code {e.ApplicationExitCode})");
        _rdpLauncher?.Dispose();
        base.OnExit(e);
    }

    private void WireGlobalExceptionHandlers()
    {
        // UI thread exceptions — mark handled so the app doesn't terminate on recoverable faults.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled dispatcher exception", args.Exception);
            try
            {
                MessageBox.Show(
                    $"{TranslationSource.Get("ErrorOccurred")}\n\n{args.Exception.Message}",
                    TranslationSource.Get("ErrorOccurred"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* swallow — we're already in an exception handler */ }
            args.Handled = true;

            // If the fault escaped before any window existed, swallowing it would leave a
            // windowless process running forever (ShutdownMode is OnLastWindowClose).
            if (MainWindow is null)
                Shutdown(1);
        };

        // Non-UI thread exceptions — cannot recover, but log before the process terminates.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log.Critical($"Unhandled AppDomain exception (terminating={args.IsTerminating})", ex);
            else
                Log.Critical($"Unhandled AppDomain exception (non-CLR object, terminating={args.IsTerminating})");
        };

        // Fire-and-forget Task exceptions that are never awaited.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved Task exception", args.Exception);
            args.SetObserved();
        };
    }

    private static string GetDefaultDbPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "RemoteNest");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "remotenest.db");
    }
}

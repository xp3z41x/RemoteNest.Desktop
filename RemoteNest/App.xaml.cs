using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RemoteNest.Data;
using RemoteNest.Localization;
using RemoteNest.Services;
using RemoteNest.ViewModels;
using RemoteNest.Views;

namespace RemoteNest;

public partial class App : Application
{
    private RdpLauncherService? _rdpLauncher;
    private FileLoggerProvider? _loggerProvider;
    private ILoggerFactory? _loggerFactory;
    private ILogger? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Wire global exception handlers FIRST so early startup failures are captured.
        WireGlobalExceptionHandlers();

        _loggerProvider = new FileLoggerProvider();
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(_loggerProvider);
        });
        _logger = _loggerFactory.CreateLogger("RemoteNest.App");
        _logger.LogInformation("RemoteNest starting up");

        LanguageManager.Initialize();
        Services.ThemeManager.Initialize();

        try
        {
            // Build DbContextOptions once, share via factory. OnConfiguring's default
            // path is bypassed because the options are pre-configured here.
            var dbPath = GetDefaultDbPath();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using (var db = new AppDbContext(options))
            {
                await db.Database.MigrateAsync();
            }

            var factory = new AppDbContextFactory(options);
            IEncryptionService encryptionService = new EncryptionService();
            IConnectionService connectionService = new ConnectionService(factory);
            _rdpLauncher = new RdpLauncherService(_loggerFactory.CreateLogger<RdpLauncherService>());
            IDialogService dialogService = new DialogService();

            var mainVm = new MainViewModel(
                connectionService,
                encryptionService,
                _rdpLauncher,
                dialogService,
                _loggerFactory.CreateLogger<MainViewModel>());

            var mainWindow = new MainWindow(mainVm);
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "Startup failed");
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
        _logger?.LogInformation("RemoteNest shutting down (exit code {Code})", e.ApplicationExitCode);
        _rdpLauncher?.Dispose();
        _loggerFactory?.Dispose();
        _loggerProvider?.Dispose();
        base.OnExit(e);
    }

    private void WireGlobalExceptionHandlers()
    {
        // UI thread exceptions — mark handled so the app doesn't terminate on recoverable faults.
        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.LogError(args.Exception, "Unhandled dispatcher exception");
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
        };

        // Non-UI thread exceptions — cannot recover, but log before the process terminates.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                _logger?.LogCritical(ex, "Unhandled AppDomain exception (terminating={Terminating})", args.IsTerminating);
            else
                _logger?.LogCritical("Unhandled AppDomain exception (non-CLR object, terminating={Terminating})", args.IsTerminating);
        };

        // Fire-and-forget Task exceptions that are never awaited.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.LogError(args.Exception, "Unobserved Task exception");
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

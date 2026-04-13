using System.Windows;
using Microsoft.EntityFrameworkCore;
using RemoteNest.Data;
using RemoteNest.Localization;
using RemoteNest.Services;
using RemoteNest.ViewModels;
using RemoteNest.Views;

namespace RemoteNest;

public partial class App : Application
{
    private RdpLauncherService? _rdpLauncher;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LanguageManager.Initialize();

        try
        {
            using (var db = new AppDbContext())
            {
                await db.Database.MigrateAsync();
            }

            var factory = new AppDbContextFactory();
            IEncryptionService encryptionService = new EncryptionService();
            IConnectionService connectionService = new ConnectionService(factory);
            _rdpLauncher = new RdpLauncherService();

            var mainVm = new MainViewModel(connectionService, encryptionService, _rdpLauncher);
            var mainWindow = new MainWindow(mainVm);
            mainWindow.Show();
        }
        catch (Exception ex)
        {
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
        _rdpLauncher?.Dispose();
        base.OnExit(e);
    }
}

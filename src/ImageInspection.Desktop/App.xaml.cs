using System.Windows;
using VideoInferenceDemo;

namespace VideoInferenceDemo.ImageInspection;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var databasePaths = WorkspaceDatabaseBootstrap.Initialize(AppContext.BaseDirectory);
        var personnelRepository = new PersonnelRepository(databasePaths.ConfigDbPath);
        var authenticationService = new PersonnelAuthenticationService(personnelRepository);
        var loginWindow = new LoginWindow(authenticationService);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (loginWindow.ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        var mainWindow = new MainWindow(authenticationService, databasePaths);
        MainWindow = mainWindow;
        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }
}

using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NewLife.Log;

namespace VideoInferenceDemo;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        XTrace.UseConsole();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
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

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnCurrentDomainUnhandledException;
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CameraDiagnostics.Error("app", "Unhandled WPF dispatcher exception.", e.Exception);
    }

    private static void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            CameraDiagnostics.Error("app", $"Unhandled AppDomain exception. IsTerminating={e.IsTerminating}", ex);
            return;
        }

        CameraDiagnostics.Error("app", $"Unhandled AppDomain exception. IsTerminating={e.IsTerminating}, ExceptionObject={e.ExceptionObject}");
    }
}

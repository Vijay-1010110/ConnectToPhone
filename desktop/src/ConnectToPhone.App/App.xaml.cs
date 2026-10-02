using System.IO;
using System.Windows;

namespace ConnectToPhone.App;

public partial class App : Application
{
    private const string LogPath = @"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log";

    protected override void OnStartup(StartupEventArgs e)
    {
        File.AppendAllText(LogPath, $"[App] OnStartup at {DateTime.Now}\n");

        DispatcherUnhandledException += (s, args) =>
        {
            File.AppendAllText(LogPath, $"[App] DispatcherUnhandledException: {args.Exception}\n");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            File.AppendAllText(LogPath, $"[App] DomainUnhandledException: {args.ExceptionObject}\n");
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            File.AppendAllText(LogPath, $"[App] UnobservedTaskException: {args.Exception}\n");
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        File.AppendAllText(LogPath, $"[App] OnExit with code {e.ApplicationExitCode} at {DateTime.Now}\n");
        base.OnExit(e);
    }
}

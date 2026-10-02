using System.IO;
using System.Windows;

namespace ConnectToPhone.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, args) =>
        {
            File.WriteAllText("crash.log", args.Exception.ToString());
            MessageBox.Show(args.Exception.Message, "ConnectToPhone Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            File.WriteAllText("crash_domain.log", args.ExceptionObject.ToString());
        };

        base.OnStartup(e);
    }
}

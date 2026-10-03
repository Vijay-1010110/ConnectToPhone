using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace ConnectToPhone.App;

public partial class App : Application
{
    private const string LogPath = @"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log";
    private static Mutex? _singleInstanceMutex;

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try { File.AppendAllText(LogPath, $"[App] DomainUnhandledException: {args.ExceptionObject}\n"); } catch { }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            try { File.AppendAllText(LogPath, $"[App] DispatcherUnhandledException: {args.Exception}\n"); } catch { }
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            try { File.AppendAllText(LogPath, $"[App] UnobservedTaskException: {args.Exception}\n"); } catch { }
            args.SetObserved();
        };

        try
        {
            File.AppendAllText(LogPath, $"[App] OnStartup at {DateTime.Now}\n");
        }
        catch { }

        bool isNewInstance = true;
        try
        {
            _singleInstanceMutex = new Mutex(true, "ConnectToPhone_SingleInstance_Mutex_Vijay", out isNewInstance);
        }
        catch (AbandonedMutexException)
        {
            isNewInstance = true;
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(LogPath, $"[App] Mutex exception: {ex}\n"); } catch { }
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            try
            {
                var cur = Process.GetCurrentProcess();
                bool restoredAny = false;
                foreach (var p in Process.GetProcessesByName(cur.ProcessName))
                {
                    if (p.Id != cur.Id)
                    {
                        IntPtr hWnd = p.MainWindowHandle;
                        if (hWnd == IntPtr.Zero)
                        {
                            hWnd = FindWindow(null, "ConnectToPhone ⚡ - High-Speed Sync & Remote");
                        }
                        if (hWnd != IntPtr.Zero)
                        {
                            ShowWindowAsync(hWnd, SW_RESTORE);
                            SetForegroundWindow(hWnd);
                            restoredAny = true;
                            break;
                        }
                        else
                        {
                            // Stale headless / orphaned process without a window! Kill it so the new GUI instance can start
                            try
                            {
                                File.AppendAllText(LogPath, $"[App] Killing headless orphaned process PID {p.Id}\n");
                                p.Kill();
                            }
                            catch { }
                        }
                    }
                }

                if (restoredAny)
                {
                    Shutdown(0);
                    return;
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(LogPath, $"[App] Instance resolution exception: {ex}\n"); } catch { }
            }
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex?.Dispose();
        try { File.AppendAllText(LogPath, $"[App] OnExit with code {e.ApplicationExitCode} at {DateTime.Now}\n"); } catch { }
        base.OnExit(e);
    }
}

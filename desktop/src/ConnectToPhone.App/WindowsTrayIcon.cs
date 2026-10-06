using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ConnectToPhone.App;

public sealed class WindowsTrayIcon : IDisposable
{
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int NIIF_INFO = 0x00000001;

    private const int WM_APP = 0x8000;
    public const int WM_TRAYICON = WM_APP + 42;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    private readonly IntPtr _hwnd;
    private readonly HwndSource _hwndSource;
    private bool _isDisposed;
    private readonly IntPtr _hIcon;

    public event Action? TrayClicked;
    public event Action? TrayDoubleClicked;
    public event Action? TrayRightClicked;

    public WindowsTrayIcon(Window window, IntPtr hIcon)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource.AddHook(HwndHook);
        _hIcon = hIcon != IntPtr.Zero ? hIcon : LoadIcon(IntPtr.Zero, (IntPtr)32512);

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "ConnectToPhone - Ultra-light background sync"
        };
        Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    public void ShowBalloonTip(string title, string text)
    {
        if (_isDisposed) return;
        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_INFO,
            szInfoTitle = title,
            szInfo = text,
            dwInfoFlags = NIIF_INFO
        };
        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int mouseMsg = lParam.ToInt32();
            if (mouseMsg == WM_LBUTTONUP)
            {
                TrayClicked?.Invoke();
                handled = true;
            }
            else if (mouseMsg == WM_LBUTTONDBLCLK)
            {
                TrayDoubleClicked?.Invoke();
                handled = true;
            }
            else if (mouseMsg == WM_RBUTTONUP)
            {
                TrayRightClicked?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001
        };
        Shell_NotifyIcon(NIM_DELETE, ref nid);
        _hwndSource.RemoveHook(HwndHook);
    }
}

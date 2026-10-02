using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ConnectToPhone.Core.Protocol;
using ConnectToPhone.Core.Protocol.Models;

namespace ConnectToPhone.Clipboard;

public sealed partial class ClipboardService : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AddClipboardFormatListener(IntPtr hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveClipboardFormatListener(IntPtr hwnd);

    private readonly Dispatcher _dispatcher;
    private HwndSource? _hwndSource;
    private readonly HashSet<ulong> _recentlyHandledHashes = [];
    private readonly object _hashLock = new();
    private bool _isDisposed;

    public event Action<ClipboardPayload>? LocalClipboardCopied;

    public ClipboardService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void Initialize(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return;

        _hwndSource = HwndSource.FromHwnd(windowHandle);
        _hwndSource?.AddHook(HwndHook);
        AddClipboardFormatListener(windowHandle);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            OnClipboardUpdate();
        }
        return IntPtr.Zero;
    }

    private void OnClipboardUpdate()
    {
        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (!System.Windows.Clipboard.ContainsText())
                    return;

                string text = System.Windows.Clipboard.GetText();
                if (string.IsNullOrEmpty(text))
                    return;

                byte[] textBytes = Encoding.UTF8.GetBytes(text);
                ulong hash = XxHash64.HashToUInt64(textBytes);

                lock (_hashLock)
                {
                    if (_recentlyHandledHashes.Contains(hash))
                    {
                        // Ignore echo loop
                        return;
                    }

                    _recentlyHandledHashes.Add(hash);
                    if (_recentlyHandledHashes.Count > 100)
                    {
                        _recentlyHandledHashes.Clear();
                        _recentlyHandledHashes.Add(hash);
                    }
                }

                var payload = new ClipboardPayload
                {
                    Format = ClipboardFormat.PlainText,
                    ContentHash = hash,
                    TextContent = text,
                    MimeType = "text/plain",
                    TimestampMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                LocalClipboardCopied?.Invoke(payload);
            }
            catch
            {
                // Clipboard may be temporarily locked by another process
            }
        });
    }

    /// <summary>
    /// Sets incoming remote clipboard content to Windows clipboard without echo bounce.
    /// </summary>
    public void SetRemoteClipboard(ClipboardPayload payload)
    {
        if (string.IsNullOrEmpty(payload.TextContent))
            return;

        lock (_hashLock)
        {
            _recentlyHandledHashes.Add(payload.ContentHash);
        }

        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                System.Windows.Clipboard.SetText(payload.TextContent);
            }
            catch
            {
                // Ignore transient lock
            }
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_hwndSource != null && _hwndSource.Handle != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_hwndSource.Handle);
            _hwndSource.RemoveHook(HwndHook);
            _hwndSource = null;
        }
    }
}

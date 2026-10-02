using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using ConnectToPhone.Clipboard;
using ConnectToPhone.Core.Cache;
using ConnectToPhone.Core.Engine;
using ConnectToPhone.Core.Protocol;
using ConnectToPhone.Core.Protocol.Models;
using ConnectToPhone.Explorer;
using ConnectToPhone.Transports;
using ConnectToPhone.Transports.Discovery;
using ConnectToPhone.Transports.Sockets;
using ConnectToPhone.Transports.Usb;
using Microsoft.Win32;

namespace ConnectToPhone.App;

public partial class MainWindow : Window
{
    private ClipboardService? _clipboardService;
    private ITransport? _activeTransport;
    private TcpServer? _tcpServer;
    private UdpDiscoveryBeacon? _udpBeacon;
    private readonly WindowsFileSystemHost _fsHost = new();
    private readonly DeviceSecurityManager _security = new();
    private readonly CacheManager _cacheManager = new();
    private readonly ObservableCollection<string> _activityLog = [];
    private readonly ObservableCollection<PhoneEntryDisplay> _phoneEntries = [];
    private readonly ObservableCollection<string> _clipboardLog = [];
    private readonly ObservableCollection<PairedDeviceDisplay> _pairedDevices = [];
    private readonly ObservableCollection<CachedFileItem> _cachedFiles = [];
    private readonly Dictionary<ulong, SparseFileWriter> _activeWriters = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, ChunkScheduler> _activeSchedulers = new();
    private CancellationTokenSource? _autoConnectCts;
    private string _currentCacheFilter = "All";

    public sealed class PhoneEntryDisplay
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string SizeDisplay { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
    }

    public sealed class PairedDeviceDisplay
    {
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string TrustStatus { get; set; } = string.Empty;
    }

    public MainWindow()
    {
        InitializeComponent();
        ListActivity.ItemsSource = _activityLog;
        ListPhoneEntries.ItemsSource = _phoneEntries;
        ListClipboardHistory.ItemsSource = _clipboardLog;
        ListPairedDevices.ItemsSource = _pairedDevices;
        ListCachedFiles.ItemsSource = _cachedFiles;

        _cacheManager.CacheUpdated += () => Dispatcher.Invoke(RefreshCacheView);

        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. Hook clipboard listener to the Win32 HWND
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        _clipboardService = new ClipboardService(Dispatcher);
        _clipboardService.Initialize(hwnd);
        _clipboardService.LocalClipboardCopied += OnLocalClipboardCopied;

        // 2. Initialize status & PIN
        _activityLog.Add("[System] Initialized ConnectToPhone Core Engine.");
        UpdateTaskbarProgress(0, TaskbarItemProgressState.None);
        TxtPin.Text = _security.CurrentSessionPin;
        RefreshPairedDevicesList();

        // 3. Start local TCP server (Port 42424) so Phone can connect over Wi-Fi LAN
        StartTcpServer();

        // 4. Start zero-touch UDP discovery
        StartUdpDiscovery();

        // 5. Connect to phone over USB bridge automatically in continuous loop
        _autoConnectCts = new CancellationTokenSource();
        _ = AutoConnectUsbLoopAsync(_autoConnectCts.Token);

        // 6. Initialize storage & cache
        RefreshCacheView();
        Task.Run(async () =>
        {
            while (!_autoConnectCts.Token.IsCancellationRequested)
            {
                _cacheManager.RunPeriodicCleanup();
                await Task.Delay(TimeSpan.FromHours(6), _autoConnectCts.Token);
            }
        });
    }

    private void RefreshPairedDevicesList()
    {
        _pairedDevices.Clear();
        foreach (var d in _security.GetAllDevices())
        {
            _pairedDevices.Add(new PairedDeviceDisplay
            {
                DeviceName = d.DeviceName,
                DeviceId = d.DeviceId,
                DeviceType = d.DeviceType.ToString(),
                TrustStatus = d.IsTrusted ? "✓ Trusted" : "Pending"
            });
        }
    }

    private void StartTcpServer()
    {
        try
        {
            _tcpServer = new TcpServer(42424);
            _tcpServer.ClientConnected += transport =>
            {
                AttachTransport(transport, "Wi-Fi LAN");
            };
            _tcpServer.Start();
            _activityLog.Insert(0, "[Wi-Fi] Listening on port 42424 for local wireless connections.");
        }
        catch (Exception ex)
        {
            _activityLog.Insert(0, $"[Wi-Fi] Server note: {ex.Message}");
        }
    }

    private void StartUdpDiscovery()
    {
        try
        {
            _udpBeacon = new UdpDiscoveryBeacon(Environment.MachineName, $"{Environment.MachineName} (PC)", 42424);
            _udpBeacon.PeerDiscovered += peer =>
            {
                Dispatcher.Invoke(() =>
                {
                    _activityLog.Insert(0, $"[Discovery] Found phone '{peer.DeviceName}' at {peer.RemoteIpAddress}:{peer.TcpPort}");
                });
            };
            _udpBeacon.Start();
        }
        catch (Exception ex)
        {
            _activityLog.Insert(0, $"[Discovery] Note: {ex.Message}");
        }
    }

    private async Task AutoConnectUsbLoopAsync(CancellationToken ct)
    {
        var adb = new AdbBridgeTransport();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_activeTransport == null || !_activeTransport.IsConnected)
                {
                    // 1. Try USB ADB
                    var devices = await adb.GetConnectedAdbDevicesAsync(ct);
                    if (devices.Count > 0)
                    {
                        string deviceId = devices[0];
                        var transport = await adb.ConnectOverUsbAsync(deviceId, AdbBridgeTransport.DefaultUsbForwardPort, ct);
                        if (transport != null)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                AttachTransport(transport, $"USB ({deviceId})");
                            });

                            await SendHandshakeAndRootReqAsync(transport);
                            await Task.Delay(2500, ct).ConfigureAwait(false);
                            continue;
                        }
                    }

                    // 2. Try Wi-Fi to phone IP (e.g. 192.168.6.215)
                    string[] wifiCandidates = ["192.168.6.215", "192.168.1.100"];
                    foreach (var ip in wifiCandidates)
                    {
                        try
                        {
                            using var ctsTimeout = new CancellationTokenSource(800);
                            var wifiTransport = await TcpServer.ConnectAsync(ip, 42424, ctsTimeout.Token);
                            if (wifiTransport != null && wifiTransport.IsConnected)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    AttachTransport(wifiTransport, $"Wi-Fi ({ip})");
                                });

                                await SendHandshakeAndRootReqAsync(wifiTransport);
                                break;
                            }
                        }
                        catch {}
                    }
                }
            }
            catch {}

            await Task.Delay(2500, ct).ConfigureAwait(false);
        }
    }

    private async Task SendHandshakeAndRootReqAsync(ITransport transport)
    {
        var syn = new HandshakeSyn
        {
            DeviceId = Environment.MachineName,
            DeviceName = $"{Environment.MachineName} (Windows)",
            DeviceType = DeviceType.Windows,
            SupportedTransports = TransportType.UsbAdb | TransportType.WifiLan
        };

        await transport.SendFrameAsync(new BinaryFrame(
            FrameType.HandshakeSyn,
            12345678,
            syn.ToUtf8Bytes()
        ));

        // Request root directory from phone
        await RequestPhoneDirectoryAsync("/");
    }

    private void AttachTransport(ITransport transport, string label)
    {
        _activeTransport = transport;
        _activeTransport.FrameReceived += OnFrameReceivedFromPhone;
        _activeTransport.Disconnected += OnTransportDisconnected;
        _activeTransport.StartReceiving();

        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
        TxtStatus.Text = $"Connected over {label} ⚡";
        TxtDeviceName.Text = $"Connected Device ({label})";
        if (label.Contains("USB", StringComparison.OrdinalIgnoreCase))
        {
            TxtUsbBadge.Text = "⚡ USB (Active Link)";
            TxtWifiBadge.Text = "💤 Wi-Fi (Standby / Deep Sleep)";
        }
        else
        {
            TxtWifiBadge.Text = "📶 Wi-Fi (Active Link)";
            TxtUsbBadge.Text = "💤 USB (Standby)";
        }
        TxtBondedSpeed.Text = "Idle / Energy Saver (0% CPU)";
        _activityLog.Insert(0, $"[Transport] Single-pipe established via {label}. Secondary channels in low-power sleep.");
    }

    private async Task OnFrameReceivedFromPhone(ITransport transport, BinaryFrame frame)
    {
        switch (frame.Type)
        {
            case FrameType.HandshakeSyn:
                var syn = HandshakeSyn.FromUtf8Bytes(frame.Payload);
                if (syn != null)
                {
                    _security.TrustDevice(syn.DeviceId, syn.DeviceName, syn.DeviceType);
                    var ack = new HandshakeAck
                    {
                        Accepted = true,
                        SessionId = frame.SessionId,
                        DeviceId = Environment.MachineName,
                        DeviceName = $"{Environment.MachineName} (Windows)"
                    };
                    await transport.SendFrameAsync(new BinaryFrame(FrameType.HandshakeAck, frame.SessionId, ack.ToUtf8Bytes()));

                    Dispatcher.Invoke(() =>
                    {
                        TxtDeviceName.Text = syn.DeviceName;
                        RefreshPairedDevicesList();
                        _activityLog.Insert(0, $"[Handshake] Accepted connection from {syn.DeviceName}!");
                    });
                }
                break;

            case FrameType.HandshakeAck:
                var ackResp = HandshakeAck.FromUtf8Bytes(frame.Payload);
                Dispatcher.Invoke(() =>
                {
                    if (ackResp != null)
                    {
                        TxtDeviceName.Text = $"{ackResp.DeviceName}";
                        _security.TrustDevice(ackResp.DeviceId, ackResp.DeviceName, DeviceType.Android);
                        RefreshPairedDevicesList();
                        _activityLog.Insert(0, $"[Handshake] Successfully paired with {ackResp.DeviceName}!");
                    }
                });
                break;

            // Remote Touchpad & Mouse Control
            case FrameType.CtrlMouse:
                var mouse = MouseControlPayload.FromUtf8Bytes(frame.Payload);
                if (mouse != null)
                {
                    Win32InputSimulator.HandleMouse(mouse);
                }
                break;

            // Remote Keyboard Control
            case FrameType.CtrlKeyboard:
                var kbd = KeyboardControlPayload.FromUtf8Bytes(frame.Payload);
                if (kbd != null)
                {
                    Win32InputSimulator.HandleKeyboard(kbd);
                    Dispatcher.Invoke(() =>
                    {
                        string desc = !string.IsNullOrEmpty(kbd.SpecialKey) ? $"[{kbd.SpecialKey}]" : $"\"{kbd.Text}\"";
                        _activityLog.Insert(0, $"[Remote Key] Executed input: {desc}");
                    });
                }
                break;

            // Media Control
            case FrameType.CtrlMedia:
                var media = MediaControlPayload.FromUtf8Bytes(frame.Payload);
                if (media != null)
                {
                    Win32InputSimulator.HandleMedia(media.Action);
                    Dispatcher.Invoke(() => _activityLog.Insert(0, $"[Remote Media] Action: {media.Action}"));
                }
                break;

            // Power Control
            case FrameType.CtrlPower:
                var power = PowerControlPayload.FromUtf8Bytes(frame.Payload);
                if (power != null)
                {
                    Win32InputSimulator.HandlePower(power.Action);
                    Dispatcher.Invoke(() => _activityLog.Insert(0, $"[Remote Power] Executed: {power.Action}"));
                }
                break;

            case FrameType.ClipboardSync:
                var clip = ClipboardPayload.FromUtf8Bytes(frame.Payload);
                if (clip != null)
                {
                    _clipboardService?.SetRemoteClipboard(clip);
                    Dispatcher.Invoke(() =>
                    {
                        string preview = clip.TextContent?.Length > 60 ? clip.TextContent[..60] + "..." : clip.TextContent ?? "";
                        _clipboardLog.Insert(0, $"[Phone Clipboard] Received: \"{preview}\"");
                    });
                }
                break;

            case FrameType.FsListDirReq:
                var listReq = FsListDirRequest.FromUtf8Bytes(frame.Payload);
                var listResp = _fsHost.ListDirectory(listReq?.TargetPath, listReq?.IncludeHidden ?? false);
                await transport.SendFrameAsync(new BinaryFrame(
                    FrameType.FsListDirResp,
                    frame.SessionId,
                    listResp.ToUtf8Bytes()
                ));
                Dispatcher.Invoke(() => _activityLog.Insert(0, $"[Remote Explorer] Phone browsed PC folder: {listResp.CurrentPath}"));
                break;

            case FrameType.FsPullFileReq:
                var pullReq = FsPullFileRequest.FromUtf8Bytes(frame.Payload);
                if (pullReq != null && File.Exists(pullReq.RemoteFilePath))
                {
                    Dispatcher.Invoke(() => _activityLog.Insert(0, $"[Pull] Phone requested PC file: {Path.GetFileName(pullReq.RemoteFilePath)} (Preview: {pullReq.IsPreview})"));
                    _ = PushFileToPhoneAsync(pullReq.RemoteFilePath, pullReq.IsPreview);
                }
                break;

            case FrameType.FsListDirResp:
                var resp = FsListDirResponse.FromUtf8Bytes(frame.Payload);
                if (resp != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        _phoneEntries.Clear();
                        foreach (var e in resp.Entries)
                        {
                            _phoneEntries.Add(new PhoneEntryDisplay
                            {
                                Name = e.EntryType == FsEntryType.Directory ? $"📁 {e.Name}" : e.Name,
                                Path = e.Path,
                                Category = e.Category.ToString(),
                                SizeDisplay = e.SizeBytes > 0 ? $"{(e.SizeBytes / 1024.0 / 1024.0):F1} MB" : "-",
                                IsDirectory = e.EntryType == FsEntryType.Directory
                            });
                        }
                        TxtExplorerPath.Text = resp.CurrentPath;
                        _activityLog.Insert(0, $"[Phone Explorer] Loaded {resp.Entries.Count} items from phone: {resp.CurrentPath}");
                    });
                }
                break;

            case FrameType.TransferManifest:
                var manifest = TransferManifest.FromUtf8Bytes(frame.Payload);
                if (manifest != null)
                {
                    string targetDir = manifest.IsPreview ? _cacheManager.CacheDirectory : _cacheManager.DownloadsDirectory;
                    var writer = new SparseFileWriter(manifest, targetDir);
                    _activeWriters[manifest.TransferId] = writer;

                    Dispatcher.Invoke(() =>
                    {
                        TxtCurrentFileName.Text = manifest.FileName;
                        TxtTransferDetails.Text = $"Receiving: {manifest.TotalBytes / 1024.0 / 1024.0:F1} MB • Channel: Multi-Path {(manifest.IsPreview ? "(Preview Cache)" : "(Non-C Downloads)")}";
                        UpdateTaskbarProgress(0.01, TaskbarItemProgressState.Normal);
                        TxtBondedSpeed.Text = "🚀 Multi-Path Turbo Burst";
                        _activityLog.Insert(0, $"[Transfer] Starting pull for '{manifest.FileName}' ({manifest.TotalBytes / 1024.0 / 1024.0:F1} MB, Preview: {manifest.IsPreview})");
                    });
                }
                break;

            case FrameType.TransferChunk:
                if (TransferChunk.TryParse(frame.Payload, out var chunk) && chunk != null)
                {
                    if (_activeWriters.TryGetValue(chunk.TransferId, out var writer))
                    {
                        bool ok = writer.WriteChunk(chunk, out _);
                        var chunkAck = new TransferChunkAck
                        {
                            TransferId = chunk.TransferId,
                            ChunkIndex = chunk.ChunkIndex,
                            Success = ok
                        };

                        await transport.SendFrameAsync(new BinaryFrame(
                            FrameType.TransferChunkAck,
                            frame.SessionId,
                            chunkAck.Serialize()
                        ));

                        double progress = (double)writer.VerifiedChunkCount / writer.TotalChunks;
                        Dispatcher.Invoke(() =>
                        {
                            TransferProgressBar.Value = progress * 100;
                            UpdateTaskbarProgress(progress, TaskbarItemProgressState.Normal);
                            TxtTransferRate.Text = $"Progress: {progress * 100:F1}% ({writer.VerifiedChunkCount}/{writer.TotalChunks} chunks)";
                        });

                        if (writer.IsComplete)
                        {
                            writer.FinalizeFile(out _);
                            string finalizedPath = writer.FinalDestinationPath;
                            _activeWriters.Remove(chunk.TransferId);
                            Dispatcher.Invoke(() =>
                            {
                                UpdateTaskbarProgress(1.0, TaskbarItemProgressState.None);
                                TxtBondedSpeed.Text = "Idle / Energy Saver";
                                try { System.Media.SystemSounds.Asterisk.Play(); } catch {}

                                if (writer.Manifest.IsPreview)
                                {
                                    _cacheManager.EnforceQuotaAndPrune();
                                    _activityLog.Insert(0, $"[Preview Ready] Loaded '{writer.FileName}' into cache memory without cluttering downloads!");
                                    RefreshCacheView();
                                    _cacheManager.OpenQuickView(finalizedPath);
                                }
                                else
                                {
                                    _activityLog.Insert(0, $"[Download] Successfully received '{writer.FileName}' directly into {_cacheManager.DownloadsDirectory}!");
                                }
                            });
                        }
                    }
                }
                break;

            case FrameType.TransferChunkAck:
                if (TransferChunkAck.TryParse(frame.Payload, out var incomingAck) && incomingAck != null)
                {
                    if (_activeSchedulers.TryGetValue(incomingAck.TransferId, out var scheduler))
                    {
                        if (incomingAck.Success)
                        {
                            scheduler.AcknowledgeChunk(incomingAck.ChunkIndex);
                        }
                    }
                }
                break;
        }
    }

    private void OnTransportDisconnected(ITransport transport)
    {
        Dispatcher.Invoke(() =>
        {
            if (_activeTransport == transport)
            {
                _activeTransport = null;
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                TxtStatus.Text = "Disconnected";
                TxtUsbBadge.Text = "USB: Disconnected";
                TxtWifiBadge.Text = "Wi-Fi: Ready";
                TxtBondedSpeed.Text = "Idle";
                _activityLog.Insert(0, "[Transport] Pipe disconnected. Auto-connector standing by...");
            }
        });
    }

    private async Task RequestPhoneDirectoryAsync(string path)
    {
        if (_activeTransport != null && _activeTransport.IsConnected)
        {
            var req = new FsListDirRequest { TargetPath = path, IncludeHidden = false };
            await _activeTransport.SendFrameAsync(new BinaryFrame(
                FrameType.FsListDirReq,
                12345678,
                req.ToUtf8Bytes()
            ));
        }
    }

    public void UpdateTaskbarProgress(double progress0To1, TaskbarItemProgressState state)
    {
        Dispatcher.Invoke(() =>
        {
            AppTaskbarInfo.ProgressState = state;
            AppTaskbarInfo.ProgressValue = progress0To1;
        });
    }

    private void OnLocalClipboardCopied(ClipboardPayload payload)
    {
        Dispatcher.Invoke(async () =>
        {
            string preview = payload.TextContent?.Length > 60
                ? payload.TextContent[..60] + "..."
                : payload.TextContent ?? "";

            _clipboardLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Copied: \"{preview}\"");

            // Forward to phone
            if (ChkAutoClip?.IsChecked == true && _activeTransport != null && _activeTransport.IsConnected)
            {
                await _activeTransport.SendFrameAsync(new BinaryFrame(
                    FrameType.ClipboardSync,
                    12345678,
                    payload.ToUtf8Bytes()
                ));
            }
        });
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewDashboard == null || ViewExplorer == null || ViewClipboard == null || ViewMcp == null || ViewExtensions == null || ViewSecurity == null || ViewCache == null)
            return;

        ViewDashboard.Visibility = Visibility.Collapsed;
        ViewExplorer.Visibility = Visibility.Collapsed;
        ViewClipboard.Visibility = Visibility.Collapsed;
        ViewMcp.Visibility = Visibility.Collapsed;
        ViewExtensions.Visibility = Visibility.Collapsed;
        ViewSecurity.Visibility = Visibility.Collapsed;
        ViewCache.Visibility = Visibility.Collapsed;

        if (NavDashboard.IsChecked == true)
        {
            ViewDashboard.Visibility = Visibility.Visible;
            TxtViewTitle.Text = "Transfer Hub & Live Multi-Path Speed";
        }
        else if (NavExplorer.IsChecked == true)
        {
            ViewExplorer.Visibility = Visibility.Visible;
            TxtViewTitle.Text = "Remote Phone Explorer";
        }
        else if (NavClipboard.IsChecked == true)
        {
            ViewClipboard.Visibility = Visibility.Visible;
            TxtViewTitle.Text = "Real-Time Clipboard Sync";
        }
        else if (NavCache.IsChecked == true)
        {
            ViewCache.Visibility = Visibility.Visible;
            TxtViewTitle.Text = "Drive & Preview Cache Manager";
            RefreshCacheView();
        }
        else if (NavSecurity.IsChecked == true)
        {
            ViewSecurity.Visibility = Visibility.Visible;
            TxtViewTitle.Text = "Paired Devices & Security Management";
            RefreshPairedDevicesList();
        }
        else if (NavMcp.IsChecked == true)
        {
            ViewMcp.Visibility = Visibility.Visible;
            TxtViewTitle.Text = "Model Context Protocol (MCP) AI Tools";
        }
        else if (NavMirror.IsChecked == true)
        {
            ViewExtensions.Visibility = Visibility.Visible;
            TxtViewTitle.Text = "Screen Mirroring";
            TxtExtensionIcon.Text = "🖥️";
            TxtExtensionTitle.Text = "Low-Latency Screen Mirroring";
            TxtExtensionDesc.Text = "Real-time 60 FPS interactive screen stream over multi-path pipe.";
        }
        else if (NavDisplay.IsChecked == true)
        {
            ViewExtensions.Visibility = Visibility.Visible;
            TxtViewTitle.Text = "Virtual Secondary Display";
            TxtExtensionIcon.Text = "📐";
            TxtExtensionTitle.Text = "Extend Windows Desktop to Phone/Tablet";
            TxtExtensionDesc.Text = "Turn your phone or tablet into a native second monitor for Windows.";
        }
        else if (NavRemote.IsChecked == true)
        {
            ViewExtensions.Visibility = Visibility.Visible;
            TxtViewTitle.Text = "PC Remote & Utility Control";
            TxtExtensionIcon.Text = "🎮";
            TxtExtensionTitle.Text = "Remote Touchpad, Media & Power Controls";
            TxtExtensionDesc.Text = "Control your Windows PC wirelessly from your phone screen.";
        }
    }

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        UpdateTaskbarProgress(0.57, TaskbarItemProgressState.Paused);
        _activityLog.Insert(0, "[Transfer] Paused by user.");
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        UpdateTaskbarProgress(0, TaskbarItemProgressState.None);
        _activityLog.Insert(0, "[Transfer] Cancelled by user.");
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var f in files)
            {
                await PushFileToPhoneAsync(f);
            }
        }
    }

    private async void BtnBrowseSend_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dlg = new() { Multiselect = true, Title = "Select files to send to Phone" };
        if (dlg.ShowDialog() == true)
        {
            foreach (var f in dlg.FileNames)
            {
                await PushFileToPhoneAsync(f);
            }
        }
    }

    private async Task PushFileToPhoneAsync(string filePath, bool isPreview = false)
    {
        if (_activeTransport == null || !_activeTransport.IsConnected)
        {
            MessageBox.Show("Phone is not connected over USB or Wi-Fi.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var scheduler = new ChunkScheduler(filePath);
            scheduler.Manifest.IsPreview = isPreview;
            _activeSchedulers[scheduler.TransferId] = scheduler;
            TxtBondedSpeed.Text = "🚀 Multi-Path Turbo Burst";
            _activityLog.Insert(0, $"[Upload] Streaming '{scheduler.Manifest.FileName}' ({scheduler.Manifest.TotalBytes / 1024.0 / 1024.0:F1} MB) to phone (Preview: {isPreview})...");

            // Send Manifest
            await _activeTransport.SendFrameAsync(new BinaryFrame(
                FrameType.TransferManifest,
                12345678,
                scheduler.Manifest.ToUtf8Bytes()
            ));

            // Stream chunks
            int timeoutCounter = 0;
            while (!scheduler.IsComplete && timeoutCounter < 1500)
            {
                if (scheduler.TryGetNextChunk(_activeTransport.ChannelId, out var chunk) && chunk != null)
                {
                    await _activeTransport.SendFrameAsync(new BinaryFrame(
                        FrameType.TransferChunk,
                        12345678,
                        chunk.Serialize()
                    ));
                    timeoutCounter = 0;
                }
                else
                {
                    await Task.Delay(10);
                    timeoutCounter++;
                    if (timeoutCounter % 100 == 0)
                    {
                        scheduler.RequeueTimedOutChunks(TimeSpan.FromSeconds(2));
                    }
                }

                double progress = scheduler.TotalChunks > 0 ? (double)scheduler.AckedCount / scheduler.TotalChunks : 0;
                UpdateTaskbarProgress(progress, TaskbarItemProgressState.Normal);
            }

            _activeSchedulers.TryRemove(scheduler.TransferId, out _);
            UpdateTaskbarProgress(1.0, TaskbarItemProgressState.None);
            TxtBondedSpeed.Text = "Idle / Energy Saver";
            try { System.Media.SystemSounds.Asterisk.Play(); } catch {}
            _activityLog.Insert(0, $"[Upload] Completed sending '{scheduler.Manifest.FileName}'!");
        }
        catch (Exception ex)
        {
            _activityLog.Insert(0, $"[Upload] Error: {ex.Message}");
        }
    }

    private async void ListPhoneEntries_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ListPhoneEntries.SelectedItem is PhoneEntryDisplay selected && selected.IsDirectory)
        {
            await RequestPhoneDirectoryAsync(selected.Path);
        }
    }

    private async void BtnExplorerBack_Click(object sender, RoutedEventArgs e)
    {
        await RequestPhoneDirectoryAsync("/");
    }

    private async void BtnExplorerRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RequestPhoneDirectoryAsync(TxtExplorerPath.Text);
    }

    private async void BtnQuickViewSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ListPhoneEntries.SelectedItem is PhoneEntryDisplay selected && !selected.IsDirectory)
        {
            _activityLog.Insert(0, $"[Quick View] Requesting '{selected.Name}' into temporary cache...");
            if (_activeTransport != null && _activeTransport.IsConnected)
            {
                var req = new FsPullFileRequest { RemoteFilePath = selected.Path, IsPreview = true };
                await _activeTransport.SendFrameAsync(new BinaryFrame(
                    FrameType.FsPullFileReq,
                    12345678,
                    req.ToUtf8Bytes()
                ));
            }
        }
        else
        {
            MessageBox.Show("Please select a file from the phone explorer to preview.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void BtnDownloadSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ListPhoneEntries.SelectedItem is PhoneEntryDisplay selected && !selected.IsDirectory)
        {
            _activityLog.Insert(0, $"[Download] Requesting '{selected.Name}' to permanent non-C storage...");
            if (_activeTransport != null && _activeTransport.IsConnected)
            {
                var req = new FsPullFileRequest { RemoteFilePath = selected.Path, IsPreview = false };
                await _activeTransport.SendFrameAsync(new BinaryFrame(
                    FrameType.FsPullFileReq,
                    12345678,
                    req.ToUtf8Bytes()
                ));
            }
        }
        else
        {
            MessageBox.Show("Please select a file from the phone explorer to download.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RefreshCacheView()
    {
        if (TxtCacheDrive == null || TxtDriveFree == null || TxtCacheUsed == null || TxtCachePath == null || TxtDownloadsPath == null || _cachedFiles == null)
            return;

        var info = _cacheManager.GetStorageSpaceInfo();
        TxtCacheDrive.Text = $"{info.DriveName} (Safe Storage)";
        TxtDriveFree.Text = $"{info.FreeDisplay} Free";
        TxtCacheUsed.Text = $"{info.CacheUsedDisplay} / {info.QuotaDisplay} Quota";
        TxtCachePath.Text = $"Cache: {_cacheManager.CacheDirectory}";
        TxtDownloadsPath.Text = $"Downloads: {_cacheManager.DownloadsDirectory}";

        _cachedFiles.Clear();
        foreach (var item in _cacheManager.GetCachedFiles(_currentCacheFilter))
        {
            _cachedFiles.Add(item);
        }
    }

    private void FilterCache_Changed(object sender, RoutedEventArgs e)
    {
        if (FilterCacheAll == null || FilterCacheImages == null || FilterCacheDocs == null || FilterCacheMedia == null || FilterCacheOther == null)
            return;

        if (FilterCacheImages.IsChecked == true) _currentCacheFilter = "Images";
        else if (FilterCacheDocs.IsChecked == true) _currentCacheFilter = "Documents";
        else if (FilterCacheMedia.IsChecked == true) _currentCacheFilter = "Media";
        else if (FilterCacheOther.IsChecked == true) _currentCacheFilter = "Other";
        else _currentCacheFilter = "All";
        RefreshCacheView();
    }

    private void BtnClearAllCache_Click(object sender, RoutedEventArgs e)
    {
        _cacheManager.ClearAllCache();
        RefreshCacheView();
        try { System.Media.SystemSounds.Asterisk.Play(); } catch {}
        _activityLog.Insert(0, "[Cache] Cleared all temporary preview cache files!");
        MessageBox.Show("Preview cache cleared successfully.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnOpenCacheFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_cacheManager.CacheDirectory))
        {
            System.Diagnostics.Process.Start("explorer.exe", _cacheManager.CacheDirectory);
        }
    }

    private void BtnDeleteSelectedCache_Click(object sender, RoutedEventArgs e)
    {
        if (ListCachedFiles.SelectedItem is CachedFileItem sel)
        {
            if (_cacheManager.DeleteCachedFile(sel.FilePath))
            {
                RefreshCacheView();
                try { System.Media.SystemSounds.Asterisk.Play(); } catch {}
                _activityLog.Insert(0, $"[Cache] Removed '{sel.FileName}' from preview cache.");
            }
        }
    }

    private void BtnOpenSelectedCache_Click(object sender, RoutedEventArgs e)
    {
        if (ListCachedFiles.SelectedItem is CachedFileItem sel)
        {
            _cacheManager.OpenQuickView(sel.FilePath);
        }
    }

    private void ListCachedFiles_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        BtnOpenSelectedCache_Click(sender, e);
    }

    private void ChkAutoClip_Changed(object sender, RoutedEventArgs e)
    {
        bool isEnabled = ChkAutoClip.IsChecked == true;
        _activityLog.Insert(0, $"[Clipboard] Automatic sync {(isEnabled ? "enabled" : "disabled")}.");
    }

    private void BtnOpenMovies_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(@"D:\movies"))
        {
            System.Diagnostics.Process.Start("explorer.exe", @"D:\movies");
        }
    }

    private void BtnOpenDownloads_Click(object sender, RoutedEventArgs e)
    {
        string downloads = _cacheManager.DownloadsDirectory;
        if (Directory.Exists(downloads))
        {
            System.Diagnostics.Process.Start("explorer.exe", downloads);
        }
    }

    private void BtnForceConnect_Click(object sender, RoutedEventArgs e)
    {
        _activityLog.Insert(0, "[USB] Manual trigger: Scanning ADB devices...");
        _ = AutoConnectUsbLoopAsync(CancellationToken.None);
    }

    private void BtnRegenPin_Click(object sender, RoutedEventArgs e)
    {
        TxtPin.Text = _security.GenerateNewPin();
        _activityLog.Insert(0, $"[Security] Generated new verification PIN: {TxtPin.Text}");
    }

    private void BtnUnpair_Click(object sender, RoutedEventArgs e)
    {
        if (ListPairedDevices.SelectedItem is PairedDeviceDisplay sel)
        {
            _security.UnpairDevice(sel.DeviceId);
            RefreshPairedDevicesList();
            _activityLog.Insert(0, $"[Security] Unpaired device '{sel.DeviceName}' ({sel.DeviceId}).");
        }
    }

    private void ListActivity_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        BtnOpenDownloads_Click(sender, e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _autoConnectCts?.Cancel();
        _clipboardService?.Dispose();
        _ = _tcpServer?.DisposeAsync();
        _udpBeacon?.Dispose();
        _ = _activeTransport?.DisposeAsync();
    }
}
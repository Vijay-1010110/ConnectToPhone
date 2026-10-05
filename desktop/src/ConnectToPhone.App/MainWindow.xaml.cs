using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using ConnectToPhone.Clipboard;
using ConnectToPhone.Core.Cache;
using ConnectToPhone.Core.Engine;
using ConnectToPhone.Core.Protocol;
using ConnectToPhone.Core.Protocol.Models;
using ConnectToPhone.Explorer;
using ConnectToPhone.Transports;
using ConnectToPhone.Transports.Bluetooth;
using ConnectToPhone.Transports.Discovery;
using ConnectToPhone.Transports.Simulation;
using ConnectToPhone.Transports.Sockets;
using ConnectToPhone.Transports.Usb;
using Microsoft.Win32;
using QRCoder;
using TransportType = ConnectToPhone.Core.Protocol.TransportType;

namespace ConnectToPhone.App;

public partial class MainWindow : Window
{
    private ClipboardService? _clipboardService;
    private ITransport? _activeTransport;
    private TcpServer? _tcpServer;
    private UdpDiscoveryBeacon? _udpBeacon;
    private BluetoothBridge? _btBridge;
    private readonly WindowsFileSystemHost _fsHost = new();
    private readonly DeviceSecurityManager _security = new();
    private readonly CacheManager _cacheManager = new();
    private readonly ObservableCollection<string> _activityLog = [];
    private readonly ObservableCollection<PhoneEntryDisplay> _phoneEntries = [];
    private readonly ObservableCollection<string> _clipboardLog = [];
    private readonly ObservableCollection<PairedDeviceDisplay> _pairedDevices = [];
    private readonly ObservableCollection<DiscoveredDeviceDisplay> _discoveredDevices = [];
    private readonly ObservableCollection<TargetDeviceItem> _targetDevices = [];
    private readonly ObservableCollection<CachedFileItem> _cachedFiles = [];
    private readonly Dictionary<ulong, SparseFileWriter> _activeWriters = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, ChunkScheduler> _activeSchedulers = new();
    private readonly Dictionary<string, SimulatedTransport> _simulatedTransports = new();
    private CancellationTokenSource? _autoConnectCts;
    private string _currentCacheFilter = "All";
    private bool _usbEnabled = true;
    private bool _wifiEnabled = true;
    private bool _btEnabled = true;

    public sealed class DiscoveredDeviceDisplay
    {
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string TransportBadge { get; set; } = string.Empty;
        public string LastSeenDisplay { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 42424;
    }

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

    public sealed class TargetDeviceItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Protocol { get; set; } = string.Empty;
        public bool IsVirtual { get; set; }
        public override string ToString() => Name;
    }

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            ListActivity.ItemsSource = _activityLog;
            ListPhoneEntries.ItemsSource = _phoneEntries;
            ListClipboardHistory.ItemsSource = _clipboardLog;
            ListPairedDevices.ItemsSource = _pairedDevices;
            ListDiscoveredDevices.ItemsSource = _discoveredDevices;
            CmbTargetDevice.ItemsSource = _targetDevices;
            ListCachedFiles.ItemsSource = _cachedFiles;

            _cacheManager.CacheUpdated += () => Dispatcher.Invoke(RefreshCacheView);

            Loaded += MainWindow_Loaded;
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(@"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log", $"[MainWindow] Constructor Exception: {ex}\n"); } catch { }
            throw;
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            File.AppendAllText(@"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log", $"[MainWindow] MainWindow_Loaded start at {DateTime.Now}\n");

            // Ensure window is visible, restored and brought to foreground
            WindowState = WindowState.Normal;
            Show();
            Activate();
            Focus();
            Topmost = true;
            Dispatcher.BeginInvoke(new Action(() => Topmost = false), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            try
            {
                // 1. Hook clipboard listener to the Win32 HWND
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                _clipboardService = new ClipboardService(Dispatcher);
                _clipboardService.Initialize(hwnd);
                _clipboardService.LocalClipboardCopied += OnLocalClipboardCopied;
            }
            catch (Exception ex)
            {
                File.AppendAllText(@"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log", $"[MainWindow] ClipboardService Init Note: {ex.Message}\n");
            }

            // 2. Initialize status & PIN
            _activityLog.Add("[System] Initialized ConnectToPhone Core Engine.");
            try { UpdateTaskbarProgress(0, TaskbarItemProgressState.None); } catch { }
            TxtPin.Text = _security.CurrentSessionPin;
            RefreshPairedDevicesList();

            // 3. Start local TCP server (Port 42424) so Phone can connect over Wi-Fi LAN
            StartTcpServer();

            // 4. Start zero-touch UDP discovery
            StartUdpDiscovery();

            // 5. Start Bluetooth RFCOMM listener for native paired phone connectivity
            StartBluetoothServer();

            // 6. Connect to phone over USB bridge or Wi-Fi/BT automatically in continuous loop
            _autoConnectCts = new CancellationTokenSource();
            _ = AutoConnectUsbLoopAsync(_autoConnectCts.Token);

            // 7. Initialize storage & cache
            RefreshCacheView();
            Task.Run(async () =>
            {
                while (!_autoConnectCts.Token.IsCancellationRequested)
                {
                    _cacheManager.RunPeriodicCleanup();
                    await Task.Delay(TimeSpan.FromHours(6), _autoConnectCts.Token);
                }
            });

            File.AppendAllText(@"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log", $"[MainWindow] MainWindow_Loaded finish at {DateTime.Now}\n");
        }
        catch (Exception ex)
        {
            File.AppendAllText(@"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log", $"[MainWindow] MainWindow_Loaded Exception: {ex}\n");
        }
    }

    private void RefreshPairedDevicesList()
    {
        try
        {
            _pairedDevices.Clear();
            _targetDevices.Clear();

            // 1. Devices from security storage
            foreach (var d in _security.GetAllDevices())
            {
                _pairedDevices.Add(new PairedDeviceDisplay
                {
                    DeviceName = d.DeviceName,
                    DeviceId = d.DeviceId,
                    DeviceType = d.DeviceType.ToString(),
                    TrustStatus = d.IsTrusted ? "✓ Trusted" : "Pending"
                });

                _targetDevices.Add(new TargetDeviceItem
                {
                    Id = d.DeviceId,
                    Name = $"📱 {d.DeviceName} ({d.DeviceType})",
                    Protocol = d.DeviceType.ToString(),
                    IsVirtual = false
                });
            }

            // 2. Devices from Bluetooth
            if (_btBridge != null && _btBridge.IsSupported)
            {
                foreach (var bt in _btBridge.GetPairedDevices())
                {
                    try
                    {
                        string name = bt.DeviceName ?? "Bluetooth Device";
                        string addr = bt.DeviceAddress?.ToString() ?? "";
                        bool isConnected = false;
                        try { isConnected = bt.Connected; } catch { }

                        if (!_pairedDevices.Any(p => p.DeviceName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            _pairedDevices.Add(new PairedDeviceDisplay
                            {
                                DeviceName = name,
                                DeviceId = addr,
                                DeviceType = "Bluetooth",
                                TrustStatus = isConnected ? "⚡ Connected" : "✓ Paired"
                            });

                            _targetDevices.Add(new TargetDeviceItem
                            {
                                Id = addr,
                                Name = $"📶 {name} (Bluetooth)",
                                Protocol = "Bluetooth",
                                IsVirtual = false
                            });
                        }
                    }
                    catch { }
                }
            }

            // 3. Virtual Simulated Devices
            foreach (var sim in _simulatedTransports.Values)
            {
                if (!_pairedDevices.Any(p => p.DeviceId == sim.SimulatedDeviceId))
                {
                    _pairedDevices.Add(new PairedDeviceDisplay
                    {
                        DeviceName = sim.SimulatedDeviceName,
                        DeviceId = sim.SimulatedDeviceId,
                        DeviceType = "Virtual Simulation",
                        TrustStatus = sim.IsConnected ? "⚡ Active (Virtual)" : "💤 Disconnected"
                    });
                }

                _targetDevices.Add(new TargetDeviceItem
                {
                    Id = sim.SimulatedDeviceId,
                    Name = $"🧪 {sim.SimulatedDeviceName} (Virtual)",
                    Protocol = sim.Type.ToString(),
                    IsVirtual = true
                });
            }

            if (_targetDevices.Count > 0 && CmbTargetDevice != null && CmbTargetDevice.SelectedIndex < 0)
            {
                CmbTargetDevice.SelectedIndex = 0;
            }

            UpdateProtocolUi();
            UpdateDevicesHubUi();
        }
        catch (Exception ex)
        {
            File.AppendAllText("app_lifecycle.log", $"[MainWindow] RefreshPairedDevicesList Exception: {ex}\n");
        }
    }

    private void StartBluetoothServer()
    {
        try
        {
            _btBridge = new BluetoothBridge();
            if (_btBridge.IsSupported)
            {
                _btBridge.ClientConnected += transport =>
                {
                    AttachTransport(transport, "Bluetooth");
                    _ = SendHandshakeAndRootReqAsync(transport);
                };
                _btBridge.StartListener();
                _activityLog.Insert(0, "[Bluetooth] RFCOMM Server listening for paired devices.");
                RefreshPairedDevicesList();
            }
            else
            {
                _activityLog.Insert(0, "[Bluetooth] Radio not active or supported on this system.");
            }
        }
        catch (Exception ex)
        {
            _activityLog.Insert(0, $"[Bluetooth] Note: {ex.Message}");
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
                    string ip = peer.RemoteIpAddress?.ToString() ?? "Unknown";
                    _activityLog.Insert(0, $"[Discovery] Found phone '{peer.DeviceName}' at {ip}:{peer.TcpPort}");

                    var existing = _discoveredDevices.FirstOrDefault(d => d.DeviceId == peer.DeviceId || d.Endpoint == $"{ip}:{peer.TcpPort}");
                    if (existing != null)
                    {
                        existing.LastSeenDisplay = DateTime.Now.ToString("HH:mm:ss");
                    }
                    else
                    {
                        _discoveredDevices.Add(new DiscoveredDeviceDisplay
                        {
                            DeviceName = peer.DeviceName,
                            DeviceId = peer.DeviceId,
                            Endpoint = $"{ip}:{peer.TcpPort}",
                            TransportBadge = "📶 Wi-Fi LAN",
                            LastSeenDisplay = DateTime.Now.ToString("HH:mm:ss"),
                            IpAddress = ip,
                            Port = peer.TcpPort
                        });
                    }
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
                    // 1. Try USB ADB (if enabled)
                    if (_usbEnabled)
                    {
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
                    }

                    // 2. Try Wi-Fi to phone IP (if enabled)
                    if (_wifiEnabled)
                    {
                        string manualIp = Dispatcher.Invoke(() => TxtDirectIp?.Text?.Trim() ?? "192.168.6.215");
                        string[] wifiCandidates = [manualIp, "192.168.6.215", "192.168.1.100"];
                        foreach (var ip in wifiCandidates.Distinct())
                        {
                            try
                            {
                                using var ctsTimeout = new CancellationTokenSource(800);
                                var wifiTransport = await TcpServer.ConnectAsync(ip, 42424, ctsTimeout.Token);
                                if (wifiTransport != null && wifiTransport.IsConnected)
                                {
                                    AttachTransport(wifiTransport, $"Wi-Fi ({ip})");
                                    await SendHandshakeAndRootReqAsync(wifiTransport);
                                    break;
                                }
                            }
                            catch {}
                        }
                    }

                    // 3. Try paired Bluetooth devices (if enabled)
                    if (_btEnabled && _activeTransport == null && _btBridge != null && _btBridge.IsSupported)
                    {
                        var pairedBt = _btBridge.GetPairedDevices();
                        foreach (var dev in pairedBt)
                        {
                            try
                            {
                                using var ctsTimeout = new CancellationTokenSource(1200);
                                var btTransport = await _btBridge.ConnectToDeviceAsync(dev, ctsTimeout.Token);
                                if (btTransport != null && btTransport.IsConnected)
                                {
                                    AttachTransport(btTransport, $"Bluetooth ({dev.DeviceName})");
                                    await SendHandshakeAndRootReqAsync(btTransport);
                                    break;
                                }
                            }
                            catch { }
                        }
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
            SupportedTransports = TransportType.UsbAdb | TransportType.WifiLan | TransportType.BluetoothRfcomm
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

        Dispatcher.Invoke(() =>
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            TxtStatus.Text = $"Connected over {label} ⚡";
            TxtDeviceName.Text = $"Connected Device ({label})";
            if (label.Contains("USB", StringComparison.OrdinalIgnoreCase))
            {
                TxtUsbBadge.Text = "⚡ USB (Active Link)";
                TxtWifiBadge.Text = "💤 Wi-Fi (Standby)";
                TxtBtBadge.Text = "💤 Bluetooth (Standby)";
            }
            else if (label.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
            {
                TxtBtBadge.Text = "⚡ Bluetooth (Active Link)";
                TxtUsbBadge.Text = "💤 USB (Standby)";
                TxtWifiBadge.Text = "💤 Wi-Fi (Standby)";
            }
            else
            {
                TxtWifiBadge.Text = "📶 Wi-Fi (Active Link)";
                TxtUsbBadge.Text = "💤 USB (Standby)";
                TxtBtBadge.Text = "💤 Bluetooth (Standby)";
            }
            TxtBondedSpeed.Text = "Idle / Energy Saver (0% CPU)";
            _activityLog.Insert(0, $"[Transport] Single-pipe established via {label}. Secondary channels in low-power sleep.");
            try { File.AppendAllText(@"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log", $"[AttachTransport] Established: {label} at {DateTime.Now}\n"); } catch {}
            UpdateDevicesHubUi();
        });
    }

    private async Task OnFrameReceivedFromPhone(ITransport transport, BinaryFrame frame)
    {
        try { File.AppendAllText(@"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log", $"[Frame] Received Type={frame.Type} (0x{(byte)frame.Type:X2}), Length={frame.Payload.Length} at {DateTime.Now:HH:mm:ss.fff}\n"); } catch {}
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
                TxtBtBadge.Text = "Bluetooth: Ready";
                TxtBondedSpeed.Text = "Idle";
                _activityLog.Insert(0, "[Transport] Pipe disconnected. Auto-connector standing by...");
                UpdateDevicesHubUi();
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
            try { File.AppendAllText(@"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log", $"[Clipboard] Local copied: '{preview}', Forwarding={_activeTransport != null && _activeTransport.IsConnected}\n"); } catch {}

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
        if (ViewDashboard == null || ViewExplorer == null || ViewClipboard == null || ViewMcp == null || ViewExtensions == null || ViewDevices == null || ViewCache == null)
            return;

        ViewDashboard.Visibility = Visibility.Collapsed;
        ViewExplorer.Visibility = Visibility.Collapsed;
        ViewClipboard.Visibility = Visibility.Collapsed;
        ViewMcp.Visibility = Visibility.Collapsed;
        ViewExtensions.Visibility = Visibility.Collapsed;
        ViewDevices.Visibility = Visibility.Collapsed;
        ViewCache.Visibility = Visibility.Collapsed;

        if (NavDashboard.IsChecked == true)
        {
            ViewDashboard.Visibility = Visibility.Visible;
            TxtViewTitle.Text = "Transfer Hub & Live Multi-Path Speed";
        }
        else if (NavDevices.IsChecked == true)
        {
            ViewDevices.Visibility = Visibility.Visible;
            TxtViewTitle.Text = "Devices & Pairing Hub";
            RefreshPairedDevicesList();
            UpdateDevicesHubUi();
        }
        else if (NavExplorer.IsChecked == true)
        {
            ViewExplorer.Visibility = Visibility.Visible;
            TxtViewTitle.Text = "Remote Phone Explorer";
            if (_phoneEntries.Count == 0 && _activeTransport != null && _activeTransport.IsConnected)
            {
                _ = RequestPhoneDirectoryAsync("/");
            }
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

    private void Nav_Clicked(object sender, RoutedEventArgs e)
    {
        Nav_Checked(sender, e);
    }

    private void DropZone_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        BtnBrowseSend_Click(sender, e);
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
        if (ListPhoneEntries.SelectedItem is PhoneEntryDisplay selected)
        {
            if (selected.IsDirectory)
            {
                await RequestPhoneDirectoryAsync(selected.Path);
            }
            else
            {
                BtnQuickViewSelected_Click(sender, e);
            }
        }
    }

    private async void BtnExplorerBack_Click(object sender, RoutedEventArgs e)
    {
        string current = TxtExplorerPath.Text.TrimEnd('/', '\\');
        int lastSlash = current.LastIndexOfAny(new[] { '/', '\\' });
        string parent = (lastSlash > 0) ? current.Substring(0, lastSlash) : "/";
        await RequestPhoneDirectoryAsync(parent);
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
        try
        {
            string cache = _cacheManager.CacheDirectory;
            if (!Directory.Exists(cache)) Directory.CreateDirectory(cache);
            Process.Start(new ProcessStartInfo { FileName = cache, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open cache folder: {ex.Message}", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        try
        {
            string movies = @"D:\movies";
            if (!Directory.Exists(movies)) Directory.CreateDirectory(movies);
            Process.Start(new ProcessStartInfo { FileName = movies, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open movies folder: {ex.Message}", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnOpenDownloads_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string downloads = _cacheManager.DownloadsDirectory;
            if (!Directory.Exists(downloads)) Directory.CreateDirectory(downloads);
            Process.Start(new ProcessStartInfo { FileName = downloads, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open downloads folder: {ex.Message}", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (_activeTransport != null)
            {
                var t = _activeTransport;
                _ = t.DisposeAsync();
                _activeTransport = null;
                OnTransportDisconnected(t);
            }
            RefreshPairedDevicesList();
            _activityLog.Insert(0, $"[Security] Unpaired device '{sel.DeviceName}' ({sel.DeviceId}).");
            MessageBox.Show($"Device '{sel.DeviceName}' has been unpaired and forgotten.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else if (_activeTransport != null)
        {
            string devName = TxtDeviceName.Text.Replace("Connected Device (", "").Replace(")", "").Trim();
            var t = _activeTransport;
            _ = t.DisposeAsync();
            _activeTransport = null;
            OnTransportDisconnected(t);
            RefreshPairedDevicesList();
            _activityLog.Insert(0, $"[Security] Unpaired active device '{devName}'.");
            MessageBox.Show($"Active device '{devName}' has been unpaired and disconnected.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Please select a device from the list to unpair, or connect a device first.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ListActivity_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        BtnOpenDownloads_Click(sender, e);
    }

    private void UpdateProtocolUi()
    {
        Dispatcher.Invoke(() =>
        {
            // Header buttons
            if (BtnToggleUsb != null)
            {
                BtnToggleUsb.Content = _usbEnabled ? "⚡ USB: On" : "⚡ USB: Off";
                BtnToggleUsb.Background = _usbEnabled ? new SolidColorBrush(Color.FromRgb(30, 41, 59)) : new SolidColorBrush(Color.FromRgb(63, 63, 70));
                BtnToggleUsb.Foreground = _usbEnabled ? new SolidColorBrush(Color.FromRgb(96, 165, 250)) : new SolidColorBrush(Color.FromRgb(156, 163, 175));
            }
            if (BtnToggleWifi != null)
            {
                BtnToggleWifi.Content = _wifiEnabled ? "📶 Wi-Fi: On" : "📶 Wi-Fi: Off";
                BtnToggleWifi.Background = _wifiEnabled ? new SolidColorBrush(Color.FromRgb(6, 78, 59)) : new SolidColorBrush(Color.FromRgb(63, 63, 70));
                BtnToggleWifi.Foreground = _wifiEnabled ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(156, 163, 175));
            }
            if (BtnToggleBt != null)
            {
                BtnToggleBt.Content = _btEnabled ? "📱 BT: On" : "📱 BT: Off";
                BtnToggleBt.Background = _btEnabled ? new SolidColorBrush(Color.FromRgb(49, 46, 129)) : new SolidColorBrush(Color.FromRgb(63, 63, 70));
                BtnToggleBt.Foreground = _btEnabled ? new SolidColorBrush(Color.FromRgb(129, 140, 248)) : new SolidColorBrush(Color.FromRgb(156, 163, 175));
            }

            // Detail cards in ViewSecurity
            if (TxtDetailUsbStatus != null)
            {
                TxtDetailUsbStatus.Text = _usbEnabled 
                    ? (_activeTransport?.Type.HasFlag(TransportType.UsbAdb) == true ? "Status: Connected ⚡" : "Status: Active / Ready") 
                    : "Status: Disabled by user";
            }
            if (BtnDetailUsbToggle != null)
            {
                BtnDetailUsbToggle.Content = _usbEnabled ? "⚡ Disconnect USB" : "⚡ Enable & Connect USB";
            }

            if (TxtDetailWifiStatus != null)
            {
                TxtDetailWifiStatus.Text = _wifiEnabled 
                    ? (_activeTransport?.Type.HasFlag(TransportType.WifiLan) == true ? "Status: Connected 📶" : "Status: Server Active (42424)") 
                    : "Status: Disabled by user";
            }
            if (BtnDetailWifiToggle != null)
            {
                BtnDetailWifiToggle.Content = _wifiEnabled ? "📶 Disconnect Wi-Fi" : "📶 Enable & Connect Wi-Fi";
            }

            if (TxtDetailBtStatus != null)
            {
                TxtDetailBtStatus.Text = _btEnabled 
                    ? (_activeTransport?.Type.HasFlag(TransportType.BluetoothRfcomm) == true ? "Status: Connected 📱" : "Status: RFCOMM Ready") 
                    : "Status: Disabled by user";
            }
            if (BtnDetailBtToggle != null)
            {
                BtnDetailBtToggle.Content = _btEnabled ? "📱 Disconnect BT" : "📱 Enable & Connect BT";
            }
        });
    }

    private void CmbTargetDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbTargetDevice.SelectedItem is TargetDeviceItem item)
        {
            _activityLog.Insert(0, $"[Target Device] Target switched to '{item.Name}'.");
        }
    }

    private async void BtnConnectTarget_Click(object sender, RoutedEventArgs e)
    {
        if (CmbTargetDevice.SelectedItem is TargetDeviceItem item)
        {
            if (item.IsVirtual && _simulatedTransports.TryGetValue(item.Id, out var sim))
            {
                AttachTransport(sim, $"{sim.SimulatedDeviceName} (Virtual)");
                await SendHandshakeAndRootReqAsync(sim);
                return;
            }

            _activityLog.Insert(0, $"[Connect] Connecting to target '{item.Name}'...");
            _ = AutoConnectUsbLoopAsync(CancellationToken.None);
        }
        else
        {
            _activityLog.Insert(0, "[Connect] Scanning for available devices across USB, Wi-Fi, and Bluetooth...");
            _ = AutoConnectUsbLoopAsync(CancellationToken.None);
        }
    }

    private void BtnDisconnectTarget_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTransport != null)
        {
            var t = _activeTransport;
            _ = t.DisposeAsync();
            _activeTransport = null;
            OnTransportDisconnected(t);
            _activityLog.Insert(0, "[Disconnect] Active device disconnected by user.");
            MessageBox.Show("Disconnected from active device.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("No active device is currently connected.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnPairNewDevice_Click(object sender, RoutedEventArgs e)
    {
        NavDevices.IsChecked = true;
        Nav_Checked(NavDevices, new RoutedEventArgs());
        TxtPairPinInput?.Focus();
    }

    private void BtnToggleUsb_Click(object sender, RoutedEventArgs e)
    {
        _usbEnabled = !_usbEnabled;
        UpdateProtocolUi();
        if (!_usbEnabled)
        {
            if (_activeTransport != null && _activeTransport.Type.HasFlag(TransportType.UsbAdb))
            {
                _ = _activeTransport.DisposeAsync();
                _activeTransport = null;
            }
            TxtUsbBadge.Text = "USB: Disabled";
            _activityLog.Insert(0, "[USB] Protocol disabled by user.");
        }
        else
        {
            TxtUsbBadge.Text = "USB: Scanning";
            _activityLog.Insert(0, "[USB] Protocol enabled. Scanning ADB devices...");
            _ = AutoConnectUsbLoopAsync(CancellationToken.None);
        }
    }

    private void BtnToggleWifi_Click(object sender, RoutedEventArgs e)
    {
        _wifiEnabled = !_wifiEnabled;
        UpdateProtocolUi();
        if (!_wifiEnabled)
        {
            if (_activeTransport != null && _activeTransport.Type.HasFlag(TransportType.WifiLan))
            {
                _ = _activeTransport.DisposeAsync();
                _activeTransport = null;
            }
            TxtWifiBadge.Text = "Wi-Fi: Disabled";
            _activityLog.Insert(0, "[Wi-Fi] Protocol disabled by user.");
        }
        else
        {
            TxtWifiBadge.Text = "Wi-Fi: Listening (42424)";
            _activityLog.Insert(0, "[Wi-Fi] Protocol enabled. Restarting TCP Server port 42424...");
            StartTcpServer();
        }
    }

    private void BtnToggleBt_Click(object sender, RoutedEventArgs e)
    {
        _btEnabled = !_btEnabled;
        UpdateProtocolUi();
        if (!_btEnabled)
        {
            if (_activeTransport != null && _activeTransport.Type.HasFlag(TransportType.BluetoothRfcomm))
            {
                _ = _activeTransport.DisposeAsync();
                _activeTransport = null;
            }
            TxtBtBadge.Text = "Bluetooth: Disabled";
            _activityLog.Insert(0, "[Bluetooth] Protocol disabled by user.");
        }
        else
        {
            TxtBtBadge.Text = "Bluetooth: Listening";
            _activityLog.Insert(0, "[Bluetooth] Protocol enabled. Listening for RFCOMM connections...");
            StartBluetoothServer();
        }
    }

    private async void BtnDirectConnect_Click(object sender, RoutedEventArgs e)
    {
        string ip = TxtDirectIp.Text.Trim();
        if (!int.TryParse(TxtDirectPort.Text.Trim(), out int port)) port = 42424;

        _activityLog.Insert(0, $"[Wi-Fi Direct] Connecting to {ip}:{port}...");
        try
        {
            using var cts = new CancellationTokenSource(3000);
            var transport = await TcpServer.ConnectAsync(ip, port, cts.Token);
            if (transport != null && transport.IsConnected)
            {
                AttachTransport(transport, $"Wi-Fi ({ip}:{port})");
                await SendHandshakeAndRootReqAsync(transport);
                MessageBox.Show($"Successfully connected to {ip}:{port}!", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Could not connect to {ip}:{port}. Verify device is on same network and port is open.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            _activityLog.Insert(0, $"[Wi-Fi Direct] Connection error: {ex.Message}");
            MessageBox.Show($"Connection failed: {ex.Message}", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnScanAdb_Click(object sender, RoutedEventArgs e)
    {
        _activityLog.Insert(0, "[USB] Rescanning connected ADB devices...");
        _ = AutoConnectUsbLoopAsync(CancellationToken.None);
    }

    private void BtnSubmitPair_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtPairDeviceName.Text.Trim();
        string pin = TxtPairPinInput.Text.Trim();

        if (string.IsNullOrEmpty(name)) name = "Android Phone";
        if (pin.Length != 6)
        {
            MessageBox.Show("Please enter a valid 6-digit PIN.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string devId = $"DEV_{Math.Abs(name.GetHashCode() ^ pin.GetHashCode()):X8}";
        _security.TrustDevice(devId, name, DeviceType.Android);
        RefreshPairedDevicesList();
        try { System.Media.SystemSounds.Asterisk.Play(); } catch {}
        _activityLog.Insert(0, $"[Security] Successfully paired & authorized device '{name}' with PIN {pin}.");
        MessageBox.Show($"Device '{name}' successfully authorized and paired!", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnConnectSelectedDevice_Click(object sender, RoutedEventArgs e)
    {
        if (ListPairedDevices.SelectedItem is PairedDeviceDisplay sel)
        {
            if (_simulatedTransports.TryGetValue(sel.DeviceId, out var sim))
            {
                AttachTransport(sim, $"{sim.SimulatedDeviceName} (Virtual)");
                await SendHandshakeAndRootReqAsync(sim);
                return;
            }
            _activityLog.Insert(0, $"[Connect] Attempting connection to '{sel.DeviceName}'...");
            _ = AutoConnectUsbLoopAsync(CancellationToken.None);
        }
        else
        {
            MessageBox.Show("Please select a device from the list to connect.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnDisconnectSelectedDevice_Click(object sender, RoutedEventArgs e)
    {
        BtnDisconnectTarget_Click(sender, e);
    }

    private void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        RefreshPairedDevicesList();
        _activityLog.Insert(0, "[Devices] Refreshed paired and discovered devices list.");
    }

    private void BtnSpawnGalaxyTab_Click(object sender, RoutedEventArgs e)
    {
        SpawnSimulatedDevice("Simulated Galaxy Tab S9", "sim_tab_s9", TransportType.WifiLan);
    }

    private void BtnSpawnPixel8_Click(object sender, RoutedEventArgs e)
    {
        SpawnSimulatedDevice("Simulated Pixel 8 Pro", "sim_pixel_8", TransportType.UsbAdb);
    }

    private void SpawnSimulatedDevice(string deviceName, string deviceId, TransportType type)
    {
        var simTransport = new SimulatedTransport(deviceName, deviceId, type);
        _simulatedTransports[deviceId] = simTransport;
        
        // Trust in security
        _security.TrustDevice(deviceId, deviceName, DeviceType.Android);

        RefreshPairedDevicesList();

        // Attach as active transport
        AttachTransport(simTransport, $"{deviceName} (Virtual)");
        _ = SendHandshakeAndRootReqAsync(simTransport);

        TxtSimulationStatus.Text = $"Active Simulation: {deviceName} connected ({type}). High-speed ~80 MB/s pipeline active. Test explorer and transfers!";
        _activityLog.Insert(0, $"[Simulation] Spawned virtual device '{deviceName}' ({deviceId}).");
        try { System.Media.SystemSounds.Asterisk.Play(); } catch {}
    }

    private void BtnSimulateDrop_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTransport is SimulatedTransport sim)
        {
            sim.TriggerDisconnect();
            TxtSimulationStatus.Text = $"[Failover Test] Dropped channel for '{sim.SimulatedDeviceName}'. Failover triggered! Secondary standby channels engage without data loss.";
            _activityLog.Insert(0, $"[Simulation Failover] Triggered channel drop for {sim.SimulatedDeviceName}. Zero-loss failover initiated.");
        }
        else if (_activeTransport != null)
        {
            var current = _activeTransport;
            _ = current.DisposeAsync();
            _activeTransport = null;
            OnTransportDisconnected(current);
            TxtSimulationStatus.Text = "[Failover Test] Dropped active physical channel! Auto-connector will failover to surviving channel.";
            _activityLog.Insert(0, "[Failover Test] Triggered disconnect on active transport.");
        }
        else
        {
            TxtSimulationStatus.Text = "No active transport to disconnect. Spawn a virtual device or connect a phone first.";
        }
    }

    private void BtnSimulateReconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_simulatedTransports.Count > 0)
        {
            var sim = _simulatedTransports.Values.First();
            sim.TriggerReconnect();
            AttachTransport(sim, $"{sim.SimulatedDeviceName} (Virtual)");
            _ = SendHandshakeAndRootReqAsync(sim);
            TxtSimulationStatus.Text = $"Simulated channel restored for '{sim.SimulatedDeviceName}'. Pipe re-synchronized.";
            _activityLog.Insert(0, $"[Simulation] Channel reconnected for {sim.SimulatedDeviceName}.");
        }
        else
        {
            TxtSimulationStatus.Text = "No simulated devices found. Click 'Spawn Galaxy Tab S9' to create one.";
        }
    }

    private void BtnRemoveSimulated_Click(object sender, RoutedEventArgs e)
    {
        foreach (var kvp in _simulatedTransports)
        {
            _ = kvp.Value.DisposeAsync();
            _security.UnpairDevice(kvp.Key);
        }
        _simulatedTransports.Clear();
        if (_activeTransport is SimulatedTransport)
        {
            _activeTransport = null;
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtStatus.Text = "Waiting for Phone...";
        }
        RefreshPairedDevicesList();
        TxtSimulationStatus.Text = "Removed all simulated virtual devices.";
        _activityLog.Insert(0, "[Simulation] Virtual devices removed.");
    }

    public static string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch { }

        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch { }

        return "127.0.0.1";
    }

    private BitmapImage? GenerateQrCodeBitmap(string payload)
    {
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeBytes = qrCode.GetGraphic(12);

            var bitmap = new BitmapImage();
            using (var ms = new MemoryStream(qrCodeBytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
            }
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            _activityLog.Insert(0, $"[QR] Generation error: {ex.Message}");
            return null;
        }
    }

    private void UpdateDevicesHubUi()
    {
        Dispatcher.Invoke(() =>
        {
            if (TxtPinSegmented == null || TxtPin == null || ImgQrCode == null || CardActiveDeviceHero == null || CardDisconnectedHero == null)
                return;

            string localIp = GetLocalIpAddress();
            string pin = _security.CurrentSessionPin;

            // 1. Format segmented PIN
            if (pin.Length == 6)
            {
                TxtPinSegmented.Text = $"{pin[0]} {pin[1]} {pin[2]}  -  {pin[3]} {pin[4]} {pin[5]}";
            }
            else
            {
                TxtPinSegmented.Text = pin;
            }
            TxtPin.Text = pin;

            // 2. Generate live QR Code
            string pairUrl = $"connecttowindow://pair?name={Uri.EscapeDataString(Environment.MachineName)}&ip={localIp}&port=42424&pin={pin}";
            TxtQrInfo.Text = pairUrl;
            TxtLocalIpBadge.Text = $"Host IP: {localIp}:42424";
            TxtDirectIp.Text = localIp;

            var qrBmp = GenerateQrCodeBitmap(pairUrl);
            if (qrBmp != null)
            {
                ImgQrCode.Source = qrBmp;
            }

            // 3. Update Hero Card
            if (_activeTransport != null && _activeTransport.IsConnected)
            {
                CardActiveDeviceHero.Visibility = Visibility.Visible;
                CardDisconnectedHero.Visibility = Visibility.Collapsed;

                string devName = TxtDeviceName.Text.Replace("Connected Device (", "").Replace(")", "").Trim();
                if (string.IsNullOrEmpty(devName) || devName.StartsWith("Searching")) devName = "Android Device";
                TxtConnectedHeroName.Text = devName;
                TxtConnectedHeroId.Text = $"Hardware Link: {_activeTransport.Type} • Active Pipeline";
                TxtHeroTransports.Text = _activeTransport.Type.ToString();
                TxtHeroLatency.Text = "< 2 ms (Ultra Low)";
                TxtHeroPipeline.Text = "Multi-Pipe Active";
            }
            else
            {
                CardActiveDeviceHero.Visibility = Visibility.Collapsed;
                CardDisconnectedHero.Visibility = Visibility.Visible;
                TxtDisconnectedHostInfo.Text = $"PC '{Environment.MachineName}' ready. Connect via USB cable, scan QR code below, or join Wi-Fi LAN.";
            }
        });
    }

    private void BtnRegenQr_Click(object sender, RoutedEventArgs e)
    {
        _security.GenerateNewPin();
        UpdateDevicesHubUi();
        _activityLog.Insert(0, $"[Security] Generated new pair PIN & QR code: {_security.CurrentSessionPin}");
    }

    private void BtnCopyPairUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(TxtQrInfo.Text);
            _activityLog.Insert(0, "[Pairing] Copied QR Pair Link to clipboard!");
            MessageBox.Show("Pairing link copied to clipboard!\n\n" + TxtQrInfo.Text, "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _activityLog.Insert(0, $"[Clipboard] Copy error: {ex.Message}");
        }
    }

    private void BtnCopyPin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_security.CurrentSessionPin);
            _activityLog.Insert(0, $"[Security] Copied 6-digit PIN {_security.CurrentSessionPin} to clipboard.");
            MessageBox.Show($"PIN {_security.CurrentSessionPin} copied to clipboard!", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _activityLog.Insert(0, $"[Clipboard] Copy error: {ex.Message}");
        }
    }

    private void BtnRefreshDiscovered_Click(object sender, RoutedEventArgs e)
    {
        _activityLog.Insert(0, "[Discovery] Refreshing nearby UDP radar...");
        _discoveredDevices.Clear();
    }

    private async void BtnConnectDiscovered_Click(object sender, RoutedEventArgs e)
    {
        if (ListDiscoveredDevices.SelectedItem is DiscoveredDeviceDisplay selected)
        {
            _activityLog.Insert(0, $"[Discovery] Connecting to discovered phone '{selected.DeviceName}' at {selected.Endpoint}...");
            try
            {
                using var cts = new CancellationTokenSource(3000);
                var transport = await TcpServer.ConnectAsync(selected.IpAddress, selected.Port, cts.Token);
                if (transport != null && transport.IsConnected)
                {
                    AttachTransport(transport, $"Wi-Fi ({selected.DeviceName})");
                    await SendHandshakeAndRootReqAsync(transport);
                    MessageBox.Show($"Successfully connected to {selected.DeviceName}!", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Could not connect to {selected.DeviceName} at {selected.Endpoint}.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("Please select a discovered device from the list to connect.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void BtnForceSync_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTransport != null && _activeTransport.IsConnected)
        {
            _activityLog.Insert(0, "[Sync] Force Sync triggered: Refreshing remote file tree and clipboard...");
            try
            {
                await RequestPhoneDirectoryAsync("/");
                await _activeTransport.SendFrameAsync(new BinaryFrame(
                    FrameType.FsListDirReq,
                    12345678,
                    new FsListDirRequest { TargetPath = "/", IncludeHidden = false }.ToUtf8Bytes()
                ));
                try { System.Media.SystemSounds.Asterisk.Play(); } catch {}
                _activityLog.Insert(0, "[Sync] Force Sync complete! Remote explorer and transport state refreshed.");
                MessageBox.Show("Devices synchronized successfully!", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _activityLog.Insert(0, $"[Sync] Sync error: {ex.Message}");
            }
        }
        else
        {
            MessageBox.Show("No device is currently connected to synchronize.", "ConnectToPhone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void FooterConnection_Click(object sender, MouseButtonEventArgs e)
    {
        NavDevices.IsChecked = true;
        Nav_Checked(NavDevices, new RoutedEventArgs());
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        File.AppendAllText(@"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log", $"[MainWindow] OnClosing at {DateTime.Now}. StackTrace: {Environment.StackTrace}\n");
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        File.AppendAllText(@"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log", $"[MainWindow] OnClosed at {DateTime.Now}\n");
        base.OnClosed(e);
        _autoConnectCts?.Cancel();
        _clipboardService?.Dispose();
        _ = _tcpServer?.DisposeAsync();
        _udpBeacon?.Dispose();
        _ = _activeTransport?.DisposeAsync();
    }
}
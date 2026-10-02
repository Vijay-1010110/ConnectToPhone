using System.Diagnostics;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace ConnectToPhone.Transports.Bluetooth;

public sealed class BluetoothBridge : IAsyncDisposable
{
    public static readonly Guid ServiceUuid = BluetoothService.SerialPort; // Standard SPP (00001101-0000-1000-8000-00805F9B34FB)

    private BluetoothListener? _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private bool _isDisposed;

    public bool IsSupported { get; private set; }
    public bool IsListening => _listener?.Active ?? false;

    public event Action<BluetoothRfcommTransport>? ClientConnected;

    public BluetoothBridge()
    {
        try
        {
            var radio = BluetoothRadio.Default;
            IsSupported = radio != null;
        }
        catch
        {
            IsSupported = false;
        }
    }

    public void StartListener()
    {
        if (!IsSupported || IsListening) return;

        try
        {
            _listener = new BluetoothListener(ServiceUuid)
            {
                ServiceName = "ConnectToWindow"
            };
            _listener.Start();
            _listenTask = Task.Run(AcceptLoopAsync);
            Debug.WriteLine("[Bluetooth] RFCOMM Server started on SerialPort Service.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Bluetooth] Failed to start RFCOMM listener: {ex.Message}");
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested && _listener != null && _listener.Active)
            {
                var client = await Task.Run(() => _listener.AcceptBluetoothClient(), _cts.Token).ConfigureAwait(false);
                if (client != null && client.Connected)
                {
                    var transport = new BluetoothRfcommTransport(client, $"bt_{client.RemoteMachineName}");
                    try
                    {
                        ClientConnected?.Invoke(transport);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Bluetooth] Error dispatching ClientConnected: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Bluetooth] Accept loop ended: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns all paired devices on this Windows PC (e.g. vivo Y21, etc.).
    /// </summary>
    public IReadOnlyList<BluetoothDeviceInfo> GetPairedDevices()
    {
        if (!IsSupported) return [];
        try
        {
            using var client = new BluetoothClient();
            return client.PairedDevices.ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Attempts to connect to a paired phone by name or address over RFCOMM.
    /// </summary>
    public async Task<BluetoothRfcommTransport?> ConnectToDeviceAsync(BluetoothDeviceInfo device, CancellationToken ct = default)
    {
        try
        {
            var client = new BluetoothClient();
            await Task.Run(() => client.Connect(device.DeviceAddress, ServiceUuid), ct).ConfigureAwait(false);
            if (client.Connected)
            {
                return new BluetoothRfcommTransport(client, $"bt_{device.DeviceName}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Bluetooth] Connect to {device.DeviceName} failed: {ex.Message}");
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _cts.Cancel();
            _listener?.Stop();
            _cts.Dispose();
        }
        catch { }

        await Task.CompletedTask;
    }
}

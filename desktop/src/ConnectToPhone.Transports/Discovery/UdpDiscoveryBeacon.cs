using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConnectToPhone.Core.Protocol;

namespace ConnectToPhone.Transports.Discovery;

public sealed class DiscoveredPeer
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    [JsonPropertyName("deviceType")]
    public DeviceType DeviceType { get; set; }

    [JsonPropertyName("tcpPort")]
    public int TcpPort { get; set; } = 42424;

    [JsonPropertyName("transports")]
    public TransportType SupportedTransports { get; set; }

    [JsonIgnore]
    public IPAddress? RemoteIpAddress { get; set; }

    [JsonIgnore]
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

public sealed class UdpDiscoveryBeacon : IDisposable
{
    public const int DefaultDiscoveryPort = 42425;

    private readonly DiscoveredPeer _localInfo;
    private readonly UdpClient _udpListener;
    private readonly UdpClient _udpSender;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private Task? _broadcastTask;
    private bool _isDisposed;

    public event Action<DiscoveredPeer>? PeerDiscovered;

    public UdpDiscoveryBeacon(string deviceId, string deviceName, int tcpPort = 42424)
    {
        _localInfo = new DiscoveredPeer
        {
            DeviceId = deviceId,
            DeviceName = deviceName,
            DeviceType = DeviceType.Windows,
            TcpPort = tcpPort,
            SupportedTransports = TransportType.WifiLan | TransportType.UsbAdb
        };

        _udpListener = new UdpClient();
        _udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, DefaultDiscoveryPort));

        _udpSender = new UdpClient();
        _udpSender.EnableBroadcast = true;
    }

    public void Start()
    {
        _listenTask = Task.Run(ListenLoopAsync);
        _broadcastTask = Task.Run(BroadcastLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                UdpReceiveResult result = await _udpListener.ReceiveAsync(_cts.Token).ConfigureAwait(false);
                string json = Encoding.UTF8.GetString(result.Buffer);

                try
                {
                    var peer = JsonSerializer.Deserialize<DiscoveredPeer>(json);
                    if (peer != null && peer.DeviceId != _localInfo.DeviceId)
                    {
                        peer.RemoteIpAddress = result.RemoteEndPoint.Address;
                        peer.LastSeen = DateTime.UtcNow;
                        PeerDiscovered?.Invoke(peer);
                    }
                }
                catch { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"UDP discovery listen error: {ex.Message}");
        }
    }

    private async Task BroadcastLoopAsync()
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(_localInfo);
        IPEndPoint broadcastEp = new(IPAddress.Broadcast, DefaultDiscoveryPort);

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await _udpSender.SendAsync(payload, payload.Length, broadcastEp).ConfigureAwait(false);
                await Task.Delay(2000, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"UDP discovery broadcast error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts.Cancel();
        _udpListener.Dispose();
        _udpSender.Dispose();
        _cts.Dispose();
    }
}

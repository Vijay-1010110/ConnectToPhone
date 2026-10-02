using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ConnectToPhone.Core.Protocol;

namespace ConnectToPhone.Transports.Sockets;

public sealed class TcpServer : IAsyncDisposable
{
    public const int DefaultPort = 42424;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private bool _isDisposed;

    public int Port { get; }
    public bool IsListening { get; private set; }

    public event Action<TcpSocketTransport>? ClientConnected;

    public TcpServer(int port = DefaultPort)
    {
        Port = port;
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public void Start()
    {
        if (IsListening) return;
        _listener.Start();
        IsListening = true;
        _listenTask = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                Socket socket = await _listener.AcceptSocketAsync(_cts.Token).ConfigureAwait(false);
                var transport = new TcpSocketTransport(socket, TransportType.WifiLan);
                try
                {
                    ClientConnected?.Invoke(transport);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TcpServer] Client handler error: {ex.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_cts.Token.IsCancellationRequested) break;
                Debug.WriteLine($"[TcpServer] AcceptSocketAsync error: {ex.Message}");
                await Task.Delay(200).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Connects to a remote peer (e.g. Android phone IP on Wi-Fi).
    /// </summary>
    public static async Task<TcpSocketTransport> ConnectAsync(string host, int port = DefaultPort, CancellationToken ct = default)
    {
        Socket socket = new(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        await socket.ConnectAsync(host, port, ct).ConfigureAwait(false);
        var transport = new TcpSocketTransport(socket, TransportType.WifiLan);
        return transport;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        IsListening = false;

        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
        await Task.CompletedTask;
    }
}

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
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                Socket socket = await _listener.AcceptSocketAsync(_cts.Token).ConfigureAwait(false);
                var transport = new TcpSocketTransport(socket, TransportType.WifiLan);
                ClientConnected?.Invoke(transport);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Listener stopped
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

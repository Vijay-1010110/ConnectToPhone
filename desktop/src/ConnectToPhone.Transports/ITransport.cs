using ConnectToPhone.Core.Protocol;

namespace ConnectToPhone.Transports;

public interface ITransport : IAsyncDisposable
{
    string ChannelId { get; }
    TransportType Type { get; }
    bool IsConnected { get; }
    double CurrentSpeedBytesPerSec { get; }

    void StartReceiving();
    Task SendFrameAsync(BinaryFrame frame, CancellationToken ct = default);
    
    event Func<ITransport, BinaryFrame, Task>? FrameReceived;
    event Action<ITransport>? Disconnected;
}

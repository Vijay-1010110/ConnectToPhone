using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;
using ConnectToPhone.Core.Protocol;

namespace ConnectToPhone.Transports.Sockets;

public sealed class TcpSocketTransport : ITransport
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _readLoopTask;
    private long _bytesTransferredWindow;
    private readonly Stopwatch _speedStopwatch = Stopwatch.StartNew();
    private double _calculatedSpeed;
    private bool _isDisposed;

    public string ChannelId { get; }
    public TransportType Type { get; }
    public bool IsConnected => !_isDisposed && _socket.Connected;
    public double CurrentSpeedBytesPerSec => _calculatedSpeed;

    public event Func<ITransport, BinaryFrame, Task>? FrameReceived;
    public event Action<ITransport>? Disconnected;

    public TcpSocketTransport(Socket socket, TransportType type = TransportType.WifiLan, string? channelId = null)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _socket.NoDelay = true; // Disable Nagle's algorithm for instant low-latency delivery
        _stream = new NetworkStream(_socket, ownsSocket: false);
        Type = type;
        ChannelId = channelId ?? $"{type}_{Guid.NewGuid():N}";
    }

    public void StartReceiving()
    {
        _readLoopTask = Task.Run(ReceiveLoopAsync);
    }

    public async Task SendFrameAsync(BinaryFrame frame, CancellationToken ct = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Transport is disconnected.");

        byte[] serialized = frame.Serialize();

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(serialized, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);

            RecordBytesTransferred(serialized.Length);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync()
    {
        byte[] headerBuffer = new byte[BinaryFrame.HeaderSize];
        byte[] checksumBuffer = new byte[BinaryFrame.ChecksumSize];

        try
        {
            while (!_cts.Token.IsCancellationRequested && IsConnected)
            {
                // 1. Read 16-byte fixed header
                await _stream.ReadExactlyAsync(headerBuffer, _cts.Token).ConfigureAwait(false);
                RecordBytesTransferred(BinaryFrame.HeaderSize);

                ushort magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(headerBuffer.AsSpan(0, 2));
                if (magic != BinaryFrame.MagicMarker)
                {
                    // Out of sync: scan byte-by-byte until magic marker is found
                    byte[] singleByte = new byte[1];
                    ushort window = magic;
                    while (!_cts.Token.IsCancellationRequested && window != BinaryFrame.MagicMarker)
                    {
                        int r = await _stream.ReadAsync(singleByte, _cts.Token).ConfigureAwait(false);
                        if (r == 0) return;
                        window = (ushort)((window << 8) | singleByte[0]);
                    }

                    if (_cts.Token.IsCancellationRequested) return;

                    // We found magic marker (2 bytes), read the remaining 14 bytes of header
                    headerBuffer[0] = (byte)(BinaryFrame.MagicMarker >> 8);
                    headerBuffer[1] = (byte)(BinaryFrame.MagicMarker & 0xFF);
                    await _stream.ReadExactlyAsync(headerBuffer.AsMemory(2, 14), _cts.Token).ConfigureAwait(false);
                    RecordBytesTransferred(14);
                }

                byte version = headerBuffer[2];
                FrameType type = (FrameType)headerBuffer[3];
                ulong sessionId = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(headerBuffer.AsSpan(4, 8));
                uint payloadLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.AsSpan(12, 4));

                if (payloadLength > BinaryFrame.MaxPayloadSize)
                {
                    Console.Error.WriteLine($"[TcpTransport] Payload length {payloadLength} exceeds maximum {BinaryFrame.MaxPayloadSize}");
                    break;
                }

                // 2. Read payload
                byte[] payload = payloadLength > 0 ? GC.AllocateUninitializedArray<byte>((int)payloadLength) : [];
                if (payloadLength > 0)
                {
                    await _stream.ReadExactlyAsync(payload, _cts.Token).ConfigureAwait(false);
                    RecordBytesTransferred((int)payloadLength);
                }

                // 3. Read 4-byte CRC32 checksum
                await _stream.ReadExactlyAsync(checksumBuffer, _cts.Token).ConfigureAwait(false);
                RecordBytesTransferred(BinaryFrame.ChecksumSize);

                uint expectedCrc = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(checksumBuffer);

                // 4. Verify CRC32 over header + payload
                var hasher = new System.IO.Hashing.Crc32();
                hasher.Append(headerBuffer);
                if (payload.Length > 0)
                {
                    hasher.Append(payload);
                }
                uint computedCrc = hasher.GetCurrentHashAsUInt32();

                if (expectedCrc != computedCrc)
                {
                    Console.Error.WriteLine($"[TcpTransport] Checksum mismatch: expected 0x{expectedCrc:X8}, calculated 0x{computedCrc:X8}. Frame dropped.");
                    continue;
                }

                // 5. Construct valid frame and dispatch
                var frame = new BinaryFrame
                {
                    Version = version,
                    Type = type,
                    SessionId = sessionId,
                    Payload = payload
                };

                if (FrameReceived != null)
                {
                    try
                    {
                        await FrameReceived.Invoke(this, frame).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[TcpTransport] Frame handler exception: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException)
        {
            // Socket disconnected gracefully
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TcpTransport] Socket read exception: {ex.Message}");
        }
        finally
        {
            _ = DisposeAsync();
        }
    }

    private void RecordBytesTransferred(int byteCount)
    {
        Interlocked.Add(ref _bytesTransferredWindow, byteCount);
        if (_speedStopwatch.ElapsedMilliseconds >= 500)
        {
            long bytes = Interlocked.Exchange(ref _bytesTransferredWindow, 0);
            double seconds = _speedStopwatch.Elapsed.TotalSeconds;
            _speedStopwatch.Restart();
            _calculatedSpeed = bytes / seconds;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _cts.Cancel();
            _stream.Dispose();
            _socket.Dispose();
            _sendLock.Dispose();
            _cts.Dispose();
        }
        catch { }

        Disconnected?.Invoke(this);
        await Task.CompletedTask;
    }
}

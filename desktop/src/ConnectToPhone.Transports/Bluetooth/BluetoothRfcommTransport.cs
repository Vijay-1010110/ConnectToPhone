using System.Buffers;
using System.Diagnostics;
using ConnectToPhone.Core.Protocol;
using InTheHand.Net.Sockets;

namespace ConnectToPhone.Transports.Bluetooth;

public sealed class BluetoothRfcommTransport : ITransport
{
    private readonly BluetoothClient _client;
    private readonly Stream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _readLoopTask;
    private long _bytesTransferredWindow;
    private readonly Stopwatch _speedStopwatch = Stopwatch.StartNew();
    private double _calculatedSpeed;
    private bool _isDisposed;

    public string ChannelId { get; }
    public TransportType Type => TransportType.BluetoothRfcomm;
    public bool IsConnected => !_isDisposed && _client.Connected;
    public double CurrentSpeedBytesPerSec => _calculatedSpeed;

    public event Func<ITransport, BinaryFrame, Task>? FrameReceived;
    public event Action<ITransport>? Disconnected;

    public BluetoothRfcommTransport(BluetoothClient client, string? channelId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _stream = client.GetStream();
        ChannelId = channelId ?? $"bt_{Guid.NewGuid():N}";
    }

    public void StartReceiving()
    {
        _readLoopTask = Task.Run(ReceiveLoopAsync);
    }

    public async Task SendFrameAsync(BinaryFrame frame, CancellationToken ct = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Bluetooth transport is disconnected.");

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
                // 1. Read fixed header
                await _stream.ReadExactlyAsync(headerBuffer, _cts.Token).ConfigureAwait(false);
                RecordBytesTransferred(BinaryFrame.HeaderSize);

                ushort magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(headerBuffer.AsSpan(0, 2));
                if (magic != BinaryFrame.MagicMarker)
                {
                    byte[] singleByte = new byte[1];
                    ushort window = magic;
                    while (!_cts.Token.IsCancellationRequested && window != BinaryFrame.MagicMarker)
                    {
                        int r = await _stream.ReadAsync(singleByte, _cts.Token).ConfigureAwait(false);
                        if (r == 0) return;
                        window = (ushort)((window << 8) | singleByte[0]);
                    }

                    if (_cts.Token.IsCancellationRequested) return;

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
                    break;

                // 2. Read payload
                byte[] payload = payloadLength > 0 ? GC.AllocateUninitializedArray<byte>((int)payloadLength) : [];
                if (payloadLength > 0)
                {
                    await _stream.ReadExactlyAsync(payload, _cts.Token).ConfigureAwait(false);
                    RecordBytesTransferred((int)payloadLength);
                }

                // 3. Read CRC32
                await _stream.ReadExactlyAsync(checksumBuffer, _cts.Token).ConfigureAwait(false);
                RecordBytesTransferred(BinaryFrame.ChecksumSize);

                uint expectedCrc = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(checksumBuffer);

                // 4. Verify CRC32
                var hasher = new System.IO.Hashing.Crc32();
                hasher.Append(headerBuffer);
                if (payload.Length > 0)
                {
                    hasher.Append(payload);
                }
                uint computedCrc = hasher.GetCurrentHashAsUInt32();

                if (expectedCrc != computedCrc)
                    continue;

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
                        Debug.WriteLine($"[BtTransport] Frame error: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BtTransport] Read exception: {ex.Message}");
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
            _client.Dispose();
            _sendLock.Dispose();
            _cts.Dispose();
        }
        catch { }

        Disconnected?.Invoke(this);
        await Task.CompletedTask;
    }
}

using System.Collections;
using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Security.Cryptography;
using ConnectToPhone.Core.Protocol.Models;
using Microsoft.Win32.SafeHandles;

namespace ConnectToPhone.Core.Engine;

/// <summary>
/// Manages chunk partitioning, multi-channel distribution, and resilient failover.
/// </summary>
public sealed class ChunkScheduler : IDisposable
{
    private readonly string _sourceFilePath;
    private readonly SafeFileHandle _fileHandle;
    private readonly TransferManifest _manifest;
    private readonly BitArray _ackedChunks;
    private readonly ConcurrentQueue<uint> _pendingChunkIndices = new();
    private readonly ConcurrentDictionary<uint, (string channelId, DateTime sentTime)> _inFlightChunks = new();
    private readonly object _lock = new();
    private int _ackedCount;
    private bool _isDisposed;

    public TransferManifest Manifest => _manifest;
    public ulong TransferId => _manifest.TransferId;
    public int TotalChunks => _manifest.TotalChunks;
    public int AckedCount => _ackedCount;
    public bool IsComplete => _ackedCount == TotalChunks;

    public ChunkScheduler(string filePath, int chunkSize = 2 * 1024 * 1024)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Source file does not exist", filePath);

        _sourceFilePath = filePath;
        FileInfo info = new(filePath);
        long fileSize = info.Length;
        int totalChunks = (int)((fileSize + chunkSize - 1) / chunkSize);
        if (totalChunks == 0) totalChunks = 1;

        // Compute whole file hash
        using var fs = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(fs);

        _manifest = new TransferManifest
        {
            TransferId = (ulong)Random.Shared.NextInt64(),
            FileName = info.Name,
            RelativePath = info.Name,
            TotalBytes = fileSize,
            ChunkSize = chunkSize,
            TotalChunks = totalChunks,
            WholeFileHashHex = Convert.ToHexString(hash).ToLowerInvariant(),
            ModifiedTimestamp = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds()
        };

        _ackedChunks = new BitArray(totalChunks, false);
        for (uint i = 0; i < (uint)totalChunks; i++)
        {
            _pendingChunkIndices.Enqueue(i);
        }

        _fileHandle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
    }

    /// <summary>
    /// Gets the next chunk ready to be transmitted over a specific channel.
    /// </summary>
    public bool TryGetNextChunk(string channelId, out TransferChunk? chunk)
    {
        chunk = null;
        if (!_pendingChunkIndices.TryDequeue(out uint chunkIndex))
            return false;

        long offset = (long)chunkIndex * _manifest.ChunkSize;
        int bytesToRead = (int)Math.Min((long)_manifest.ChunkSize, _manifest.TotalBytes - offset);
        if (bytesToRead < 0) bytesToRead = 0;

        byte[] payload = new byte[bytesToRead];
        if (bytesToRead > 0)
        {
            RandomAccess.Read(_fileHandle, payload, offset);
        }

        ulong chunkHash = XxHash64.HashToUInt64(payload);

        chunk = new TransferChunk
        {
            TransferId = _manifest.TransferId,
            ChunkIndex = chunkIndex,
            ChunkHash = chunkHash,
            Payload = payload
        };

        _inFlightChunks[chunkIndex] = (channelId, DateTime.UtcNow);
        return true;
    }

    /// <summary>
    /// Records acknowledgment from the receiver for a verified chunk.
    /// </summary>
    public void AcknowledgeChunk(uint chunkIndex)
    {
        _inFlightChunks.TryRemove(chunkIndex, out _);

        lock (_lock)
        {
            if (chunkIndex < (uint)_ackedChunks.Length && !_ackedChunks[(int)chunkIndex])
            {
                _ackedChunks[(int)chunkIndex] = true;
                _ackedCount++;
            }
        }
    }

    /// <summary>
    /// Fails over chunks assigned to a channel that dropped (e.g. USB unplugged).
    /// Puts them right back in the queue for surviving channels (e.g. Wi-Fi).
    /// </summary>
    public void HandleChannelDisconnect(string failedChannelId)
    {
        foreach (var (idx, (channelId, _)) in _inFlightChunks)
        {
            if (channelId == failedChannelId)
            {
                if (_inFlightChunks.TryRemove(idx, out _))
                {
                    _pendingChunkIndices.Enqueue(idx);
                }
            }
        }
    }

    /// <summary>
    /// Checks for chunks that were transmitted but unacknowledged within the timeout.
    /// Requeues them for retransmission to ensure 100% complete delivery under packet drops or delay.
    /// </summary>
    public int RequeueTimedOutChunks(TimeSpan timeout)
    {
        int requeued = 0;
        DateTime cutoff = DateTime.UtcNow - timeout;
        foreach (var (idx, (channelId, sentTime)) in _inFlightChunks)
        {
            if (sentTime < cutoff)
            {
                if (_inFlightChunks.TryRemove(idx, out _))
                {
                    _pendingChunkIndices.Enqueue(idx);
                    requeued++;
                }
            }
        }
        return requeued;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _fileHandle.Dispose();
        }
    }
}

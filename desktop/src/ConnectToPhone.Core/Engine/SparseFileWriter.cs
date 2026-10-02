using System.Collections;
using System.IO.Hashing;
using System.Security.Cryptography;
using ConnectToPhone.Core.Protocol.Models;
using Microsoft.Win32.SafeHandles;

namespace ConnectToPhone.Core.Engine;

/// <summary>
/// Writes incoming out-of-order verified chunks directly into the filesystem
/// using high-performance non-blocking random access writes.
/// </summary>
public sealed class SparseFileWriter : IDisposable
{
    private readonly TransferManifest _manifest;
    private readonly string _finalDestinationPath;
    private readonly string _tempPartPath;
    private readonly SafeFileHandle _fileHandle;
    private readonly BitArray _completedChunks;
    private readonly object _lock = new();
    private int _verifiedChunkCount;
    private bool _isDisposed;

    public ulong TransferId => _manifest.TransferId;
    public string FileName => _manifest.FileName;
    public long TotalBytes => _manifest.TotalBytes;
    public int TotalChunks => _manifest.TotalChunks;
    public int VerifiedChunkCount => _verifiedChunkCount;
    public bool IsComplete => _verifiedChunkCount == TotalChunks;
    public TransferManifest Manifest => _manifest;
    public string FinalDestinationPath => _finalDestinationPath;

    public SparseFileWriter(TransferManifest manifest, string destinationDirectory)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Directory.CreateDirectory(destinationDirectory);

        _finalDestinationPath = Path.Combine(destinationDirectory, manifest.FileName);
        _tempPartPath = _finalDestinationPath + ".c2p_part";

        _completedChunks = new BitArray(manifest.TotalChunks, false);

        _fileHandle = File.OpenHandle(
            _tempPartPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite,
            FileOptions.Asynchronous | FileOptions.RandomAccess
        );
        RandomAccess.SetLength(_fileHandle, manifest.TotalBytes);
    }

    /// <summary>
    /// Writes an incoming chunk directly to its deterministic byte offset.
    /// Verifies the chunk's integrity hash before writing.
    /// </summary>
    public bool WriteChunk(TransferChunk chunk, out string? errorMessage)
    {
        errorMessage = null;

        if (chunk.ChunkIndex >= (uint)TotalChunks)
        {
            errorMessage = $"Chunk index {chunk.ChunkIndex} exceeds total chunks {TotalChunks}.";
            return false;
        }

        // 1. Verify chunk hash
        ulong calculatedHash = XxHash64.HashToUInt64(chunk.Payload);
        if (chunk.ChunkHash != 0 && chunk.ChunkHash != calculatedHash)
        {
            errorMessage = $"Hash mismatch on chunk {chunk.ChunkIndex}. Expected {chunk.ChunkHash}, got {calculatedHash}.";
            return false;
        }

        lock (_lock)
        {
            if (_completedChunks[(int)chunk.ChunkIndex])
            {
                // Already received and written (redundant retransmission)
                return true;
            }

            long offset = (long)chunk.ChunkIndex * _manifest.ChunkSize;
            RandomAccess.Write(_fileHandle, chunk.Payload, offset);

            _completedChunks[(int)chunk.ChunkIndex] = true;
            _verifiedChunkCount++;
        }

        return true;
    }

    /// <summary>
    /// Gets a compact bitset indicating which chunks have been received.
    /// Used during transfer resumption.
    /// </summary>
    public byte[] GetReceivedBitset()
    {
        lock (_lock)
        {
            int numBytes = (_completedChunks.Length + 7) / 8;
            byte[] bytes = new byte[numBytes];
            _completedChunks.CopyTo(bytes, 0);
            return bytes;
        }
    }

    /// <summary>
    /// Finalizes the file after all chunks have been received and verified.
    /// Validates whole-file SHA-256 and renames the .c2p_part file to the final destination.
    /// </summary>
    public bool FinalizeFile(out string? error)
    {
        error = null;
        if (!IsComplete)
        {
            error = $"Cannot finalize: only {_verifiedChunkCount}/{TotalChunks} chunks received.";
            return false;
        }

        try
        {
            _fileHandle.Dispose();

            // Validate whole file hash if provided
            if (!string.IsNullOrEmpty(_manifest.WholeFileHashHex))
            {
                using var fs = File.OpenRead(_tempPartPath);
                byte[] hashBytes = SHA256.HashData(fs);
                string computedHashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

                if (!string.Equals(computedHashHex, _manifest.WholeFileHashHex, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Whole file hash validation failed. Expected: {_manifest.WholeFileHashHex}, Computed: {computedHashHex}";
                    return false;
                }
            }

            // Atomically move to final destination
            if (File.Exists(_finalDestinationPath))
            {
                File.Delete(_finalDestinationPath);
            }
            File.Move(_tempPartPath, _finalDestinationPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
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

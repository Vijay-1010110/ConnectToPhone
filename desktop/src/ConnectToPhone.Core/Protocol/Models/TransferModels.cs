using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConnectToPhone.Core.Protocol.Models;

public sealed class TransferManifest
{
    [JsonPropertyName("transferId")]
    public ulong TransferId { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("chunkSize")]
    public int ChunkSize { get; set; } = 2 * 1024 * 1024; // 2 MB default

    [JsonPropertyName("totalChunks")]
    public int TotalChunks { get; set; }

    [JsonPropertyName("wholeFileHashHex")]
    public string WholeFileHashHex { get; set; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "application/octet-stream";

    [JsonPropertyName("modifiedTimestamp")]
    public long ModifiedTimestamp { get; set; }

    [JsonPropertyName("isPreview")]
    public bool IsPreview { get; set; }

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static TransferManifest? FromUtf8Bytes(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<TransferManifest>(bytes);
}

/// <summary>
/// High-speed binary chunk container.
/// Packed layout:
/// [TransferId: 8B][ChunkIndex: 4B][DataLength: 4B][ChunkHash: 8B][ChunkData: N B]
/// Header = 24 bytes.
/// </summary>
public sealed class TransferChunk
{
    public const int HeaderSize = 24;

    public ulong TransferId { get; set; }
    public uint ChunkIndex { get; set; }
    public ulong ChunkHash { get; set; }
    public byte[] Payload { get; set; } = [];

    public byte[] Serialize()
    {
        byte[] buffer = new byte[HeaderSize + Payload.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(0, 8), TransferId);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), ChunkIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, 4), (uint)Payload.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(16, 8), ChunkHash);
        Payload.CopyTo(buffer.AsSpan(HeaderSize));
        return buffer;
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out TransferChunk? chunk)
    {
        chunk = null;
        if (span.Length < HeaderSize)
            return false;

        ulong transferId = BinaryPrimitives.ReadUInt64LittleEndian(span[..8]);
        uint chunkIndex = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4));
        uint dataLen = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4));
        ulong chunkHash = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(16, 8));

        if (span.Length < HeaderSize + (int)dataLen)
            return false;

        byte[] payload = span.Slice(HeaderSize, (int)dataLen).ToArray();

        chunk = new TransferChunk
        {
            TransferId = transferId,
            ChunkIndex = chunkIndex,
            ChunkHash = chunkHash,
            Payload = payload
        };
        return true;
    }
}

public sealed class TransferChunkAck
{
    public ulong TransferId { get; set; }
    public uint ChunkIndex { get; set; }
    public bool Success { get; set; }

    public byte[] Serialize()
    {
        byte[] buffer = new byte[13];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(0, 8), TransferId);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), ChunkIndex);
        buffer[12] = (byte)(Success ? 1 : 0);
        return buffer;
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out TransferChunkAck? ack)
    {
        ack = null;
        if (span.Length < 13)
            return false;

        ack = new TransferChunkAck
        {
            TransferId = BinaryPrimitives.ReadUInt64LittleEndian(span[..8]),
            ChunkIndex = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4)),
            Success = span[12] == 1
        };
        return true;
    }
}

public sealed class TransferProgressStats
{
    public ulong TransferId { get; set; }
    public long BytesTransferred { get; set; }
    public long TotalBytes { get; set; }
    public double SpeedBytesPerSecond { get; set; }
    public TransportType ActiveTransports { get; set; }
    public int EtaSeconds { get; set; }
    public double ProgressPercentage => TotalBytes > 0 ? (double)BytesTransferred / TotalBytes * 100.0 : 0.0;
}

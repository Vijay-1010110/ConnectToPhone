using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConnectToPhone.Core.Protocol.Models;

public sealed class FsEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("entryType")]
    public FsEntryType EntryType { get; set; }

    [JsonPropertyName("category")]
    public FileCategory Category { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("modifiedTimestamp")]
    public long ModifiedTimestamp { get; set; }

    [JsonPropertyName("isHidden")]
    public bool IsHidden { get; set; }
}

public sealed class FsListDirRequest
{
    [JsonPropertyName("targetPath")]
    public string TargetPath { get; set; } = string.Empty;

    [JsonPropertyName("includeHidden")]
    public bool IncludeHidden { get; set; }

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static FsListDirRequest? FromUtf8Bytes(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<FsListDirRequest>(bytes);
}

public sealed class FsListDirResponse
{
    [JsonPropertyName("currentPath")]
    public string CurrentPath { get; set; } = string.Empty;

    [JsonPropertyName("entries")]
    public List<FsEntry> Entries { get; set; } = [];

    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static FsListDirResponse? FromUtf8Bytes(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<FsListDirResponse>(bytes);
}

public sealed class FsPullFileRequest
{
    [JsonPropertyName("remoteFilePath")]
    public string RemoteFilePath { get; set; } = string.Empty;

    [JsonPropertyName("startOffset")]
    public long StartOffset { get; set; }

    [JsonPropertyName("isPreview")]
    public bool IsPreview { get; set; }

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static FsPullFileRequest? FromUtf8Bytes(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<FsPullFileRequest>(bytes);
}

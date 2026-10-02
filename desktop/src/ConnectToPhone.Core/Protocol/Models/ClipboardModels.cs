using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConnectToPhone.Core.Protocol.Models;

public sealed class ClipboardPayload
{
    [JsonPropertyName("format")]
    public ClipboardFormat Format { get; set; } = ClipboardFormat.PlainText;

    [JsonPropertyName("contentHash")]
    public ulong ContentHash { get; set; }

    [JsonPropertyName("timestamp")]
    public long TimestampMillis { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonPropertyName("text")]
    public string? TextContent { get; set; }

    [JsonPropertyName("imageBase64")]
    public string? ImageBase64 { get; set; }

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "text/plain";

    [JsonPropertyName("sourceDeviceId")]
    public string SourceDeviceId { get; set; } = string.Empty;

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static ClipboardPayload? FromUtf8Bytes(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<ClipboardPayload>(bytes);
}

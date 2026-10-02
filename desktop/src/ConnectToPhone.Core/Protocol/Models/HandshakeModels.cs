using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConnectToPhone.Core.Protocol.Models;

public sealed class HandshakeSyn
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    [JsonPropertyName("deviceType")]
    public DeviceType DeviceType { get; set; }

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "1.0.0";

    [JsonPropertyName("supportedTransports")]
    public TransportType SupportedTransports { get; set; }

    [JsonPropertyName("publicKeyHex")]
    public string PublicKeyHex { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long TimestampMillis { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static HandshakeSyn? FromUtf8Bytes(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<HandshakeSyn>(bytes);
}

public sealed class HandshakeAck
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; }

    [JsonPropertyName("sessionId")]
    public ulong SessionId { get; set; }

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    [JsonPropertyName("publicKeyHex")]
    public string PublicKeyHex { get; set; } = string.Empty;

    [JsonPropertyName("pairingPin")]
    public string PairingPin { get; set; } = string.Empty;

    [JsonPropertyName("rejectionReason")]
    public string? RejectionReason { get; set; }

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static HandshakeAck? FromUtf8Bytes(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<HandshakeAck>(bytes);
}

public sealed class HeartbeatPong
{
    [JsonPropertyName("echoTimestamp")]
    public long EchoTimestampMillis { get; set; }

    [JsonPropertyName("batteryPercentage")]
    public int BatteryPercentage { get; set; }

    [JsonPropertyName("isCharging")]
    public bool IsCharging { get; set; }

    [JsonPropertyName("activeNetwork")]
    public string ActiveNetwork { get; set; } = string.Empty;

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static HeartbeatPong? FromUtf8Bytes(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<HeartbeatPong>(bytes);
}

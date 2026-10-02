using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConnectToPhone.Core.Protocol.Models;

public enum MediaAction : byte
{
    PlayPause = 0,
    NextTrack = 1,
    PrevTrack = 2,
    VolumeUp = 3,
    VolumeDown = 4,
    MuteToggle = 5
}

public enum PowerAction : byte
{
    Lock = 0,
    Sleep = 1,
    Restart = 2,
    Shutdown = 3
}

public sealed class MouseControlPayload
{
    [JsonPropertyName("dx")]
    public int DeltaX { get; set; }

    [JsonPropertyName("dy")]
    public int DeltaY { get; set; }

    [JsonPropertyName("leftClick")]
    public bool LeftClick { get; set; }

    [JsonPropertyName("rightClick")]
    public bool RightClick { get; set; }

    [JsonPropertyName("middleClick")]
    public bool MiddleClick { get; set; }

    [JsonPropertyName("leftDown")]
    public bool LeftButtonDown { get; set; }

    [JsonPropertyName("rightDown")]
    public bool RightButtonDown { get; set; }

    [JsonPropertyName("wheelDelta")]
    public int WheelDelta { get; set; }

    [JsonPropertyName("wheelDeltaX")]
    public int WheelDeltaX { get; set; }

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static MouseControlPayload? FromUtf8Bytes(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<MouseControlPayload>(bytes);
}

public sealed class KeyboardControlPayload
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("virtualKey")]
    public int VirtualKey { get; set; }

    [JsonPropertyName("modifiers")]
    public int Modifiers { get; set; }

    [JsonPropertyName("specialKey")]
    public string? SpecialKey { get; set; }

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static KeyboardControlPayload? FromUtf8Bytes(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<KeyboardControlPayload>(bytes);
}

public sealed class MediaControlPayload
{
    [JsonPropertyName("action")]
    public MediaAction Action { get; set; }

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static MediaControlPayload? FromUtf8Bytes(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<MediaControlPayload>(bytes);
}

public sealed class PowerControlPayload
{
    [JsonPropertyName("action")]
    public PowerAction Action { get; set; }

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static PowerControlPayload? FromUtf8Bytes(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<PowerControlPayload>(bytes);
}

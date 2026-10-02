using System.Buffers.Binary;
using System.IO.Hashing;

namespace ConnectToPhone.Core.Protocol;

/// <summary>
/// Represents a raw transport-agnostic binary frame.
/// Fixed 16-byte header + N payload bytes + 4-byte CRC32-C.
/// </summary>
public sealed class BinaryFrame
{
    public const ushort MagicMarker = 0xCAFE;
    public const byte CurrentVersion = 0x01;
    public const int HeaderSize = 16;
    public const int ChecksumSize = 4;
    public const int MinFrameSize = HeaderSize + ChecksumSize; // 20 bytes
    public const int MaxPayloadSize = 16 * 1024 * 1024; // 16 MB limit per frame

    public byte Version { get; set; } = CurrentVersion;
    public FrameType Type { get; set; }
    public ulong SessionId { get; set; }
    public byte[] Payload { get; set; } = [];

    public int TotalWireLength => HeaderSize + Payload.Length + ChecksumSize;

    public BinaryFrame() { }

    public BinaryFrame(FrameType type, ulong sessionId, byte[] payload)
    {
        Type = type;
        SessionId = sessionId;
        Payload = payload ?? [];
    }

    /// <summary>
    /// Serializes this frame into a byte array ready for transmission across TCP, USB, or Bluetooth.
    /// </summary>
    public byte[] Serialize()
    {
        byte[] buffer = new byte[TotalWireLength];
        SerializeInto(buffer);
        return buffer;
    }

    /// <summary>
    /// Serializes this frame directly into a provided Span.
    /// </summary>
    public void SerializeInto(Span<byte> destination)
    {
        if (destination.Length < TotalWireLength)
            throw new ArgumentException("Destination span is too small for serialized frame.", nameof(destination));

        // 1. Header (16 bytes)
        BinaryPrimitives.WriteUInt16BigEndian(destination[..2], MagicMarker);
        destination[2] = Version;
        destination[3] = (byte)Type;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[4..12], SessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..16], (uint)Payload.Length);

        // 2. Payload
        if (Payload.Length > 0)
        {
            Payload.AsSpan().CopyTo(destination.Slice(HeaderSize, Payload.Length));
        }

        // 3. Compute CRC32 over Header + Payload
        int dataToHashLen = HeaderSize + Payload.Length;
        uint crc = Crc32.HashToUInt32(destination[..dataToHashLen]);

        // 4. Append CRC32
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(dataToHashLen, ChecksumSize), crc);
    }

    /// <summary>
    /// Attempts to parse a BinaryFrame from a ReadOnlySpan of bytes.
    /// Returns true if successful and outputs the frame and bytes consumed.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out BinaryFrame? frame, out int bytesConsumed)
    {
        frame = null;
        bytesConsumed = 0;

        if (data.Length < MinFrameSize)
            return false;

        ushort magic = BinaryPrimitives.ReadUInt16BigEndian(data[..2]);
        if (magic != MagicMarker)
            return false;

        byte version = data[2];
        FrameType type = (FrameType)data[3];
        ulong sessionId = BinaryPrimitives.ReadUInt64LittleEndian(data[4..12]);
        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(data[12..16]);

        if (payloadLength > MaxPayloadSize)
            return false;

        int totalExpectedSize = HeaderSize + (int)payloadLength + ChecksumSize;
        if (data.Length < totalExpectedSize)
            return false; // Wait for more data in buffer

        // Verify CRC32
        int dataToHashLen = HeaderSize + (int)payloadLength;
        uint expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(dataToHashLen, ChecksumSize));
        uint calculatedCrc = Crc32.HashToUInt32(data[..dataToHashLen]);

        if (expectedCrc != calculatedCrc)
            return false; // Checksum corrupted

        byte[] payload = payloadLength > 0 
            ? data.Slice(HeaderSize, (int)payloadLength).ToArray() 
            : [];

        frame = new BinaryFrame
        {
            Version = version,
            Type = type,
            SessionId = sessionId,
            Payload = payload
        };

        bytesConsumed = totalExpectedSize;
        return true;
    }
}

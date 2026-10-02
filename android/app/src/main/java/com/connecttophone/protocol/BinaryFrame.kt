package com.connecttophone.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.zip.CRC32

/**
 * Transport-agnostic binary frame serialization and parsing for Android.
 * Fixed 16-byte header + N payload bytes + 4-byte CRC32.
 */
class BinaryFrame(
    var version: Byte = CURRENT_VERSION,
    var type: FrameType = FrameType.UNKNOWN,
    var sessionId: Long = 0L,
    var payload: ByteArray = ByteArray(0)
) {
    companion object {
        const val MAGIC_MARKER: Short = 0xCAFE.toShort()
        const val CURRENT_VERSION: Byte = 0x01
        const val HEADER_SIZE = 16
        const val CHECKSUM_SIZE = 4
        const val MIN_FRAME_SIZE = HEADER_SIZE + CHECKSUM_SIZE
        const val MAX_PAYLOAD_SIZE = 16 * 1024 * 1024 // 16 MB

        /**
         * Attempts to parse a BinaryFrame from a byte array.
         */
        fun tryParse(data: ByteArray, offset: Int = 0, length: Int = data.size): ParseResult {
            if (length < MIN_FRAME_SIZE) {
                return ParseResult.NeedMoreData
            }

            val buf = ByteBuffer.wrap(data, offset, length)
            buf.order(ByteOrder.BIG_ENDIAN)

            val magic = buf.short
            if (magic != MAGIC_MARKER) {
                return ParseResult.InvalidMagic
            }

            val version = buf.get()
            val typeByte = buf.get()
            val type = FrameType.fromByte(typeByte)

            buf.order(ByteOrder.LITTLE_ENDIAN)
            val sessionId = buf.long
            val payloadLength = buf.int

            if (payloadLength < 0 || payloadLength > MAX_PAYLOAD_SIZE) {
                return ParseResult.Corrupted("Payload length $payloadLength is out of bounds.")
            }

            val totalExpected = HEADER_SIZE + payloadLength + CHECKSUM_SIZE
            if (length < totalExpected) {
                return ParseResult.NeedMoreData
            }

            // Verify CRC32
            val crc = CRC32()
            crc.update(data, offset, HEADER_SIZE + payloadLength)
            val expectedCrc = (crc.value and 0xFFFFFFFFL).toInt()

            buf.position(offset + HEADER_SIZE + payloadLength)
            val actualCrc = buf.int

            if (expectedCrc != actualCrc) {
                return ParseResult.Corrupted("CRC32 mismatch: expected $expectedCrc, got $actualCrc")
            }

            val payload = ByteArray(payloadLength)
            System.arraycopy(data, offset + HEADER_SIZE, payload, 0, payloadLength)

            val frame = BinaryFrame(
                version = version,
                type = type,
                sessionId = sessionId,
                payload = payload
            )
            return ParseResult.Success(frame, totalExpected)
        }
    }

    val totalWireLength: Int
        get() = HEADER_SIZE + payload.size + CHECKSUM_SIZE

    /**
     * Serializes this frame into a ByteArray ready for TCP, USB, or Bluetooth transmission.
     */
    fun serialize(): ByteArray {
        val bytes = ByteArray(totalWireLength)
        val buf = ByteBuffer.wrap(bytes)

        // 1. Header (Big-endian magic, little-endian sessionId & payloadLen)
        buf.order(ByteOrder.BIG_ENDIAN)
        buf.putShort(MAGIC_MARKER)
        buf.put(version)
        buf.put(type.value)

        buf.order(ByteOrder.LITTLE_ENDIAN)
        buf.putLong(sessionId)
        buf.putInt(payload.size)

        // 2. Payload
        if (payload.isNotEmpty()) {
            buf.put(payload)
        }

        // 3. Compute CRC32
        val crc = CRC32()
        crc.update(bytes, 0, HEADER_SIZE + payload.size)
        val crcValue = (crc.value and 0xFFFFFFFFL).toInt()

        // 4. Append CRC32
        buf.putInt(crcValue)

        return bytes
    }

    sealed class ParseResult {
        data class Success(val frame: BinaryFrame, val bytesConsumed: Int) : ParseResult()
        object NeedMoreData : ParseResult()
        object InvalidMagic : ParseResult()
        data class Corrupted(val reason: String) : ParseResult()
    }
}

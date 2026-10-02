package com.connecttophone.transport

import android.bluetooth.BluetoothSocket
import android.util.Log
import com.connecttophone.protocol.BinaryFrame
import com.connecttophone.protocol.FrameType
import com.connecttophone.protocol.TransportType
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.io.DataInputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.UUID
import java.util.concurrent.atomic.AtomicLong
import java.util.zip.CRC32

class BluetoothRfcommTransport(
    private val socket: BluetoothSocket,
    override val channelId: String = "bt_${UUID.randomUUID()}"
) : ITransport {

    override val type: Long = TransportType.BLUETOOTH_RFCOMM

    private val dataIn = DataInputStream(socket.inputStream)
    private val outputStream: OutputStream = socket.outputStream
    private val writeMutex = Mutex()
    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())

    private val bytesInWindow = AtomicLong(0)
    private var lastSpeedCheckTime = System.currentTimeMillis()
    private var calculatedSpeed: Double = 0.0
    private var isDisposed = false
    private var isReceiving = false

    override val isConnected: Boolean get() = !isDisposed && socket.isConnected
    override val currentSpeedBytesPerSec: Double get() = calculatedSpeed

    override var onFrameReceived: ((ITransport, BinaryFrame) -> Unit)? = null
    override var onDisconnected: ((ITransport) -> Unit)? = null

    override fun startReceiving() {
        if (isReceiving || isDisposed) return
        isReceiving = true
        scope.launch {
            receiveLoop()
        }
    }

    override suspend fun sendFrame(frame: BinaryFrame) {
        if (!isConnected) return

        val bytes = frame.serialize()
        try {
            writeMutex.withLock {
                withContext(Dispatchers.IO) {
                    outputStream.write(bytes)
                    outputStream.flush()
                }
                recordBytes(bytes.size)
            }
        } catch (e: Exception) {
            close()
        }
    }

    private suspend fun receiveLoop() = withContext(Dispatchers.IO) {
        val headerBuffer = ByteArray(BinaryFrame.HEADER_SIZE)
        val checksumBuffer = ByteArray(BinaryFrame.CHECKSUM_SIZE)

        try {
            while (isActive && isConnected) {
                // 1. Read 16-byte fixed header
                dataIn.readFully(headerBuffer)
                recordBytes(BinaryFrame.HEADER_SIZE)

                val magic = ((headerBuffer[0].toInt() and 0xFF) shl 8) or (headerBuffer[1].toInt() and 0xFF)
                if (magic != (BinaryFrame.MAGIC_MARKER.toInt() and 0xFFFF)) {
                    var window = magic
                    val targetMagic = BinaryFrame.MAGIC_MARKER.toInt() and 0xFFFF
                    while (isActive && window != targetMagic) {
                        val b = dataIn.read()
                        if (b == -1) return@withContext
                        window = ((window shl 8) and 0xFFFF) or (b and 0xFF)
                    }
                    headerBuffer[0] = ((targetMagic shr 8) and 0xFF).toByte()
                    headerBuffer[1] = (targetMagic and 0xFF).toByte()
                    dataIn.readFully(headerBuffer, 2, 14)
                    recordBytes(14)
                }

                val version = headerBuffer[2]
                val type = FrameType.fromByte(headerBuffer[3])

                val headerBb = ByteBuffer.wrap(headerBuffer).order(ByteOrder.LITTLE_ENDIAN)
                headerBb.position(4)
                val sessionId = headerBb.long
                val payloadLength = headerBb.int

                if (payloadLength < 0 || payloadLength > BinaryFrame.MAX_PAYLOAD_SIZE) {
                    Log.e("ConnectToPhone", "[Bluetooth] Payload length $payloadLength out of bounds")
                    break
                }

                // 2. Read payload
                val payload = if (payloadLength > 0) ByteArray(payloadLength) else ByteArray(0)
                if (payloadLength > 0) {
                    dataIn.readFully(payload)
                    recordBytes(payloadLength)
                }

                // 3. Read checksum
                dataIn.readFully(checksumBuffer)
                recordBytes(BinaryFrame.CHECKSUM_SIZE)

                val csBb = ByteBuffer.wrap(checksumBuffer).order(ByteOrder.LITTLE_ENDIAN)
                val expectedCrc = csBb.int

                // 4. Verify CRC32
                val crc = CRC32()
                crc.update(headerBuffer)
                if (payload.isNotEmpty()) {
                    crc.update(payload)
                }
                val computedCrc = (crc.value and 0xFFFFFFFFL).toInt()

                if (expectedCrc != computedCrc) {
                    Log.w("ConnectToPhone", "[Bluetooth] CRC mismatch")
                    continue
                }

                // 5. Dispatch frame
                val frame = BinaryFrame(
                    version = version,
                    type = type,
                    sessionId = sessionId,
                    payload = payload
                )

                try {
                    onFrameReceived?.invoke(this@BluetoothRfcommTransport, frame)
                } catch (e: Exception) {
                    Log.e("ConnectToPhone", "[Bluetooth] Error handling frame: ${e.message}", e)
                }
            }
        } catch (_: Exception) {
        } finally {
            close()
        }
    }

    private fun recordBytes(count: Int) {
        bytesInWindow.addAndGet(count.toLong())
        val now = System.currentTimeMillis()
        val elapsed = now - lastSpeedCheckTime
        if (elapsed >= 500) {
            val bytes = bytesInWindow.getAndSet(0)
            calculatedSpeed = (bytes.toDouble() / elapsed) * 1000.0
            lastSpeedCheckTime = now
        }
    }

    override fun close() {
        if (isDisposed) return
        isDisposed = true
        scope.cancel()
        try {
            socket.close()
        } catch (_: Exception) {}
        onDisconnected?.invoke(this)
    }
}

package com.connecttophone.engine

import com.connecttophone.protocol.TransferChunk
import com.connecttophone.protocol.TransferManifest
import java.io.File
import java.io.FileInputStream
import java.io.RandomAccessFile
import java.nio.ByteBuffer
import java.nio.channels.FileChannel
import java.security.MessageDigest
import java.util.BitSet
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.ConcurrentLinkedQueue
import java.util.zip.CRC32
import kotlin.random.Random

/**
 * Android ChunkScheduler: slices files into 2 MB chunks, tracks inflight state,
 * and redistributes chunks if a channel fails (e.g., USB unplugged mid-transfer).
 */
class ChunkScheduler(
    val file: File,
    val chunkSize: Int = 2 * 1024 * 1024
) : AutoCloseable {

    val manifest: TransferManifest
    private val randomAccessFile: RandomAccessFile
    private val fileChannel: FileChannel
    private val pendingIndices = ConcurrentLinkedQueue<Int>()
    private val inFlightChunks = ConcurrentHashMap<Int, Pair<String, Long>>()
    private val ackedBitSet: BitSet
    private var ackedCount = 0
    private val lock = Any()

    val totalChunks: Int get() = manifest.totalChunks
    val isComplete: Boolean get() = synchronized(lock) { ackedCount == totalChunks }

    init {
        require(file.exists()) { "File does not exist: ${file.absolutePath}" }

        val fileSize = file.length()
        val total = ((fileSize + chunkSize - 1) / chunkSize).toInt().coerceAtLeast(1)

        // Compute SHA-256
        val digest = MessageDigest.getInstance("SHA-256")
        FileInputStream(file).use { fis ->
            val buf = ByteArray(65536)
            var read: Int
            while (fis.read(buf).also { read = it } != -1) {
                digest.update(buf, 0, read)
            }
        }
        val fileHash = digest.digest().joinToString("") { "%02x".format(it) }

        manifest = TransferManifest(
            transferId = Random.nextLong(),
            fileName = file.name,
            relativePath = file.name,
            totalBytes = fileSize,
            chunkSize = chunkSize,
            totalChunks = total,
            wholeFileHashHex = fileHash,
            modifiedTimestamp = file.lastModified()
        )

        ackedBitSet = BitSet(total)
        for (i in 0 until total) {
            pendingIndices.add(i)
        }

        randomAccessFile = RandomAccessFile(file, "r")
        fileChannel = randomAccessFile.channel
    }

    /**
     * Retrieves the next available chunk for a specific transport channel.
     */
    fun tryGetNextChunk(channelId: String): TransferChunk? {
        val chunkIndex = pendingIndices.poll() ?: return null

        val offset = chunkIndex.toLong() * chunkSize
        val bytesToRead = Math.min(chunkSize.toLong(), manifest.totalBytes - offset).toInt().coerceAtLeast(0)

        val payload = ByteArray(bytesToRead)
        if (bytesToRead > 0) {
            val buf = ByteBuffer.wrap(payload)
            fileChannel.read(buf, offset)
        }

        val crc = CRC32()
        crc.update(payload)
        val hash = crc.value

        inFlightChunks[chunkIndex] = Pair(channelId, System.currentTimeMillis())
        return TransferChunk(manifest.transferId, chunkIndex, hash, payload)
    }

    /**
     * Marks a chunk as acknowledged by the receiver.
     */
    fun acknowledgeChunk(chunkIndex: Int) {
        inFlightChunks.remove(chunkIndex)
        synchronized(lock) {
            if (chunkIndex in 0 until totalChunks && !ackedBitSet.get(chunkIndex)) {
                ackedBitSet.set(chunkIndex, true)
                ackedCount++
            }
        }
    }

    /**
     * Resets any inflight chunks from a disconnected channel back to the pending queue.
     */
    fun handleChannelDisconnect(channelId: String) {
        inFlightChunks.entries.removeIf { (idx, pair) ->
            if (pair.first == channelId) {
                pendingIndices.add(idx)
                true
            } else {
                false
            }
        }
    }

    override fun close() {
        try {
            fileChannel.close()
            randomAccessFile.close()
        } catch (_: Exception) {}
    }
}

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

/**
 * Writes incoming out-of-order verified chunks directly into the filesystem
 * using high-performance NIO FileChannel random-access writes on Android.
 */
class SparseFileWriter(
    val manifest: TransferManifest,
    destinationDir: File
) : AutoCloseable {

    private val finalFile: File
    private val tempPartFile: File
    private val randomAccessFile: RandomAccessFile
    private val fileChannel: FileChannel
    private val completedChunks: BitSet = BitSet(manifest.totalChunks)
    private var verifiedChunkCount: Int = 0
    private val lock = Any()

    val transferId: Long get() = manifest.transferId
    val fileName: String get() = manifest.fileName
    val totalBytes: Long get() = manifest.totalBytes
    val totalChunks: Int get() = manifest.totalChunks
    val completedCount: Int get() = synchronized(lock) { verifiedChunkCount }
    val isComplete: Boolean get() = synchronized(lock) { verifiedChunkCount == totalChunks }

    init {
        destinationDir.mkdirs()
        finalFile = File(destinationDir, manifest.fileName)
        tempPartFile = File(destinationDir, "${manifest.fileName}.c2p_part")

        randomAccessFile = RandomAccessFile(tempPartFile, "rw")
        randomAccessFile.setLength(manifest.totalBytes)
        fileChannel = randomAccessFile.channel
    }

    /**
     * Writes an incoming chunk directly to its deterministic byte offset.
     */
    fun writeChunk(chunk: TransferChunk): Boolean {
        if (chunk.chunkIndex >= totalChunks || chunk.chunkIndex < 0) {
            return false
        }

        synchronized(lock) {
            if (completedChunks.get(chunk.chunkIndex)) {
                return true // Already written
            }

            val offset = chunk.chunkIndex.toLong() * manifest.chunkSize
            val buffer = ByteBuffer.wrap(chunk.payload)
            fileChannel.write(buffer, offset)

            completedChunks.set(chunk.chunkIndex, true)
            verifiedChunkCount++
        }
        return true
    }

    /**
     * Gets a compact bitset byte array representing which chunks are completed.
     */
    fun getReceivedBitset(): ByteArray {
        synchronized(lock) {
            return completedChunks.toByteArray()
        }
    }

    /**
     * Finalizes file after all chunks are verified, checks SHA-256, and renames temp file.
     */
    fun finalizeFile(): Result<File> {
        if (!isComplete) {
            return Result.failure(IllegalStateException("Only $verifiedChunkCount/$totalChunks chunks received."))
        }

        return try {
            fileChannel.close()
            randomAccessFile.close()

            // Verify whole file hash if present
            if (manifest.wholeFileHashHex.isNotEmpty()) {
                val digest = MessageDigest.getInstance("SHA-256")
                FileInputStream(tempPartFile).use { fis ->
                    val buffer = ByteArray(65536)
                    var read: Int
                    while (fis.read(buffer).also { read = it } != -1) {
                        digest.update(buffer, 0, read)
                    }
                }
                val computedHash = digest.digest().joinToString("") { "%02x".format(it) }
                if (!computedHash.equals(manifest.wholeFileHashHex, ignoreCase = true)) {
                    return Result.failure(IllegalStateException("Hash mismatch. Expected: ${manifest.wholeFileHashHex}, Got: $computedHash"))
                }
            }

            if (finalFile.exists()) {
                finalFile.delete()
            }
            if (!tempPartFile.renameTo(finalFile)) {
                return Result.failure(IllegalStateException("Failed to rename .c2p_part to final file."))
            }

            Result.success(finalFile)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    override fun close() {
        try {
            fileChannel.close()
            randomAccessFile.close()
        } catch (_: Exception) {}
    }
}

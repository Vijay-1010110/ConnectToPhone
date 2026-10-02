package com.connecttophone.protocol

import org.json.JSONArray
import org.json.JSONObject
import java.nio.ByteBuffer
import java.nio.ByteOrder

// Handshake Models
data class HandshakeSyn(
    val deviceId: String,
    val deviceName: String,
    val deviceType: DeviceType = DeviceType.ANDROID,
    val appVersion: String = "1.0.0",
    val supportedTransports: Long = 0,
    val publicKeyHex: String = "",
    val timestamp: Long = System.currentTimeMillis()
) {
    fun toJson(): String {
        val obj = JSONObject()
        obj.put("deviceId", deviceId)
        obj.put("deviceName", deviceName)
        obj.put("deviceType", deviceType.value.toInt())
        obj.put("appVersion", appVersion)
        obj.put("supportedTransports", supportedTransports)
        obj.put("publicKeyHex", publicKeyHex)
        obj.put("timestamp", timestamp)
        return obj.toString()
    }

    companion object {
        fun fromJson(jsonStr: String): HandshakeSyn {
            val obj = JSONObject(jsonStr)
            return HandshakeSyn(
                deviceId = obj.optString("deviceId", ""),
                deviceName = obj.optString("deviceName", ""),
                deviceType = DeviceType.fromByte(obj.optInt("deviceType", 1).toByte()),
                appVersion = obj.optString("appVersion", "1.0.0"),
                supportedTransports = obj.optLong("supportedTransports", 0L),
                publicKeyHex = obj.optString("publicKeyHex", ""),
                timestamp = obj.optLong("timestamp", System.currentTimeMillis())
            )
        }
    }
}

data class HandshakeAck(
    val accepted: Boolean,
    val sessionId: Long,
    val deviceId: String,
    val deviceName: String,
    val publicKeyHex: String = "",
    val pairingPin: String = "",
    val rejectionReason: String? = null
) {
    fun toJson(): String {
        val obj = JSONObject()
        obj.put("accepted", accepted)
        obj.put("sessionId", sessionId)
        obj.put("deviceId", deviceId)
        obj.put("deviceName", deviceName)
        obj.put("publicKeyHex", publicKeyHex)
        obj.put("pairingPin", pairingPin)
        obj.put("rejectionReason", rejectionReason ?: JSONObject.NULL)
        return obj.toString()
    }

    companion object {
        fun fromJson(jsonStr: String): HandshakeAck {
            val obj = JSONObject(jsonStr)
            return HandshakeAck(
                accepted = obj.optBoolean("accepted", false),
                sessionId = obj.optLong("sessionId", 0L),
                deviceId = obj.optString("deviceId", ""),
                deviceName = obj.optString("deviceName", ""),
                publicKeyHex = obj.optString("publicKeyHex", ""),
                pairingPin = obj.optString("pairingPin", ""),
                rejectionReason = if (obj.isNull("rejectionReason")) null else obj.optString("rejectionReason")
            )
        }
    }
}

// Transfer Models
data class TransferManifest(
    val transferId: Long,
    val fileName: String,
    val relativePath: String,
    val totalBytes: Long,
    val chunkSize: Int = 2 * 1024 * 1024,
    val totalChunks: Int,
    val wholeFileHashHex: String = "",
    val mimeType: String = "application/octet-stream",
    val modifiedTimestamp: Long = 0L,
    val isPreview: Boolean = false
) {
    fun toJson(): String {
        val obj = JSONObject()
        obj.put("transferId", transferId)
        obj.put("fileName", fileName)
        obj.put("relativePath", relativePath)
        obj.put("totalBytes", totalBytes)
        obj.put("chunkSize", chunkSize)
        obj.put("totalChunks", totalChunks)
        obj.put("wholeFileHashHex", wholeFileHashHex)
        obj.put("mimeType", mimeType)
        obj.put("modifiedTimestamp", modifiedTimestamp)
        obj.put("isPreview", isPreview)
        return obj.toString()
    }

    companion object {
        fun fromJson(jsonStr: String): TransferManifest {
            val obj = JSONObject(jsonStr)
            return TransferManifest(
                transferId = obj.optLong("transferId", 0L),
                fileName = obj.optString("fileName", ""),
                relativePath = obj.optString("relativePath", ""),
                totalBytes = obj.optLong("totalBytes", 0L),
                chunkSize = obj.optInt("chunkSize", 2 * 1024 * 1024),
                totalChunks = obj.optInt("totalChunks", 0),
                wholeFileHashHex = obj.optString("wholeFileHashHex", ""),
                mimeType = obj.optString("mimeType", "application/octet-stream"),
                modifiedTimestamp = obj.optLong("modifiedTimestamp", 0L),
                isPreview = obj.optBoolean("isPreview", false)
            )
        }
    }
}

/**
 * Packed binary transfer chunk:
 * [TransferId: 8B][ChunkIndex: 4B][DataLength: 4B][ChunkHash: 8B][Payload: N B]
 */
data class TransferChunk(
    val transferId: Long,
    val chunkIndex: Int,
    val chunkHash: Long,
    val payload: ByteArray
) {
    companion object {
        const val HEADER_SIZE = 24

        fun parse(bytes: ByteArray, offset: Int = 0, length: Int = bytes.size): TransferChunk? {
            if (length < HEADER_SIZE) return null
            val buf = ByteBuffer.wrap(bytes, offset, length)
            buf.order(ByteOrder.LITTLE_ENDIAN)

            val transferId = buf.long
            val chunkIndex = buf.int
            val dataLen = buf.int
            val chunkHash = buf.long

            if (length < HEADER_SIZE + dataLen) return null
            val payload = ByteArray(dataLen)
            buf.get(payload)

            return TransferChunk(transferId, chunkIndex, chunkHash, payload)
        }
    }

    fun serialize(): ByteArray {
        val bytes = ByteArray(HEADER_SIZE + payload.size)
        val buf = ByteBuffer.wrap(bytes)
        buf.order(ByteOrder.LITTLE_ENDIAN)
        buf.putLong(transferId)
        buf.putInt(chunkIndex)
        buf.putInt(payload.size)
        buf.putLong(chunkHash)
        buf.put(payload)
        return bytes
    }
}

data class TransferChunkAck(
    val transferId: Long,
    val chunkIndex: Int,
    val success: Boolean
) {
    fun serialize(): ByteArray {
        val bytes = ByteArray(13)
        val buf = ByteBuffer.wrap(bytes)
        buf.order(ByteOrder.LITTLE_ENDIAN)
        buf.putLong(transferId)
        buf.putInt(chunkIndex)
        buf.put(if (success) 1.toByte() else 0.toByte())
        return bytes
    }

    companion object {
        fun parse(bytes: ByteArray): TransferChunkAck? {
            if (bytes.size < 13) return null
            val buf = ByteBuffer.wrap(bytes)
            buf.order(ByteOrder.LITTLE_ENDIAN)
            val transferId = buf.long
            val chunkIndex = buf.int
            val success = buf.get() == 1.toByte()
            return TransferChunkAck(transferId, chunkIndex, success)
        }
    }
}

// Clipboard Payload
data class ClipboardPayload(
    val format: ClipboardFormat = ClipboardFormat.PLAIN_TEXT,
    val contentHash: Long,
    val timestamp: Long = System.currentTimeMillis(),
    val text: String? = null,
    val imageBase64: String? = null,
    val mimeType: String = "text/plain",
    val sourceDeviceId: String = ""
) {
    fun toJson(): String {
        val obj = JSONObject()
        obj.put("format", format.value.toInt())
        obj.put("contentHash", contentHash)
        obj.put("timestamp", timestamp)
        obj.put("text", text ?: JSONObject.NULL)
        obj.put("imageBase64", imageBase64 ?: JSONObject.NULL)
        obj.put("mimeType", mimeType)
        obj.put("sourceDeviceId", sourceDeviceId)
        return obj.toString()
    }

    companion object {
        fun fromJson(jsonStr: String): ClipboardPayload {
            val obj = JSONObject(jsonStr)
            return ClipboardPayload(
                format = ClipboardFormat.fromByte(obj.optInt("format", 1).toByte()),
                contentHash = obj.optLong("contentHash", 0L),
                timestamp = obj.optLong("timestamp", System.currentTimeMillis()),
                text = if (obj.isNull("text")) null else obj.optString("text"),
                imageBase64 = if (obj.isNull("imageBase64")) null else obj.optString("imageBase64"),
                mimeType = obj.optString("mimeType", "text/plain"),
                sourceDeviceId = obj.optString("sourceDeviceId", "")
            )
        }
    }
}

// Virtual File System Models
data class FsEntry(
    val name: String,
    val path: String,
    val entryType: FsEntryType,
    val category: FileCategory,
    val sizeBytes: Long,
    val modifiedTimestamp: Long,
    val isHidden: Boolean
) {
    fun toJsonObject(): JSONObject {
        val obj = JSONObject()
        obj.put("name", name)
        obj.put("path", path)
        obj.put("entryType", entryType.value.toInt())
        obj.put("category", category.value.toInt())
        obj.put("sizeBytes", sizeBytes)
        obj.put("modifiedTimestamp", modifiedTimestamp)
        obj.put("isHidden", isHidden)
        return obj
    }

    companion object {
        fun fromJsonObject(obj: JSONObject): FsEntry {
            return FsEntry(
                name = obj.optString("name", ""),
                path = obj.optString("path", ""),
                entryType = FsEntryType.fromByte(obj.optInt("entryType", 0).toByte()),
                category = FileCategory.fromByte(obj.optInt("category", 0).toByte()),
                sizeBytes = obj.optLong("sizeBytes", 0L),
                modifiedTimestamp = obj.optLong("modifiedTimestamp", 0L),
                isHidden = obj.optBoolean("isHidden", false)
            )
        }
    }
}

data class FsListDirRequest(
    val targetPath: String = "",
    val includeHidden: Boolean = false
) {
    fun toJson(): String {
        val obj = JSONObject()
        obj.put("targetPath", targetPath)
        obj.put("includeHidden", includeHidden)
        return obj.toString()
    }

    companion object {
        fun fromJson(jsonStr: String): FsListDirRequest {
            val obj = JSONObject(jsonStr)
            return FsListDirRequest(
                targetPath = obj.optString("targetPath", ""),
                includeHidden = obj.optBoolean("includeHidden", false)
            )
        }
    }
}

data class FsListDirResponse(
    val currentPath: String,
    val entries: List<FsEntry>,
    val success: Boolean = true,
    val errorMessage: String? = null
) {
    fun toJson(): String {
        val obj = JSONObject()
        obj.put("currentPath", currentPath)
        val arr = JSONArray()
        entries.forEach { arr.put(it.toJsonObject()) }
        obj.put("entries", arr)
        obj.put("success", success)
        obj.put("errorMessage", errorMessage ?: JSONObject.NULL)
        return obj.toString()
    }

    companion object {
        fun fromJson(jsonStr: String): FsListDirResponse {
            val obj = JSONObject(jsonStr)
            val arr = obj.optJSONArray("entries") ?: JSONArray()
            val list = mutableListOf<FsEntry>()
            for (i in 0 until arr.length()) {
                list.add(FsEntry.fromJsonObject(arr.getJSONObject(i)))
            }
            return FsListDirResponse(
                currentPath = obj.optString("currentPath", ""),
                entries = list,
                success = obj.optBoolean("success", true),
                errorMessage = if (obj.isNull("errorMessage")) null else obj.optString("errorMessage")
            )
        }
    }
}

data class FsPullFileRequest(
    val remoteFilePath: String,
    val startOffset: Long = 0L,
    val isPreview: Boolean = false
) {
    fun toJson(): String {
        val obj = JSONObject()
        obj.put("remoteFilePath", remoteFilePath)
        obj.put("startOffset", startOffset)
        obj.put("isPreview", isPreview)
        return obj.toString()
    }

    companion object {
        fun fromJson(jsonStr: String): FsPullFileRequest {
            val obj = JSONObject(jsonStr)
            return FsPullFileRequest(
                remoteFilePath = obj.optString("remoteFilePath", ""),
                startOffset = obj.optLong("startOffset", 0L),
                isPreview = obj.optBoolean("isPreview", false)
            )
        }
    }
}

// Remote Control Models
data class MouseControlPayload(
    val dx: Int = 0,
    val dy: Int = 0,
    val leftClick: Boolean = false,
    val rightClick: Boolean = false,
    val middleClick: Boolean = false,
    val leftButtonDown: Boolean = false,
    val rightButtonDown: Boolean = false,
    val wheelDelta: Int = 0,
    val wheelDeltaX: Int = 0
) {
    fun toJson(): String {
        val obj = JSONObject()
        obj.put("dx", dx)
        obj.put("dy", dy)
        obj.put("leftClick", leftClick)
        obj.put("rightClick", rightClick)
        obj.put("middleClick", middleClick)
        obj.put("leftDown", leftButtonDown)
        obj.put("rightDown", rightButtonDown)
        obj.put("wheelDelta", wheelDelta)
        obj.put("wheelDeltaX", wheelDeltaX)
        return obj.toString()
    }

    companion object {
        fun fromJson(jsonStr: String): MouseControlPayload {
            val obj = JSONObject(jsonStr)
            return MouseControlPayload(
                dx = obj.optInt("dx", 0),
                dy = obj.optInt("dy", 0),
                leftClick = obj.optBoolean("leftClick", false),
                rightClick = obj.optBoolean("rightClick", false),
                middleClick = obj.optBoolean("middleClick", false),
                leftButtonDown = obj.optBoolean("leftDown", false),
                rightButtonDown = obj.optBoolean("rightDown", false),
                wheelDelta = obj.optInt("wheelDelta", 0),
                wheelDeltaX = obj.optInt("wheelDeltaX", 0)
            )
        }
    }
}

data class KeyboardControlPayload(
    val text: String? = null,
    val virtualKey: Int = 0,
    val modifiers: Int = 0,
    val specialKey: String? = null
) {
    fun toJson(): String {
        val obj = JSONObject()
        obj.put("text", text ?: JSONObject.NULL)
        obj.put("virtualKey", virtualKey)
        obj.put("modifiers", modifiers)
        obj.put("specialKey", specialKey ?: JSONObject.NULL)
        return obj.toString()
    }

    companion object {
        fun fromJson(jsonStr: String): KeyboardControlPayload {
            val obj = JSONObject(jsonStr)
            return KeyboardControlPayload(
                text = if (obj.isNull("text")) null else obj.optString("text"),
                virtualKey = obj.optInt("virtualKey", 0),
                modifiers = obj.optInt("modifiers", 0),
                specialKey = if (obj.isNull("specialKey")) null else obj.optString("specialKey")
            )
        }
    }
}

data class MediaControlPayload(val action: Byte) {
    fun toJson(): String {
        val obj = JSONObject()
        obj.put("action", action.toInt())
        return obj.toString()
    }

    companion object {
        fun fromJson(jsonStr: String): MediaControlPayload {
            val obj = JSONObject(jsonStr)
            return MediaControlPayload(action = obj.optInt("action", 0).toByte())
        }
    }
}

data class PowerControlPayload(val action: Byte) {
    fun toJson(): String {
        val obj = JSONObject()
        obj.put("action", action.toInt())
        return obj.toString()
    }

    companion object {
        fun fromJson(jsonStr: String): PowerControlPayload {
            val obj = JSONObject(jsonStr)
            return PowerControlPayload(action = obj.optInt("action", 0).toByte())
        }
    }
}


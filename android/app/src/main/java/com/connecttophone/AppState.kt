package com.connecttophone

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.widget.Toast
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.content.FileProvider
import com.connecttophone.protocol.*
import com.connecttophone.transport.ITransport
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.io.File
import java.util.UUID

data class RemoteFileItem(
    val name: String,
    val path: String,
    val isDirectory: Boolean,
    val sizeDisplay: String
)

data class TransferHistoryItem(
    val id: String = UUID.randomUUID().toString(),
    val fileName: String,
    val localFilePath: String,
    val sizeBytes: Long,
    val timestamp: Long = System.currentTimeMillis(),
    val isOutgoing: Boolean = false,
    val isSuccess: Boolean = true
) {
    val sizeDisplay: String get() = "%.1f MB".format(sizeBytes / 1024.0 / 1024.0)
}

object AppState {
    val pcEntries = mutableStateListOf<RemoteFileItem>()
    var currentPcPath by mutableStateOf("/")
    var isQueryingPcFiles by mutableStateOf(false)
    val transferHistory = mutableStateListOf<TransferHistoryItem>()

    var isConnected by mutableStateOf(false)
    var connectedDeviceName by mutableStateOf("Searching...")
    var selectedNavigationTab by mutableStateOf(0)
    var currentSpeedMb by mutableStateOf(0.0)
    var usbSpeedMb by mutableStateOf(0.0)
    var wifiSpeedMb by mutableStateOf(0.0)

    var activeTransportInstance: ITransport? = null
    var scope: CoroutineScope? = null
    var triggerConnectCallback: (() -> Unit)? = null

    fun loadExistingFiles(context: Context) {
        val dir = context.getExternalFilesDir(null) ?: context.filesDir
        dir.listFiles()?.filter { it.isFile && !it.name.endsWith(".c2p_part") }?.forEach { file ->
            if (transferHistory.none { it.localFilePath == file.absolutePath }) {
                transferHistory.add(0, TransferHistoryItem(
                    fileName = file.name,
                    localFilePath = file.absolutePath,
                    sizeBytes = file.length(),
                    timestamp = file.lastModified(),
                    isOutgoing = false
                ))
            }
        }
    }

    fun connectOrRefreshPc() {
        triggerConnectCallback?.invoke()
        if (isConnected) {
            requestPcDirectory("/")
        }
    }

    fun requestPcDirectory(path: String) {
        val transport = activeTransportInstance
        if (transport == null || !transport.isConnected) {
            isQueryingPcFiles = false
            triggerConnectCallback?.invoke()
            return
        }

        isQueryingPcFiles = true
        val currentScope = scope ?: CoroutineScope(Dispatchers.IO)
        currentScope.launch {
            try {
                val req = FsListDirRequest(targetPath = path, includeHidden = false)
                val frame = BinaryFrame(
                    type = FrameType.FS_LIST_DIR_REQ,
                    sessionId = MainActivity.activeSessionId,
                    payload = req.toJson().toByteArray(Charsets.UTF_8)
                )
                transport.sendFrame(frame)

                // 4-second safety watchdog so UI never hangs in loading state
                delay(4000)
                if (isQueryingPcFiles) {
                    isQueryingPcFiles = false
                }
            } catch (_: Exception) {
                isQueryingPcFiles = false
            }
        }
    }

    var activeProtocolName by mutableStateOf("USB ⚡")
    var standbyProtocols by mutableStateOf("Wi-Fi Standby 💤")
    var isPowerSaverIdle by mutableStateOf(true)

    fun pullFileFromPc(remoteFilePath: String, isPreview: Boolean = false, context: Context) {
        val transport = activeTransportInstance ?: run {
            Toast.makeText(context, "PC not connected yet.", Toast.LENGTH_SHORT).show()
            return
        }
        val currentScope = scope ?: CoroutineScope(Dispatchers.IO)
        currentScope.launch {
            try {
                val req = FsPullFileRequest(remoteFilePath = remoteFilePath, isPreview = isPreview)
                val frame = BinaryFrame(
                    type = FrameType.FS_PULL_FILE_REQ,
                    sessionId = MainActivity.activeSessionId,
                    payload = req.toJson().toByteArray(Charsets.UTF_8)
                )
                transport.sendFrame(frame)
            } catch (_: Exception) {}
        }
        Toast.makeText(context, if (isPreview) "Loading Quick Preview..." else "Downloading to Phone...", Toast.LENGTH_SHORT).show()
    }

    // REMOTE CONTROL METHODS
    fun sendMouseDelta(
        dx: Int,
        dy: Int,
        leftClick: Boolean = false,
        rightClick: Boolean = false,
        middleClick: Boolean = false,
        leftDown: Boolean = false,
        rightDown: Boolean = false,
        wheelDelta: Int = 0,
        wheelDeltaX: Int = 0
    ) {
        val transport = activeTransportInstance ?: return
        val currentScope = scope ?: CoroutineScope(Dispatchers.IO)
        currentScope.launch {
            try {
                val payload = MouseControlPayload(
                    dx = dx,
                    dy = dy,
                    leftClick = leftClick,
                    rightClick = rightClick,
                    middleClick = middleClick,
                    leftButtonDown = leftDown,
                    rightButtonDown = rightDown,
                    wheelDelta = wheelDelta,
                    wheelDeltaX = wheelDeltaX
                )
                transport.sendFrame(BinaryFrame(
                    type = FrameType.CTRL_MOUSE,
                    sessionId = MainActivity.activeSessionId,
                    payload = payload.toJson().toByteArray(Charsets.UTF_8)
                ))
            } catch (_: Exception) {}
        }
    }

    fun sendKeyboardKey(specialKey: String? = null, text: String? = null) {
        val transport = activeTransportInstance ?: return
        val currentScope = scope ?: CoroutineScope(Dispatchers.IO)
        currentScope.launch {
            try {
                val payload = KeyboardControlPayload(
                    text = text,
                    specialKey = specialKey
                )
                transport.sendFrame(BinaryFrame(
                    type = FrameType.CTRL_KEYBOARD,
                    sessionId = MainActivity.activeSessionId,
                    payload = payload.toJson().toByteArray(Charsets.UTF_8)
                ))
            } catch (_: Exception) {}
        }
    }

    fun sendMediaAction(action: Byte) {
        val transport = activeTransportInstance ?: return
        val currentScope = scope ?: CoroutineScope(Dispatchers.IO)
        currentScope.launch {
            try {
                val payload = MediaControlPayload(action)
                transport.sendFrame(BinaryFrame(
                    type = FrameType.CTRL_MEDIA,
                    sessionId = MainActivity.activeSessionId,
                    payload = payload.toJson().toByteArray(Charsets.UTF_8)
                ))
            } catch (_: Exception) {}
        }
    }

    fun sendPowerAction(action: Byte) {
        val transport = activeTransportInstance ?: return
        val currentScope = scope ?: CoroutineScope(Dispatchers.IO)
        currentScope.launch {
            try {
                val payload = PowerControlPayload(action)
                transport.sendFrame(BinaryFrame(
                    type = FrameType.CTRL_POWER,
                    sessionId = MainActivity.activeSessionId,
                    payload = payload.toJson().toByteArray(Charsets.UTF_8)
                ))
            } catch (_: Exception) {}
        }
    }

    fun openFile(context: Context, item: TransferHistoryItem) {
        val file = File(item.localFilePath)
        if (!file.exists()) {
            Toast.makeText(context, "File does not exist: ${file.name}", Toast.LENGTH_SHORT).show()
            return
        }

        try {
            val uri: Uri = FileProvider.getUriForFile(
                context,
                "com.connecttophone.fileprovider",
                file
            )

            val mimeType = context.contentResolver.getType(uri) ?: getMimeTypeFromExt(file.extension)

            val intent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(uri, mimeType)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }

            context.startActivity(Intent.createChooser(intent, "Open with"))
        } catch (e: Exception) {
            Toast.makeText(context, "Error opening file: ${e.message}", Toast.LENGTH_LONG).show()
        }
    }

    private fun getMimeTypeFromExt(ext: String): String {
        return when (ext.lowercase()) {
            "mkv", "mp4", "avi", "mov", "webm" -> "video/*"
            "mp3", "flac", "wav", "m4a", "ogg" -> "audio/*"
            "jpg", "jpeg", "png", "webp", "gif" -> "image/*"
            "pdf" -> "application/pdf"
            "txt" -> "text/plain"
            "zip", "rar", "7z" -> "application/zip"
            "apk" -> "application/vnd.android.package-archive"
            else -> "*/*"
        }
    }
}

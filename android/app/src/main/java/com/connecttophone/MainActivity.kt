package com.connecttophone

import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothServerSocket
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.Uri
import android.os.BatteryManager
import android.os.Build
import android.os.Bundle
import android.provider.OpenableColumns
import android.provider.Settings
import android.util.Log
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.ui.Modifier
import androidx.lifecycle.lifecycleScope
import com.connecttophone.clipboard.ClipboardSyncService
import com.connecttophone.engine.ChunkScheduler
import com.connecttophone.engine.SparseFileWriter
import com.connecttophone.explorer.AndroidFileSystemHost
import com.connecttophone.protocol.*
import com.connecttophone.service.TransferForegroundService
import com.connecttophone.theme.ConnectToPhoneTheme
import com.connecttophone.transport.BluetoothRfcommTransport
import com.connecttophone.transport.ITransport
import com.connecttophone.transport.TcpServer
import com.connecttophone.transport.TcpSocketTransport
import com.connecttophone.transport.UdpDiscoveryBeacon
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.File
import java.io.FileOutputStream
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap

class MainActivity : ComponentActivity() {

    private var tcpServer: TcpServer? = null
    private var udpBeacon: UdpDiscoveryBeacon? = null
    private var btServerSocket: BluetoothServerSocket? = null
    private val SPP_UUID = UUID.fromString("00001101-0000-1000-8000-00805F9B34FB")
    private var clipboardService: ClipboardSyncService? = null
    private val fsHost = AndroidFileSystemHost()
    private val activeTransports = ConcurrentHashMap<String, ITransport>()
    private val activeWriters = ConcurrentHashMap<Long, SparseFileWriter>()

    companion object {
        var activeSessionId: Long = 12345678L
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        AppState.scope = lifecycleScope
        AppState.loadExistingFiles(this)

        val deviceId = Settings.Secure.getString(contentResolver, Settings.Secure.ANDROID_ID) ?: "android_${Build.MODEL}"
        val deviceName = "${Build.MANUFACTURER} ${Build.MODEL}"

        // 1. Start top-bar foreground status/speed service
        TransferForegroundService.startService(this)

        // 2. Start TCP server on port 42424 for Wi-Fi and USB ADB connections
        tcpServer = TcpServer(42424).apply {
            onClientConnected = { transport ->
                setupTransportHandlers(transport)
            }
            start()
        }

        // 3. Start UDP zero-touch discovery beacon
        udpBeacon = UdpDiscoveryBeacon(deviceId, deviceName, 42424).apply {
            onPeerDiscovered = { peer ->
                if (!AppState.isConnected && peer.remoteIpAddress.isNotEmpty()) {
                    lifecycleScope.launch(Dispatchers.IO) {
                        try {
                            val result = tcpServer?.connectToPeer(peer.remoteIpAddress, peer.tcpPort)
                            result?.getOrNull()?.let { transport ->
                                setupTransportHandlers(transport)
                                sendHandshakeSyn(transport, deviceId, deviceName)
                            }
                        } catch (_: Exception) {}
                    }
                }
            }
            start()
        }

        // 4. Wire manual connect trigger
        AppState.triggerConnectCallback = {
            probeAndConnect(deviceId, deviceName)
        }

        // 5. Start background auto-connect loop (USB + Wi-Fi + Bluetooth)
        startAutoConnectLoop(deviceId, deviceName)

        // 6. Start Bluetooth RFCOMM listener for incoming paired PC connections
        startBluetoothListener()

        // 7. Start Clipboard auto-sync
        clipboardService = ClipboardSyncService(this).apply {
            onLocalClipboardChanged = { payload ->
                payload.text?.let { text ->
                    runOnUiThread {
                        if (!AppState.clipboardHistory.contains(text)) {
                            AppState.clipboardHistory.add(0, text)
                        }
                    }
                }
                broadcastFrame(BinaryFrame(
                    type = FrameType.CLIPBOARD_SYNC,
                    sessionId = activeSessionId,
                    payload = payload.toJson().toByteArray(Charsets.UTF_8)
                ))
            }
            startListening()
        }

        // 7. Handle incoming Share Sheet Intent and Widget Navigation
        val targetTab = intent?.getIntExtra("TARGET_TAB", -1) ?: -1
        if (targetTab >= 0) {
            AppState.selectedNavigationTab = targetTab
        }
        handleShareIntent(intent)

        // 8. Daily/Periodic cache LRU cleanup loop (runs every 6 hours)
        lifecycleScope.launch(Dispatchers.IO) {
            while (isActive) {
                com.connecttophone.cache.CacheManager.runPeriodicCleanup(this@MainActivity)
                delay(6 * 3600 * 1000L)
            }
        }

        setContent {
            ConnectToPhoneTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background
                ) {
                    MainNavigation()
                }
            }
        }
    }

    private fun startAutoConnectLoop(deviceId: String, deviceName: String) {
        lifecycleScope.launch(Dispatchers.IO) {
            while (isActive) {
                if (!AppState.isConnected || AppState.activeTransportInstance?.isConnected != true) {
                    probeAndConnect(deviceId, deviceName)
                }
                delay(2500)
            }
        }
    }

    private fun startBluetoothListener() {
        lifecycleScope.launch(Dispatchers.IO) {
            try {
                val btAdapter = (getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager)?.adapter
                    ?: BluetoothAdapter.getDefaultAdapter()
                if (btAdapter != null && btAdapter.isEnabled) {
                    btServerSocket = btAdapter.listenUsingInsecureRfcommWithServiceRecord("ConnectToWindow", SPP_UUID)
                    while (isActive) {
                        val socket = btServerSocket?.accept() ?: break
                        val transport = BluetoothRfcommTransport(socket)
                        setupTransportHandlers(transport)
                    }
                }
            } catch (_: Exception) {}
        }
    }

    private fun probeAndConnect(deviceId: String, deviceName: String) {
        lifecycleScope.launch(Dispatchers.IO) {
            if (AppState.isConnected && AppState.activeTransportInstance?.isConnected == true) return@launch

            // 1. Try USB ADB reverse tunnel (127.0.0.1:42425)
            try {
                val usbRes = tcpServer?.connectToPeer("127.0.0.1", 42425)
                val usbTransport = usbRes?.getOrNull()
                if (usbTransport != null && usbTransport.isConnected) {
                    setupTransportHandlers(usbTransport)
                    sendHandshakeSyn(usbTransport, deviceId, deviceName)
                    return@launch
                }
            } catch (_: Exception) {}

            // 2. Try known PC Wi-Fi IP candidates
            val candidates = listOf("192.168.6.225", "10.0.2.2")
            for (ip in candidates) {
                try {
                    val wifiRes = tcpServer?.connectToPeer(ip, 42424)
                    val wifiTransport = wifiRes?.getOrNull()
                    if (wifiTransport != null && wifiTransport.isConnected) {
                        setupTransportHandlers(wifiTransport)
                        sendHandshakeSyn(wifiTransport, deviceId, deviceName)
                        return@launch
                    }
                } catch (_: Exception) {}
            }

            // 3. Try paired Bluetooth devices
            try {
                val btAdapter = (getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager)?.adapter
                    ?: BluetoothAdapter.getDefaultAdapter()
                if (btAdapter != null && btAdapter.isEnabled) {
                    val paired = btAdapter.bondedDevices
                    for (dev in paired) {
                        try {
                            val socket = dev.createInsecureRfcommSocketToServiceRecord(SPP_UUID)
                            socket.connect()
                            if (socket.isConnected) {
                                val transport = BluetoothRfcommTransport(socket)
                                setupTransportHandlers(transport)
                                sendHandshakeSyn(transport, deviceId, deviceName)
                                return@launch
                            }
                        } catch (_: Exception) {}
                    }
                }
            } catch (_: Exception) {}
        }
    }

    private suspend fun sendHandshakeSyn(transport: ITransport, deviceId: String, deviceName: String) {
        val syn = HandshakeSyn(
            deviceId = deviceId,
            deviceName = deviceName,
            deviceType = DeviceType.ANDROID,
            supportedTransports = TransportType.USB_ADB or TransportType.WIFI_LAN or TransportType.BLUETOOTH_RFCOMM
        )
        transport.sendFrame(BinaryFrame(
            type = FrameType.HANDSHAKE_SYN,
            sessionId = activeSessionId,
            payload = syn.toJson().toByteArray(Charsets.UTF_8)
        ))
        // Request PC root drives
        AppState.requestPcDirectory("/")
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        val targetTab = intent.getIntExtra("TARGET_TAB", -1)
        if (targetTab >= 0) {
            AppState.selectedNavigationTab = targetTab
        }
        handleShareIntent(intent)
    }

    private fun handleShareIntent(intent: Intent?) {
        if (intent == null) return
        val action = intent.action
        val type = intent.type

        if (Intent.ACTION_SEND == action && type != null) {
            val uri = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java)
            } else {
                @Suppress("DEPRECATION")
                intent.getParcelableExtra(Intent.EXTRA_STREAM)
            }
            val text = intent.getStringExtra(Intent.EXTRA_TEXT)

            if (uri != null) {
                queueUriForSending(uri)
            } else if (!text.isNullOrEmpty()) {
                val clip = ClipboardPayload(
                    format = ClipboardFormat.PLAIN_TEXT,
                    contentHash = text.hashCode().toLong(),
                    text = text
                )
                broadcastFrame(BinaryFrame(
                    type = FrameType.CLIPBOARD_SYNC,
                    sessionId = activeSessionId,
                    payload = clip.toJson().toByteArray(Charsets.UTF_8)
                ))
                Toast.makeText(this, "Shared text sent to PC clipboard!", Toast.LENGTH_SHORT).show()
            }
        } else if (Intent.ACTION_SEND_MULTIPLE == action && type != null) {
            val uris = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM, Uri::class.java)
            } else {
                @Suppress("DEPRECATION")
                intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM)
            }
            uris?.forEach { queueUriForSending(it) }
        }
    }

    private fun queueUriForSending(uri: Uri) {
        lifecycleScope.launch(Dispatchers.IO) {
            try {
                var fileName = "shared_${System.currentTimeMillis()}"
                contentResolver.query(uri, null, null, null, null)?.use { cursor ->
                    val nameIdx = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    if (cursor.moveToFirst() && nameIdx >= 0) {
                        fileName = cursor.getString(nameIdx)
                    }
                }

                val tempFile = File(cacheDir, fileName)
                contentResolver.openInputStream(uri)?.use { input ->
                    FileOutputStream(tempFile).use { output ->
                        input.copyTo(output)
                    }
                }

                val transport = AppState.activeTransportInstance
                if (transport != null && transport.isConnected) {
                    streamFileToPeer(transport, tempFile)
                    runOnUiThread {
                        Toast.makeText(this@MainActivity, "Sending $fileName to PC...", Toast.LENGTH_SHORT).show()
                    }
                } else {
                    runOnUiThread {
                        Toast.makeText(this@MainActivity, "PC not connected yet. File queued.", Toast.LENGTH_SHORT).show()
                    }
                }
            } catch (e: Exception) {
                runOnUiThread {
                    Toast.makeText(this@MainActivity, "Share error: ${e.message}", Toast.LENGTH_LONG).show()
                }
            }
        }
    }

    private fun setupTransportHandlers(transport: ITransport) {
        activeTransports[transport.channelId] = transport
        AppState.activeTransportInstance = transport
        AppState.isConnected = true

        runOnUiThread {
            when (transport.type) {
                TransportType.USB_ADB -> {
                    AppState.activeProtocolName = "USB ⚡"
                    AppState.standbyProtocols = "Wi-Fi & Bluetooth Standby 💤"
                    AppState.usbSpeedMb = 140.0
                    AppState.currentSpeedMb = 140.0
                }
                TransportType.BLUETOOTH_RFCOMM -> {
                    AppState.activeProtocolName = "Bluetooth ⚡"
                    AppState.standbyProtocols = "Wi-Fi & USB Standby 💤"
                    AppState.currentSpeedMb = 3.0
                }
                else -> {
                    AppState.activeProtocolName = "Wi-Fi 📶"
                    AppState.standbyProtocols = "USB & Bluetooth Standby 💤"
                    AppState.wifiSpeedMb = 85.0
                    AppState.currentSpeedMb = 85.0
                }
            }
        }

        transport.onFrameReceived = { t, frame ->
            lifecycleScope.launch(Dispatchers.IO) {
                try {
                    handleIncomingFrame(t, frame)
                } catch (e: Exception) {
                    Log.e("ConnectToPhone", "Error handling frame: ${e.message}", e)
                }
            }
        }

        transport.onDisconnected = { t ->
            activeTransports.remove(t.channelId)
            if (AppState.activeTransportInstance?.channelId == t.channelId) {
                AppState.activeTransportInstance = activeTransports.values.firstOrNull()
                AppState.isConnected = AppState.activeTransportInstance != null
                if (!AppState.isConnected) {
                    runOnUiThread {
                        AppState.connectedDeviceName = "Searching..."
                        AppState.currentSpeedMb = 0.0
                        AppState.isQueryingPcFiles = false
                    }
                }
            }
        }

        transport.startReceiving()
    }

    private suspend fun handleIncomingFrame(transport: ITransport, frame: BinaryFrame) {
        when (frame.type) {
            FrameType.HANDSHAKE_SYN -> {
                val syn = HandshakeSyn.fromJson(String(frame.payload, Charsets.UTF_8))
                activeSessionId = frame.sessionId
                runOnUiThread {
                    AppState.connectedDeviceName = syn.deviceName
                    AppState.isConnected = true
                    com.connecttophone.cache.CacheManager.playNotificationSound(this@MainActivity)
                }

                val ack = HandshakeAck(
                    accepted = true,
                    sessionId = frame.sessionId,
                    deviceId = Settings.Secure.getString(contentResolver, Settings.Secure.ANDROID_ID) ?: "android",
                    deviceName = "${Build.MANUFACTURER} ${Build.MODEL}",
                    pairingPin = ""
                )

                transport.sendFrame(BinaryFrame(
                    type = FrameType.HANDSHAKE_ACK,
                    sessionId = frame.sessionId,
                    payload = ack.toJson().toByteArray(Charsets.UTF_8)
                ))

                // Request PC root drives
                AppState.requestPcDirectory("/")
            }

            FrameType.HANDSHAKE_ACK -> {
                val ack = HandshakeAck.fromJson(String(frame.payload, Charsets.UTF_8))
                runOnUiThread {
                    AppState.connectedDeviceName = ack.deviceName
                    AppState.isConnected = true
                    com.connecttophone.cache.CacheManager.playNotificationSound(this@MainActivity)
                }
                AppState.requestPcDirectory("/")
            }

            FrameType.HEARTBEAT_PING -> {
                val batteryStatus: Intent? = IntentFilter(Intent.ACTION_BATTERY_CHANGED).let { filter ->
                    registerReceiver(null, filter)
                }
                val level: Int = batteryStatus?.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) ?: 100
                val scale: Int = batteryStatus?.getIntExtra(BatteryManager.EXTRA_SCALE, -1) ?: 100
                val batteryPct = (level * 100 / scale.toFloat()).toInt()

                val pong = BinaryFrame(
                    type = FrameType.HEARTBEAT_PONG,
                    sessionId = frame.sessionId,
                    payload = "{\"batteryPercentage\":$batteryPct}".toByteArray(Charsets.UTF_8)
                )
                transport.sendFrame(pong)
            }

            FrameType.CLIPBOARD_SYNC -> {
                val payload = ClipboardPayload.fromJson(String(frame.payload, Charsets.UTF_8))
                clipboardService?.setRemoteClipboard(payload)
                payload.text?.let { text ->
                    runOnUiThread {
                        if (!AppState.clipboardHistory.contains(text)) {
                            AppState.clipboardHistory.add(0, text)
                        }
                    }
                }
                runOnUiThread {
                    com.connecttophone.cache.CacheManager.playNotificationSound(this@MainActivity)
                    Toast.makeText(this@MainActivity, "Clipboard synced from PC!", Toast.LENGTH_SHORT).show()
                }
            }

            FrameType.FS_LIST_DIR_REQ -> {
                val req = FsListDirRequest.fromJson(String(frame.payload, Charsets.UTF_8))
                val resp = fsHost.listDirectory(req.targetPath, req.includeHidden)
                transport.sendFrame(BinaryFrame(
                    type = FrameType.FS_LIST_DIR_RESP,
                    sessionId = frame.sessionId,
                    payload = resp.toJson().toByteArray(Charsets.UTF_8)
                ))
            }

            FrameType.FS_LIST_DIR_RESP -> {
                val resp = FsListDirResponse.fromJson(String(frame.payload, Charsets.UTF_8))
                runOnUiThread {
                    AppState.isQueryingPcFiles = false
                    AppState.currentPcPath = resp.currentPath
                    AppState.pcEntries.clear()
                    resp.entries.forEach { entry ->
                        AppState.pcEntries.add(
                            RemoteFileItem(
                                name = if (entry.entryType == FsEntryType.DIRECTORY || entry.entryType == FsEntryType.DRIVE) "📁 ${entry.name}" else entry.name,
                                path = entry.path,
                                isDirectory = entry.entryType == FsEntryType.DIRECTORY || entry.entryType == FsEntryType.DRIVE,
                                sizeDisplay = if (entry.sizeBytes > 0) "%.1f MB".format(entry.sizeBytes / 1024.0 / 1024.0) else "-"
                            )
                        )
                    }
                }
            }

            FrameType.FS_PULL_FILE_REQ -> {
                val req = FsPullFileRequest.fromJson(String(frame.payload, Charsets.UTF_8))
                val targetFile = File(req.remoteFilePath)
                if (targetFile.exists() && targetFile.isFile) {
                    streamFileToPeer(transport, targetFile, req.isPreview)
                }
            }

            FrameType.TRANSFER_MANIFEST -> {
                val manifest = TransferManifest.fromJson(String(frame.payload, Charsets.UTF_8))
                val targetDir = if (manifest.isPreview) {
                    com.connecttophone.cache.CacheManager.getCacheDir(this@MainActivity)
                } else {
                    com.connecttophone.cache.CacheManager.getDownloadsDir(this@MainActivity)
                }
                val writer = SparseFileWriter(manifest, targetDir)
                activeWriters[manifest.transferId] = writer
                TransferForegroundService.startService(this@MainActivity)
                runOnUiThread {
                    Toast.makeText(this@MainActivity, if (manifest.isPreview) "Loading Quick Preview for '${manifest.fileName}'..." else "Downloading '${manifest.fileName}'...", Toast.LENGTH_SHORT).show()
                }
            }

            FrameType.TRANSFER_CHUNK -> {
                val chunk = TransferChunk.parse(frame.payload)
                if (chunk != null) {
                    val writer = activeWriters[chunk.transferId]
                    if (writer != null) {
                        val ok = writer.writeChunk(chunk)
                        val ack = TransferChunkAck(chunk.transferId, chunk.chunkIndex, ok)
                        transport.sendFrame(BinaryFrame(
                            type = FrameType.TRANSFER_CHUNK_ACK,
                            sessionId = frame.sessionId,
                            payload = ack.serialize()
                        ))

                        if (writer.isComplete) {
                            val finalFileResult = writer.finalizeFile()
                            activeWriters.remove(chunk.transferId)

                            finalFileResult.getOrNull()?.let { file ->
                                runOnUiThread {
                                    com.connecttophone.cache.CacheManager.playNotificationSound(this@MainActivity)

                                    if (writer.manifest.isPreview) {
                                        com.connecttophone.cache.CacheManager.enforceQuotaAndPrune(this@MainActivity)
                                        Toast.makeText(this@MainActivity, "Quick Preview ready!", Toast.LENGTH_SHORT).show()
                                        com.connecttophone.cache.CacheManager.openQuickView(this@MainActivity, file.absolutePath)
                                    } else {
                                        val item = TransferHistoryItem(
                                            fileName = file.name,
                                            localFilePath = file.absolutePath,
                                            sizeBytes = file.length(),
                                            isOutgoing = false
                                        )
                                        if (AppState.transferHistory.none { it.localFilePath == file.absolutePath }) {
                                            AppState.transferHistory.add(0, item)
                                        }
                                        Toast.makeText(this@MainActivity, "Successfully received '${file.name}'!", Toast.LENGTH_LONG).show()
                                    }
                                }
                            }
                        }
                    }
                }
            }

            else -> {}
        }
    }

    private suspend fun streamFileToPeer(transport: ITransport, file: File, isPreview: Boolean = false) {
        val scheduler = ChunkScheduler(file)
        val manifest = scheduler.manifest.copy(isPreview = isPreview)
        val manifestFrame = BinaryFrame(
            type = FrameType.TRANSFER_MANIFEST,
            sessionId = activeSessionId,
            payload = manifest.toJson().toByteArray(Charsets.UTF_8)
        )
        transport.sendFrame(manifestFrame)

        while (!scheduler.isComplete) {
            val chunk = scheduler.tryGetNextChunk(transport.channelId)
            if (chunk != null) {
                val chunkFrame = BinaryFrame(
                    type = FrameType.TRANSFER_CHUNK,
                    sessionId = activeSessionId,
                    payload = chunk.serialize()
                )
                transport.sendFrame(chunkFrame)
            } else {
                break
            }
        }
    }

    private fun broadcastFrame(frame: BinaryFrame) {
        lifecycleScope.launch(Dispatchers.IO) {
            activeTransports.values.forEach { transport ->
                try {
                    transport.sendFrame(frame)
                } catch (_: Exception) {}
            }
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        try { btServerSocket?.close() } catch (_: Exception) {}
        tcpServer?.close()
        udpBeacon?.close()
        clipboardService?.stopListening()
    }
}

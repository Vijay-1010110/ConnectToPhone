package com.connecttophone.transport

import com.connecttophone.protocol.DeviceType
import com.connecttophone.protocol.TransportType
import kotlinx.coroutines.*
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

data class DiscoveredPeer(
    val deviceId: String,
    val deviceName: String,
    val deviceType: DeviceType,
    val tcpPort: Int,
    val supportedTransports: Long,
    var remoteIpAddress: String = "",
    var lastSeen: Long = System.currentTimeMillis()
)

class UdpDiscoveryBeacon(
    val deviceId: String,
    val deviceName: String,
    val tcpPort: Int = 42424
) : AutoCloseable {

    companion object {
        const val DISCOVERY_PORT = 42425
    }

    private var socket: DatagramSocket? = null
    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private var isRunning = false

    var onPeerDiscovered: ((DiscoveredPeer) -> Unit)? = null

    fun start() {
        if (isRunning) return
        isRunning = true

        scope.launch {
            try {
                socket = DatagramSocket(DISCOVERY_PORT).apply {
                    broadcast = true
                    reuseAddress = true
                }

                launch { listenLoop() }
                launch { broadcastLoop() }
            } catch (_: Exception) {
            }
        }
    }

    private suspend fun listenLoop() = withContext(Dispatchers.IO) {
        val buffer = ByteArray(2048)
        val packet = DatagramPacket(buffer, buffer.size)

        while (isActive && isRunning) {
            try {
                socket?.receive(packet)
                val jsonStr = String(packet.data, 0, packet.length, Charsets.UTF_8)
                val obj = JSONObject(jsonStr)
                val peerDeviceId = obj.optString("deviceId", "")

                if (peerDeviceId.isNotEmpty() && peerDeviceId != deviceId) {
                    val peer = DiscoveredPeer(
                        deviceId = peerDeviceId,
                        deviceName = obj.optString("deviceName", "Unknown"),
                        deviceType = DeviceType.fromByte(obj.optInt("deviceType", 0).toByte()),
                        tcpPort = obj.optInt("tcpPort", 42424),
                        supportedTransports = obj.optLong("transports", 0L),
                        remoteIpAddress = packet.address.hostAddress ?: "",
                        lastSeen = System.currentTimeMillis()
                    )
                    onPeerDiscovered?.invoke(peer)
                }
            } catch (_: Exception) {}
        }
    }

    private suspend fun broadcastLoop() = withContext(Dispatchers.IO) {
        val obj = JSONObject().apply {
            put("deviceId", deviceId)
            put("deviceName", deviceName)
            put("deviceType", DeviceType.ANDROID.value.toInt())
            put("tcpPort", tcpPort)
            put("transports", TransportType.WIFI_LAN or TransportType.USB_ADB)
        }
        val bytes = obj.toString().toByteArray(Charsets.UTF_8)
        val broadcastAddress = InetAddress.getByName("255.255.255.255")
        val packet = DatagramPacket(bytes, bytes.size, broadcastAddress, DISCOVERY_PORT)

        while (isActive && isRunning) {
            try {
                socket?.send(packet)
            } catch (_: Exception) {}
            delay(2000)
        }
    }

    override fun close() {
        isRunning = false
        try {
            scope.cancel()
            socket?.close()
        } catch (_: Exception) {}
    }
}

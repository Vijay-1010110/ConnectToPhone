package com.connecttophone.transport

import com.connecttophone.protocol.TransportType
import kotlinx.coroutines.*
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket

class TcpServer(
    val port: Int = DEFAULT_PORT
) : AutoCloseable {

    companion object {
        const val DEFAULT_PORT = 42424
    }

    private var serverSocket: ServerSocket? = null
    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private var isListening = false

    var onClientConnected: ((TcpSocketTransport) -> Unit)? = null

    fun start() {
        if (isListening) return
        isListening = true

        scope.launch {
            try {
                serverSocket = ServerSocket(port)
                while (isActive && isListening) {
                    val socket = serverSocket!!.accept()
                    val transport = TcpSocketTransport(socket, TransportType.WIFI_LAN)
                    onClientConnected?.invoke(transport)
                }
            } catch (_: Exception) {
            }
        }
    }

    suspend fun connectToPeer(host: String, targetPort: Int = DEFAULT_PORT): Result<TcpSocketTransport> = withContext(Dispatchers.IO) {
        try {
            val socket = Socket()
            socket.tcpNoDelay = true
            socket.connect(InetSocketAddress(host, targetPort), 2500)
            val transportType = if (host == "127.0.0.1") TransportType.USB_ADB else TransportType.WIFI_LAN
            val transport = TcpSocketTransport(socket, transportType)
            Result.success(transport)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    override fun close() {
        isListening = false
        try {
            scope.cancel()
            serverSocket?.close()
        } catch (_: Exception) {}
    }
}

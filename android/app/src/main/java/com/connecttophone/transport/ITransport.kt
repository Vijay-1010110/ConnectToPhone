package com.connecttophone.transport

import com.connecttophone.protocol.BinaryFrame

interface ITransport : AutoCloseable {
    val channelId: String
    val type: Long
    val isConnected: Boolean
    val currentSpeedBytesPerSec: Double

    suspend fun sendFrame(frame: BinaryFrame)

    var onFrameReceived: ((ITransport, BinaryFrame) -> Unit)?
    var onDisconnected: ((ITransport) -> Unit)?
}

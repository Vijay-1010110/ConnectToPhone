package com.connecttophone.protocol

enum class FrameType(val value: Byte) {
    UNKNOWN(0),

    // Core & Handshake (0x01 - 0x0F)
    HANDSHAKE_SYN(0x01),
    HANDSHAKE_ACK(0x02),
    HEARTBEAT_PING(0x03),
    HEARTBEAT_PONG(0x04),
    DISCONNECT(0x05),

    // Clipboard (0x10 - 0x1F)
    CLIPBOARD_SYNC(0x10),

    // Virtual File System (0x20 - 0x2F)
    FS_LIST_DIR_REQ(0x20),
    FS_LIST_DIR_RESP(0x21),
    FS_PULL_FILE_REQ(0x22),
    FS_PUSH_FILE_INIT(0x23),

    // High Speed Transfer (0x30 - 0x3F)
    TRANSFER_MANIFEST(0x30),
    TRANSFER_CHUNK(0x31),
    TRANSFER_CHUNK_ACK(0x32),
    TRANSFER_PAUSE(0x33),
    TRANSFER_RESUME(0x34),
    TRANSFER_COMPLETE(0x35),

    // Screen Mirroring & Display Extension (0x40 - 0x4F)
    VIDEO_STREAM_CONFIG(0x40),
    VIDEO_FRAME_PACKET(0x41),
    TOUCH_INPUT_EVENT(0x42),

    // Remote Utility Control (0x50 - 0x5F)
    CTRL_MOUSE(0x50),
    CTRL_KEYBOARD(0x51),
    CTRL_MEDIA(0x52),
    CTRL_POWER(0x53),
    CTRL_APP_LAUNCH(0x54);

    companion object {
        fun fromByte(value: Byte): FrameType = entries.find { it.value == value } ?: UNKNOWN
    }
}

enum class DeviceType(val value: Byte) {
    WINDOWS(0),
    ANDROID(1),
    MACOS(2),
    LINUX(3),
    IOS(4);

    companion object {
        fun fromByte(value: Byte): DeviceType = entries.find { it.value == value } ?: ANDROID
    }
}

object TransportType {
    const val NONE: Long = 0
    const val WIFI_LAN: Long = 1L shl 0
    const val WIFI_DIRECT: Long = 1L shl 1
    const val USB_ADB: Long = 1L shl 2
    const val USB_AOA: Long = 1L shl 3
    const val USB_TETHER: Long = 1L shl 4
    const val BLUETOOTH_BLE: Long = 1L shl 5
    const val BLUETOOTH_RFCOMM: Long = 1L shl 6
}

enum class ClipboardFormat(val value: Byte) {
    PLAIN_TEXT(1),
    HTML(2),
    IMAGE_BLOB(3),
    FILE_LIST(4);

    companion object {
        fun fromByte(value: Byte): ClipboardFormat = entries.find { it.value == value } ?: PLAIN_TEXT
    }
}

enum class FsEntryType(val value: Byte) {
    FILE(0),
    DIRECTORY(1),
    DRIVE(2);

    companion object {
        fun fromByte(value: Byte): FsEntryType = entries.find { it.value == value } ?: FILE
    }
}

enum class FileCategory(val value: Byte) {
    GENERIC(0),
    FOLDER(1),
    IMAGE(2),
    VIDEO(3),
    AUDIO(4),
    DOCUMENT(5),
    ARCHIVE(6),
    APK(7),
    CODE(8);

    companion object {
        fun fromByte(value: Byte): FileCategory = entries.find { it.value == value } ?: GENERIC
    }
}

namespace ConnectToPhone.Core.Protocol;

public enum FrameType : byte
{
    Unknown = 0,

    // Core & Handshake (0x01 - 0x0F)
    HandshakeSyn = 0x01,
    HandshakeAck = 0x02,
    HeartbeatPing = 0x03,
    HeartbeatPong = 0x04,
    Disconnect = 0x05,

    // Clipboard (0x10 - 0x1F)
    ClipboardSync = 0x10,

    // Virtual File System (0x20 - 0x2F)
    FsListDirReq = 0x20,
    FsListDirResp = 0x21,
    FsPullFileReq = 0x22,
    FsPushFileInit = 0x23,

    // High Speed Transfer (0x30 - 0x3F)
    TransferManifest = 0x30,
    TransferChunk = 0x31,
    TransferChunkAck = 0x32,
    TransferPause = 0x33,
    TransferResume = 0x34,
    TransferComplete = 0x35,

    // Screen Mirroring & Display Extension (0x40 - 0x4F)
    VideoStreamConfig = 0x40,
    VideoFramePacket = 0x41,
    TouchInputEvent = 0x42,

    // Remote Utility Control (0x50 - 0x5F)
    CtrlMouse = 0x50,
    CtrlKeyboard = 0x51,
    CtrlMedia = 0x52,
    CtrlPower = 0x53,
    CtrlAppLaunch = 0x54
}

public enum DeviceType : byte
{
    Windows = 0,
    Android = 1,
    MacOS = 2,
    Linux = 3,
    Ios = 4
}

[Flags]
public enum TransportType : uint
{
    None = 0,
    WifiLan = 1 << 0,
    WifiDirect = 1 << 1,
    UsbAdb = 1 << 2,
    UsbAoa = 1 << 3,
    UsbTether = 1 << 4,
    BluetoothBle = 1 << 5,
    BluetoothRfcomm = 1 << 6
}

public enum ClipboardFormat : byte
{
    PlainText = 1,
    Html = 2,
    ImageBlob = 3,
    FileList = 4
}

public enum FsEntryType : byte
{
    File = 0,
    Directory = 1,
    Drive = 2
}

public enum FileCategory : byte
{
    Generic = 0,
    Folder = 1,
    Image = 2,
    Video = 3,
    Audio = 4,
    Document = 5,
    Archive = 6,
    Apk = 7,
    Code = 8
}

# Development Roadmap & Project Tracker

This file tracks the implementation milestones, task checklist, and project state for **ConnectToPhone**.

---

## 📌 Phase 1: Shared Protocol & Project Scaffolding
- [x] Comprehensive Architecture Blueprint ([ARCHITECTURE.md](ARCHITECTURE.md))
- [x] Network Wire Protocol Specification ([PROTOCOL_SPEC.md](PROTOCOL_SPEC.md))
- [x] Extensibility Design for Mirror, Extend, & Remote ([MODULES_AND_EXTENSIONS.md](MODULES_AND_EXTENSIONS.md))
- [x] Protocol definitions in `protocol/` (Protobuf schemas: `frame.proto`, `handshake.proto`, `transfer.proto`, `filesystem.proto`, `clipboard.proto`, `control.proto`)
- [x] Android native workspace in `android/` (Gradle 9.1, Kotlin 2.2, Jetpack Compose, Material 3, Android 36 SDK)
- [x] Windows native workspace in `desktop/` (.NET 9 C# solution, modern Fluent WPF, 6 modular class libraries)

---

## 📌 Phase 2: Binary Framing & Core Transfer Engine
- [x] High-performance binary frame codec (16-byte header with `0xCAFE` magic marker, version, frame type, session ID, length, payload)
- [x] Hardware-accelerated CRC32-C / CRC32 integrity checksums on both Windows and Android
- [x] Non-blocking sparse random-access file writer (`SparseFileWriter`) on Windows (`RandomAccess.Write`) and Android (`FileChannel.write`)
- [x] Dynamic chunk scheduler (`ChunkScheduler`): 2 MB chunk partitioning, XxHash64/CRC32 chunk validation, multi-path queue, and automatic disconnect failover re-queueing
- [x] SHA-256 whole-file cryptographic integrity validation

---

## 📌 Phase 3: Transports Implementation (Wi-Fi, USB, Bluetooth)
- [x] **Wi-Fi Transport**:
  - `TcpServer` & `TcpSocketTransport` high-speed non-blocking TCP streaming on both platforms
  - `UdpDiscoveryBeacon` zero-configuration peer discovery broadcasting on UDP port `42425`
- [x] **USB Transport**:
  - `AdbBridgeTransport`: Automatic ADB device detection and port forwarding (`adb forward tcp:42426 tcp:42424`) providing 80 - 200+ MB/s wired USB transfers
- [x] **Dynamic Multi-Path Failover**:
  - Unacknowledged inflight chunks automatically re-route to surviving channels if USB or Wi-Fi drops

---

## 📌 Phase 4: Single-Device Bidirectional Remote Explorer
- [x] **Windows File Provider**:
  - `WindowsFileSystemHost`: lists drives (`C:\`, `D:\`), special folders (Desktop, Downloads, Documents, Pictures, Videos), and directories
  - File category classification (Image, Video, Audio, Document, Archive, Apk, Code, Generic)
- [x] **Android File Provider**:
  - `AndroidFileSystemHost`: queries internal storage volumes and public media directories
- [x] **Android "PC Explorer" UI**:
  - Native Compose file browser on phone: browse PC folders and tap "Download to Phone" directly from mobile without touching laptop
- [x] **Windows "Phone Explorer" UI**:
  - Native Windows phone browser: browse phone media, double-click directories, tap "Download to PC", and drag-and-drop PC files to phone

---

## 📌 Phase 5: Notifications & Live Speed Displays
- [x] **Android Top Bar / Status Bar Speed Display**:
  - `TransferForegroundService`: Android Foreground Service with Ongoing Notification
  - Live aggregate speed meter (e.g. `⚡ 153.0 MB/s • USB + Wi-Fi`), progress bar, ETA, and Pause/Cancel actions
- [x] **Windows Taskbar & System Tray Integration**:
  - Live progress bar directly on the app's taskbar icon via `TaskbarItemInfo.ProgressValue` and `TaskbarItemProgressState`
  - Top header bonded speed indicators (`[USB: 110.2 MB/s]`, `[Wi-Fi: 42.8 MB/s]`, `⚡ 153.0 MB/s`)
  - Pause and Cancel transfer controls

---

## 📌 Phase 6: Instant Clipboard Synchronization
- [x] Android `ClipboardSyncService`: listens via `ClipboardManager.OnPrimaryClipChangedListener` with CRC32 deduplication to avoid infinite echo loops
- [x] Windows `ClipboardService`: Win32 `AddClipboardFormatListener` / `WM_CLIPBOARDUPDATE` hook with XxHash64 deduplication
- [x] Bidirectional injection without echo bounce

---

## 📌 Phase 7: Model Context Protocol (MCP) Server
- [x] Embedded JSON-RPC MCP server in `ConnectToPhone.Mcp`
- [x] Standard tools exposed to AI assistants:
  - `phone_get_status()`
  - `phone_list_files(path)`
  - `phone_download_file(phone_path, local_path)`
  - `phone_upload_file(local_path, phone_path)`
  - `phone_get_clipboard()`
  - `phone_set_clipboard(text)`
  - `phone_show_notification(title, message)`

---

## 📌 Phase 8: Future Pluggable Modules
- [x] Modular extension architecture ([MODULES_AND_EXTENSIONS.md](MODULES_AND_EXTENSIONS.md))
- [x] Frame type allocations for Video (`0x40 - 0x4F`), Touch/Mouse/Media/Power (`0x50 - 0x5F`)
- [x] Android PC Remote control tab (Media Play/Pause, Next/Prev, Volume, Lock PC, Sleep PC)
- [x] Windows Extension tab previews for Screen Mirroring and Virtual Display Extension

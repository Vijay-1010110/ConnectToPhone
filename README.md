# ConnectToPhone ⚡

**ConnectToPhone** is an ultra-fast, multi-protocol, bidirectional file sharing, clipboard sync, and remote management suite linking **Android** and **Windows** with zero compromises on performance.

Built completely natively:
- 📱 **Android**: Native Kotlin, Jetpack Compose, Coroutines, Java NIO / Ktor Sockets, Foreground Service with live top-bar speed meter.
- 💻 **Windows**: Native C# .NET 9/10, Fluent Modern UI, `System.IO.Pipelines`, Win32 taskbar progress & tray monitoring, WinRT Bluetooth, WinUSB, and embedded MCP (Model Context Protocol) server.

---

## 🌟 Key Highlights

- **⚡ Multi-Protocol Bonding**: Transmits chunks in parallel across **Wi-Fi (LAN / Wi-Fi Direct)**, **USB (AOA / ADB Bridge / RNDIS)**, and **Bluetooth (BLE + RFCOMM)** simultaneously to maximize total bandwidth.
- **🔄 True Bidirectional Autonomy ("Single Device Control")**:
  - Pull files from PC directly to Phone without touching your laptop.
  - Pull files from Phone directly to PC without touching your phone.
  - Browse remote directories, preview media, and stream remote files.
- **🛡️ Chunk-Level Failover & Zero-Loss Resume**: Slices files into deterministic 2 MB hashed chunks. If USB or Wi-Fi is unplugged mid-transfer, chunks automatically divert to surviving channels without dropping or restarting the transfer.
- **📊 Live Speed & System Notifications**:
  - **Phone**: Real-time speed display in the Android status bar (`⚡ 128 MB/s | USB + Wi-Fi`), transfer progress bar, and quick pause/cancel notification actions.
  - **Windows**: Native taskbar icon progress bar (`TaskbarItemInfo`), live tray speed tooltip, and Windows Notification Center toasts.
- **📋 Instant Clipboard Sync**: Sub-10ms bidirectional clipboard synchronization for text, links, and image clips.
- **🤖 Built-in MCP Server**: Exposes standard Model Context Protocol tools (`phone_list_files`, `phone_pull_file`, `phone_push_file`, `phone_get_clipboard`, etc.) so AI assistants can directly interact with the connected device.
- **🔮 Extensible Foundation**: Architected from day one for future modules:
  - Screen Mirroring & Secondary Display Extension (low-latency H.264/HEVC/AV1)
  - Remote PC & Phone Utility Control (Trackpad/Mouse, Media & Volume, Power states, Quick Actions)

---

## 📂 Project Structure

```
ConnectToPhone/
├── android/                   # Fully native Android project (Kotlin / Compose)
│   ├── app/
│   │   └── src/main/java/com/connecttophone/
│   │       ├── ui/            # Jetpack Compose UI
│   │       ├── service/       # Foreground Transfer Service
│   │       ├── engine/        # Chunk Scheduler & Reassembly
│   │       ├── transport/     # Wi-Fi, USB, Bluetooth
│   │       ├── explorer/      # Remote PC File Browser
│   │       └── clipboard/     # Clipboard Listener
├── desktop/                   # Fully native Windows project (.NET 9 C#)
│   ├── src/
│   │   ├── ConnectToPhone.App/        # Windows Fluent UI, Tray & Taskbar
│   │   ├── ConnectToPhone.Core/       # System.IO.Pipelines & Chunk Scheduler
│   │   ├── ConnectToPhone.Transports/ # Winsock, WinUSB, WinRT Bluetooth
│   │   ├── ConnectToPhone.Explorer/   # Windows file host & Phone browser
│   │   ├── ConnectToPhone.Clipboard/  # Win32 clipboard hooks
│   │   └── ConnectToPhone.Mcp/        # Model Context Protocol server
├── protocol/                  # Shared wire protocol specifications & schemas
│   ├── proto/                 # Protocol Buffers / Message schemas
│   └── PROTOCOL_SPEC.md       # Binary framing & handshake specification
└── docs/                      # Comprehensive engineering documentation
    ├── ARCHITECTURE.md        # Deep dive into system architecture & layers
    ├── PROTOCOL_SPEC.md       # Network wire format & chunk bonding
    ├── ROADMAP.md             # Development milestones & tracker
    └── MODULES_AND_EXTENSIONS.md # Extensibility guide (Mirror, Remote, etc.)
```

---

## 🚀 Quick Reference
- Read [ARCHITECTURE.md](docs/ARCHITECTURE.md) for detailed subsystem architecture.
- Read [PROTOCOL_SPEC.md](docs/PROTOCOL_SPEC.md) for binary framing and chunk scheduling.
- Read [ROADMAP.md](docs/ROADMAP.md) for current implementation progress and milestones.
- Read [MODULES_AND_EXTENSIONS.md](docs/MODULES_AND_EXTENSIONS.md) for how Screen Mirroring and Remote Utility Control are designed.

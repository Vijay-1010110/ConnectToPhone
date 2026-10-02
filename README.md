# ConnectToWindow & ConnectToPhone ⚡

**ConnectToWindow** is an ultra-fast, multi-protocol, bidirectional file sharing, real-time clipboard sync, and thumb-ergonomic remote control suite linking **Android** and **Windows** with zero compromises on performance.

Built completely natively:
- 📱 **Android Client (`ConnectToWindow`)**: Native Kotlin, Jetpack Compose, Coroutines, Java NIO Sockets, Foreground Service with live top-bar speed meter, and thumb-zone remote touchpad.
- 💻 **Windows Desktop Suite (`ConnectToPhone`)**: Native C# .NET 9, Fluent Modern UI, `System.IO.Pipelines`, Win32 taskbar progress & tray monitoring, 32-bit/64-bit RFCOMM Bluetooth bridge (`InTheHand.Net.Bluetooth`), Win32 input simulator, and embedded MCP server.

---

## 📥 Direct Downloads (v1.1.0)

Get the latest production-ready builds directly from this repository:

| Platform | Package | Version | Size | Direct Download Link |
| :--- | :--- | :--- | :--- | :--- |
| 📱 **Android** | `APK` (Universal) | `v1.1.0` (Build 101) | ~13.2 MB | [⬇ **Download ConnectToWindow APK**](https://github.com/Vijay-1010110/ConnectToPhone/raw/main/dist/ConnectToWindow-v1.1.0.apk) |
| 💻 **Windows 10/11** | `ZIP` (Portable x64) | `v1.1.0` (Build 101) | ~878 KB | [⬇ **Download Windows Portable Suite**](https://github.com/Vijay-1010110/ConnectToPhone/raw/main/dist/ConnectToWindow-Desktop-v1.1.0-win-x64.zip) |

> [!TIP]
> **GitHub Releases**: You can also browse release assets and tag details on the [GitHub Releases Page](https://github.com/Vijay-1010110/ConnectToPhone/releases).

### Verification & Checksums (SHA-256)
- **`ConnectToWindow-v1.1.0.apk`**:
  `D80D5BCF44F74C60ABDD867795CFB5176A34D975ACF2B15D95F5FE41147CBA45`
- **`ConnectToWindow-Desktop-v1.1.0-win-x64.zip`**:
  `9159D297074D6CACFBF7FD5E621A83F06E5E54B5C7BFED71040157FF7F2A8E97`

---

## 📌 Windows Start Menu Pinning & Desktop Access

When you install or extract the Windows app:
1. **Direct Launch**: Run `ConnectToPhone.App.exe` directly from the extracted directory.
2. **Start Menu Access**:
   - A verified Windows shortcut is placed at:
     ```text
     %APPDATA%\Microsoft\Windows\Start Menu\Programs\ConnectToWindow.lnk
     ```
   - Press the **`Windows`** key on your keyboard, type **`ConnectToWindow`**, and hit Enter.
3. **Pinning to Start Menu**:
   - In the Windows Start Menu, search for **ConnectToWindow**.
   - Right-click the app icon and select **📌 Pin to Start** or **📌 Pin to Taskbar**.
   - A desktop shortcut is also conveniently available at `%USERPROFILE%\Desktop\ConnectToWindow.lnk`.

---

## 🌟 Key Highlights

- **⚡ Multi-Protocol Bonding**: Transmits chunks in parallel across **Wi-Fi (LAN / Wi-Fi Direct)**, **USB (ADB Bridge / Reverse Tunnel)**, and **Bluetooth (RFCOMM SPP)** simultaneously to maximize total bandwidth (up to 140+ MB/s over USB 3.0).
- **🔄 True Bidirectional Autonomy ("Single Device Control")**:
  - Pull files from PC directly to Phone without touching your laptop.
  - Pull files from Phone directly to PC without touching your phone.
  - Browse remote PC drives (`C:\`, `D:\`), preview media, and stream remote files.
- **🛡️ Chunk-Level Failover & Zero-Loss Resume**: Slices files into deterministic 2 MB hashed chunks. If USB or Wi-Fi is unplugged mid-transfer, chunks automatically divert to surviving channels without dropping or restarting the transfer.
- **🎮 Ergonomic Thumb-Zone Remote Controller**:
  - **Large Thumb Trackpad**: Precision cursor gliding optimized for thumb reach.
  - **Dedicated Vertical Scroll Strip**: Continuous scrolling strip (`▲ ↕ ▼`) right beside the trackpad.
  - **3-Button Mouse**: Distinct `Left Click`, `🔘 Middle`, and `Right Click` buttons beneath the touchpad.
  - **Horizontal Scroll Wheel**: Dedicated horizontal swipe strip (`◀ Horizontal Scroll Wheel ▶`).
  - **Collapsible Hotkeys**: Expandable drawer with `Alt+Tab`, `Win+D`, `F5`, `Ctrl+C`, `Ctrl+V`, `Ctrl+Z`, `Enter`, `Backspace`, `Esc`, `Space`.
  - **Media & Power Bar**: Track skip, volume controls, mute, PC lock workstation, and sleep actions.
- **📋 Real-Time Bidirectional Clipboard Sync**:
  - Sub-10ms synchronization between Windows clipboard and Android clipboard.
  - Live clipboard history log with 1-tap copy back to device clipboard.
  - Notification audio feedback upon arrival.
- **📊 Live Speed & System Notifications**:
  - **Phone**: Real-time speed display in the Android status bar (`⚡ 140 MB/s | USB + Wi-Fi`), transfer progress bar, and quick pause/cancel notification actions.
  - **Windows**: Native taskbar icon progress bar (`TaskbarItemInfo`), live tray speed tooltip, and Windows Notification Center toasts.
- **🤖 Built-in MCP Server**: Exposes standard Model Context Protocol tools (`phone_list_files`, `phone_pull_file`, `phone_push_file`, `phone_get_clipboard`, etc.) so AI assistants can directly interact with the connected device.

---

## 📂 Project Structure

```
ConnectToPhone/
├── android/                   # Fully native Android project (Kotlin / Compose)
│   ├── app/
│   │   └── src/main/java/com/connecttophone/
│   │       ├── ui/            # Jetpack Compose UI (ConnectToWindow)
│   │       ├── service/       # Foreground Transfer & Speed Notification Service
│   │       ├── engine/        # Chunk Scheduler & Reassembly
│   │       ├── transport/     # Wi-Fi, USB, Bluetooth RFCOMM
│   │       ├── explorer/      # Remote PC File Browser & Quick View Cache
│   │       └── clipboard/     # Bidirectional Clipboard Sync
├── desktop/                   # Fully native Windows project (.NET 9 C#)
│   ├── src/
│   │   ├── ConnectToPhone.App/        # Windows Fluent UI, Tray & Win32 Input Simulator
│   │   ├── ConnectToPhone.Core/       # Pipelines & Multi-Path Chunk Scheduler
│   │   ├── ConnectToPhone.Transports/ # Winsock, USB ADB Tunnel, BluetoothBridge (RFCOMM)
│   │   ├── ConnectToPhone.Explorer/   # Windows file host & Phone browser
│   │   ├── ConnectToPhone.Clipboard/  # Win32 clipboard hooks
│   │   └── ConnectToPhone.Mcp/        # Model Context Protocol server
├── dist/                      # Pre-packaged direct downloadable releases
│   ├── ConnectToWindow-v1.1.0.apk
│   └── ConnectToWindow-Desktop-v1.1.0-win-x64.zip
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

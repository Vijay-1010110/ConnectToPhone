# ConnectToPhone — Version & Release Tracking Matrix

This document maintains the synchronized version numbers, build counters, patch details, and feature progression across both the Windows Desktop and Android native codebases.

---

## Current Version Summary

| Platform | Version | Version Code / Build | Protocol Compatibility | Release Stage |
| :--- | :--- | :--- | :--- | :--- |
| **Android Native App** | `1.1.0` | `101` | Protocol V1 (CAFE-01) | Production Ready |
| **Windows Desktop App** | `1.1.0` | `1.1.0.101` | Protocol V1 (CAFE-01) | Production Ready |
| **Protocol Specification**| `1.0` | `CAFE-01` | Fixed 16B Header + CRC32-C | Universal Transport |

---

## Release Notes & Patch History

### [1.1.0] — Build 101 (2026-10-02)
#### ✨ Major Features & Enhancements
1. **Live Preview / Ephemeral In-Memory & LRU Cache Engine**:
   - Instant "Quick View" (👁️) of remote images, documents, audio, and small media without permanent storage.
   - Intelligent LRU auto-eviction when cache reaches quota limit (default 200MB Phone / 500MB PC).
   - Periodic daily cleanup task evicting stale temporary files (> 24 hours).
   - Dedicated Cache Management UI:
     - Storage space breakdown (Total Device Storage, Available Free Space, Current Cache Usage).
     - Category filters: `All`, `Images 🖼️`, `Documents 📄`, `Media 🎬`, `Other 📦`.
     - Individual cache deletion (🗑️) and One-Tap "Clear All Cache" (🧹).
   - **Zero C-Drive Wear Constraint**: Windows Desktop cache and downloads strictly default to non-C storage (`D:\ConnectToPhone\Cache` and `D:\ConnectToPhone\Downloads`) with customizable folder selectors.
2. **Adaptive Single-Pipe Power Saver vs. Multi-Path Turbo Burst**:
   - **Idle / Normal Mode**: Only uses one primary high-speed protocol (Hardware USB if linked, Wi-Fi fallback) for mouse, keyboard, clipboard, and lightweight files (< 10MB). Secondary pipes remain in deep standby (0.0% CPU, zero battery drain).
   - **Multi-Path Turbo Burst**: Automatically bonds secondary links (Wi-Fi + USB concurrently) only for large files (> 10MB) or heavy streaming.
   - Dynamic UI badge showing active link status (`[⚡ USB (Active)]`, `[📶 Wi-Fi (Standby)]`).
3. **Sound Notifications on Task Completion**:
   - Audio feedback on Android (`RingtoneManager` notification sound) and Windows (`SystemSounds.Asterisk`) upon file transfer, remote clip sync, and cache clear.
4. **Mobile App Rebranding ("ConnectToWindow")**:
   - Mobile app renamed to **ConnectToWindow** across launcher, status bar, and UI headers.
5. **Vector Drawables & SVG Asset System**:
   - Converted icons from PNG to scalable SVG and Android Vector XML drawables (`app_icon.svg`, `ic_connect_window.xml`, `ic_launcher_foreground.xml`, `ic_qs_tile.xml`).
6. **Android Quick Settings (Control Panel) Tile**:
   - System notification pull-down toggle tile (`QuickConnectTileService`) enabling one-tap connection and live status inspection from anywhere in Android.
7. **Android Home Screen Widgets**:
   - **Link Status & Quick Launch Widget** (`ConnectStatusWidgetProvider`): Displays live PC connection badge and direct 1-tap shortcuts to Connect, PC Files, and Clipboard.
   - **PC Remote & Media Widget** (`RemoteControlWidgetProvider`): Directly triggers Play/Pause, Next/Prev, Volume Up/Down, and PC Lock without opening the app.
8. **Ergonomic Thumb-Zone Remote Layout**:
   - Re-engineered trackpad position to align with the lower thumb zone of the display for natural one-handed reach.
   - Left Click, Middle Mouse Click (🔘), and Right Click positioned directly below trackpad.
   - Horizontal Scroll Strip positioned at the very bottom below click buttons.
9. **Git Version Control & Repository Setup**:
   - Official remote origin registered: `https://github.com/Vijay-1010110/ConnectToPhone.git`.

---

### [1.0.0] — Build 100 (2026-10-02)
#### Initial Release
- Multi-path architecture: USB ADB (140+ MB/s), Wi-Fi LAN (TCP 42424), UDP Discovery Beacon.
- Zero-loss sparse file chunking engine with CRC32-C integrity and mid-stream failover recovery.
- Bidirectional remote filesystem exploration (Windows drives `C:\`, `D:\`, special folders).
- Real-time bidirectional clipboard auto-synchronization.
- Android status bar foreground speed meter and Windows taskbar progress indicator.
- Win32 virtual mouse and keyboard controller.
- Device pairing and PIN verification security module.

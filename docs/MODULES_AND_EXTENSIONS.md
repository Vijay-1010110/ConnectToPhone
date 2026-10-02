# Modular Extensions: Screen Mirror, Virtual Display, & Remote Utility Control

This document outlines how ConnectToPhone's underlying high-performance, multi-protocol engine seamlessly scales to support future modules: **Screen Mirroring**, **Virtual Display Extension**, and **Remote Utility Control**.

---

## 1. Extension Architecture

Every advanced feature is treated as an independent **Pluggable Feature Module** on top of the unified binary framing layer:

```
+------------------------------------------------------------------------------------+
|                         PLUGGABLE FEATURE MODULES                                  |
|   +-----------------------+  +-----------------------+  +----------------------+   |
|   | Screen Mirror Engine  |  | Virtual Display Extend|  | Remote Control & Util|   |
|   | (H.264/HEVC/AV1 NAL)  |  | (Windows Virtual Iddx)|  | (Mouse, Media, Power)|   |
|   +-----------------------+  +-----------------------+  +----------------------+   |
|              |                           |                          |              |
+--------------+---------------------------+--------------------------+--------------+
|                    UNIFIED MULTIPLEX & FRAMING ENGINE                              |
|   Frame Type 0x40 - 0x4F (Video) | Frame Type 0x50 - 0x5F (Input/Control)          |
+------------------------------------------------------------------------------------+
|                 MULTI-PROTOCOL TRANSPORT ABSTRACTION LAYER                         |
|   Wi-Fi LAN (TCP/UDP)   |   USB Cable (AOA/ADB)   |   Bluetooth (RFCOMM/BLE)       |
+------------------------------------------------------------------------------------+
```

---

## 2. Module 1: Low-Latency Screen Mirroring (`0x40` - `0x47`)

### 2.1 Concept
- **Phone $\rightarrow$ PC**: Mirror Android screen onto Windows desktop with interactive touch/mouse forwarding (similar to Scrcpy, but integrated natively).
- **PC $\rightarrow$ Phone**: Stream Windows desktop/game directly to the phone screen with ultra-low latency.

### 2.2 Pipeline Implementation
1. **Capture & Hardware Encoding**:
   - **Android**: `MediaProjection` API + `MediaCodec` hardware encoder (H.264/HEVC) outputting raw NAL units.
   - **Windows**: Windows Desktop Duplication API (DirectX 11 / DXGI) + Intel QuickSync / NVIDIA NVENC / AMD AMF hardware encoder via Media Foundation.
2. **Streaming Protocol**:
   - Frame `0x40` **VIDEO_STREAM_HEADER**: Width, Height, FPS, Codec (H.264 / HEVC / AV1).
   - Frame `0x41` **VIDEO_FRAME_PACKET**: PTS (Presentation Timestamp), Keyframe flag, NAL unit bytes.
   - Frame `0x42` **TOUCH_INPUT_EVENT**: Forwarding mouse click, drag, scroll, touch gestures with normalized $(X, Y)$ coordinates.
3. **Transport Optimization**:
   - Over USB: <15ms latency at 60-120 FPS.
   - Over Wi-Fi: 25-45ms latency with adaptive bitrate throttling based on RTT.

---

## 3. Module 2: Virtual Secondary Display Extension (`0x48` - `0x4F`)

### 3.1 Concept
Turns your Android phone or tablet into a **real physical secondary monitor** for Windows! Move your cursor seamlessly across your laptop screen onto your phone screen, drag browser windows, IDE tools, or chats onto your phone.

### 3.2 Implementation Strategy
1. **Windows Virtual Display Driver**:
   - Implemented via the official Windows `Indirect Display Driver (IDD)` (IddCx) architecture.
   - Windows natively recognizes a new monitor (e.g. 1920x1080 @ 60Hz or native phone resolution).
2. **Video Streaming Pipe**:
   - Windows desktop automatically renders to the virtual display buffer.
   - The virtual monitor frames are hardware-encoded into video frames and transmitted over ConnectToPhone's USB or Wi-Fi channel.
3. **Android Renderer & Touch Digitizer**:
   - Android decodes via `MediaCodec` directly into a `SurfaceView`.
   - Android touchscreen functions as a Windows precision touch digitizer / stylus input.

---

## 4. Module 3: Remote Utility Control & PC Remote (`0x50` - `0x5F`)

### 4.1 Concept
Operate your Windows PC remotely from your phone (or vice versa):
- **Virtual Trackpad & Mouse**: Touchpad surface on phone controlling Windows cursor, left/right clicks, two-finger scrolling, pinch-to-zoom.
- **Media Remote**: Play, Pause, Next, Previous, Volume slider, Mute (integrating with Windows System Media Transport Controls - SMTC).
- **Presentation Mode**: Next/Prev slide for PowerPoint / PDF, laser pointer overlay.
- **Power Controls**: Lock Screen, Sleep, Restart, Shutdown PC with confirmation prompt.
- **Application Launcher**: Quick tiles on phone to launch favorite apps (e.g. Browser, VS Code, Spotify) or run custom PowerShell commands.

### 4.2 Control Frame Registry
- `0x50` **CTRL_INPUT_MOUSE**: `[DeltaX: int16][DeltaY: int16][Buttons: uint8][WheelDelta: int8]`
- `0x51` **CTRL_INPUT_KEY**: `[VirtualKey: uint16][Modifiers: uint8][State: uint8]`
- `0x52` **CTRL_MEDIA_COMMAND**: `[Action: uint8 (Play, Pause, VolUp, VolDown, Mute)]`
- `0x53` **CTRL_POWER_ACTION**: `[Action: uint8 (Lock, Sleep, Restart, Shutdown)]`
- `0x54` **CTRL_APP_LAUNCH**: `[AppId: UTF-8 string]`

---

## 5. Module 4: Audio Streaming & Mic Forwarding (`0x60` - `0x6F`)

- Stream PC audio to phone (use phone as wireless headphones).
- Use phone microphone as a high-quality wireless Windows microphone for calls or recording.
- Uses Opus audio codec at 48kHz with 10ms frame sizes for imperceptible latency.

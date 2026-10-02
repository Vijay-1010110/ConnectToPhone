using System.Runtime.InteropServices;
using ConnectToPhone.Core.Protocol.Models;

namespace ConnectToPhone.App;

public static class Win32InputSimulator
{
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x01000;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const byte VK_BACK = 0x08;
    private const byte VK_TAB = 0x09;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12; // Alt
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_SPACE = 0x20;
    private const byte VK_LWIN = 0x5B;
    private const byte VK_F5 = 0x74;

    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_STOP = 0xB2;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("Powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private static bool _wasLeftDown;
    private static bool _wasRightDown;

    public static void HandleMouse(MouseControlPayload p)
    {
        try { System.IO.File.AppendAllText(@"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log", $"[Input] Mouse dx={p.DeltaX}, dy={p.DeltaY}, left={p.LeftClick}, right={p.RightClick}, mid={p.MiddleClick}, w={p.WheelDelta}\n"); } catch {}

        // 1. Movement
        if (p.DeltaX != 0 || p.DeltaY != 0)
        {
            mouse_event(MOUSEEVENTF_MOVE, p.DeltaX, p.DeltaY, 0, UIntPtr.Zero);
        }

        // 2. Clicks & Holds
        if (p.LeftClick)
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            _wasLeftDown = false;
        }
        else if (p.LeftButtonDown && !_wasLeftDown)
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            _wasLeftDown = true;
        }
        else if (!p.LeftButtonDown && _wasLeftDown)
        {
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            _wasLeftDown = false;
        }

        if (p.RightClick)
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
            _wasRightDown = false;
        }
        else if (p.RightButtonDown && !_wasRightDown)
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            _wasRightDown = true;
        }
        else if (!p.RightButtonDown && _wasRightDown)
        {
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
            _wasRightDown = false;
        }

        if (p.MiddleClick)
        {
            mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
        }

        // 3. Scroll Wheels
        if (p.WheelDelta != 0)
        {
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, p.WheelDelta, UIntPtr.Zero);
        }

        if (p.WheelDeltaX != 0)
        {
            mouse_event(MOUSEEVENTF_HWHEEL, 0, 0, p.WheelDeltaX, UIntPtr.Zero);
        }
    }

    public static void HandleKeyboard(KeyboardControlPayload p)
    {
        try { System.IO.File.AppendAllText(@"d:\Antigravity projects\ConnectToPhone\app_lifecycle.log", $"[Input] Keyboard text='{p.Text}', key='{p.SpecialKey}'\n"); } catch {}

        // 1. Special Keys & Hotkeys
        if (!string.IsNullOrEmpty(p.SpecialKey))
        {
            switch (p.SpecialKey.ToUpperInvariant())
            {
                case "ALT_TAB":
                    SendHotkey(VK_MENU, VK_TAB);
                    break;
                case "WIN_D":
                    SendHotkey(VK_LWIN, 0x44); // 'D'
                    break;
                case "COPY":
                    SendHotkey(VK_CONTROL, 0x43); // 'C'
                    break;
                case "PASTE":
                    SendHotkey(VK_CONTROL, 0x56); // 'V'
                    break;
                case "UNDO":
                    SendHotkey(VK_CONTROL, 0x5A); // 'Z'
                    break;
                case "ENTER":
                    SendKeyPress(VK_RETURN);
                    break;
                case "BACKSPACE":
                    SendKeyPress(VK_BACK);
                    break;
                case "ESC":
                    SendKeyPress(VK_ESCAPE);
                    break;
                case "SPACE":
                    SendKeyPress(VK_SPACE);
                    break;
                case "TAB":
                    SendKeyPress(VK_TAB);
                    break;
                case "F5":
                    SendKeyPress(VK_F5);
                    break;
            }
        }

        // 2. Direct Unicode Text Input
        if (!string.IsNullOrEmpty(p.Text))
        {
            foreach (char c in p.Text)
            {
                keybd_event(0, (byte)c, KEYEVENTF_UNICODE, UIntPtr.Zero);
                keybd_event(0, (byte)c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }
    }

    public static void HandleMedia(MediaAction action)
    {
        byte vk = action switch
        {
            MediaAction.PlayPause => VK_MEDIA_PLAY_PAUSE,
            MediaAction.NextTrack => VK_MEDIA_NEXT_TRACK,
            MediaAction.PrevTrack => VK_MEDIA_PREV_TRACK,
            MediaAction.VolumeUp => VK_VOLUME_UP,
            MediaAction.VolumeDown => VK_VOLUME_DOWN,
            MediaAction.MuteToggle => VK_VOLUME_MUTE,
            _ => 0
        };

        if (vk != 0)
        {
            SendKeyPress(vk);
        }
    }

    public static void HandlePower(PowerAction action)
    {
        switch (action)
        {
            case PowerAction.Lock:
                LockWorkStation();
                break;
            case PowerAction.Sleep:
                SetSuspendState(false, true, true);
                break;
        }
    }

    private static void SendKeyPress(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void SendHotkey(byte modVk, byte keyVk)
    {
        keybd_event(modVk, 0, 0, UIntPtr.Zero);
        keybd_event(keyVk, 0, 0, UIntPtr.Zero);
        keybd_event(keyVk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(modVk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}

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
    private const byte VK_PAUSE = 0x13;
    private const byte VK_CAPITAL = 0x14; // Caps Lock
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_SPACE = 0x20;
    private const byte VK_PRIOR = 0x21; // Page Up
    private const byte VK_NEXT = 0x22; // Page Down
    private const byte VK_END = 0x23;
    private const byte VK_HOME = 0x24;
    private const byte VK_LEFT = 0x25;
    private const byte VK_UP = 0x26;
    private const byte VK_RIGHT = 0x27;
    private const byte VK_DOWN = 0x28;
    private const byte VK_SNAPSHOT = 0x2C; // Print Screen
    private const byte VK_INSERT = 0x2D;
    private const byte VK_DELETE = 0x2E;
    private const byte VK_LWIN = 0x5B;

    private const byte VK_F1 = 0x70;
    private const byte VK_F2 = 0x71;
    private const byte VK_F3 = 0x72;
    private const byte VK_F4 = 0x73;
    private const byte VK_F5 = 0x74;
    private const byte VK_F6 = 0x75;
    private const byte VK_F7 = 0x76;
    private const byte VK_F8 = 0x77;
    private const byte VK_F9 = 0x78;
    private const byte VK_F10 = 0x79;
    private const byte VK_F11 = 0x7A;
    private const byte VK_F12 = 0x7B;

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
        // 1. Special Keys & Hotkeys
        if (!string.IsNullOrEmpty(p.SpecialKey))
        {
            switch (p.SpecialKey.ToUpperInvariant())
            {
                // Combinations
                case "ALT_TAB":
                    SendHotkey(VK_MENU, VK_TAB);
                    break;
                case "WIN_D":
                    SendHotkey(VK_LWIN, 0x44); // 'D'
                    break;
                case "WIN_E":
                    SendHotkey(VK_LWIN, 0x45); // 'E'
                    break;
                case "WIN_R":
                    SendHotkey(VK_LWIN, 0x52); // 'R'
                    break;
                case "WIN_L":
                    LockWorkStation();
                    break;
                case "ALT_F4":
                    SendHotkey(VK_MENU, VK_F4);
                    break;
                case "COPY":
                case "CTRL_C":
                    SendHotkey(VK_CONTROL, 0x43); // 'C'
                    break;
                case "PASTE":
                case "CTRL_V":
                    SendHotkey(VK_CONTROL, 0x56); // 'V'
                    break;
                case "CUT":
                case "CTRL_X":
                    SendHotkey(VK_CONTROL, 0x58); // 'X'
                    break;
                case "UNDO":
                case "CTRL_Z":
                    SendHotkey(VK_CONTROL, 0x5A); // 'Z'
                    break;
                case "REDO":
                case "CTRL_Y":
                    SendHotkey(VK_CONTROL, 0x59); // 'Y'
                    break;
                case "SELECT_ALL":
                case "CTRL_A":
                    SendHotkey(VK_CONTROL, 0x41); // 'A'
                    break;
                case "SAVE":
                case "CTRL_S":
                    SendHotkey(VK_CONTROL, 0x53); // 'S'
                    break;
                case "FIND":
                case "CTRL_F":
                    SendHotkey(VK_CONTROL, 0x46); // 'F'
                    break;
                case "TASK_MGR":
                case "CTRL_SHIFT_ESC":
                    keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    break;

                // Function Keys
                case "F1": SendKeyPress(VK_F1); break;
                case "F2": SendKeyPress(VK_F2); break;
                case "F3": SendKeyPress(VK_F3); break;
                case "F4": SendKeyPress(VK_F4); break;
                case "F5": SendKeyPress(VK_F5); break;
                case "F6": SendKeyPress(VK_F6); break;
                case "F7": SendKeyPress(VK_F7); break;
                case "F8": SendKeyPress(VK_F8); break;
                case "F9": SendKeyPress(VK_F9); break;
                case "F10": SendKeyPress(VK_F10); break;
                case "F11": SendKeyPress(VK_F11); break;
                case "F12": SendKeyPress(VK_F12); break;

                // Navigation & Editing
                case "ENTER": SendKeyPress(VK_RETURN); break;
                case "BACKSPACE": SendKeyPress(VK_BACK); break;
                case "ESC": SendKeyPress(VK_ESCAPE); break;
                case "SPACE": SendKeyPress(VK_SPACE); break;
                case "TAB": SendKeyPress(VK_TAB); break;
                case "DELETE":
                case "DEL": SendKeyPress(VK_DELETE); break;
                case "INSERT":
                case "INS": SendKeyPress(VK_INSERT); break;
                case "HOME": SendKeyPress(VK_HOME); break;
                case "END": SendKeyPress(VK_END); break;
                case "PAGE_UP":
                case "PGUP": SendKeyPress(VK_PRIOR); break;
                case "PAGE_DOWN":
                case "PGDN": SendKeyPress(VK_NEXT); break;

                // Directional Arrows
                case "UP":
                case "ARROW_UP": SendKeyPress(VK_UP); break;
                case "DOWN":
                case "ARROW_DOWN": SendKeyPress(VK_DOWN); break;
                case "LEFT":
                case "ARROW_LEFT": SendKeyPress(VK_LEFT); break;
                case "RIGHT":
                case "ARROW_RIGHT": SendKeyPress(VK_RIGHT); break;

                // System & Modifiers
                case "PRINTSCREEN":
                case "PRTSC": SendKeyPress(VK_SNAPSHOT); break;
                case "CAPS":
                case "CAPS_LOCK": SendKeyPress(VK_CAPITAL); break;
                case "WIN": SendKeyPress(VK_LWIN); break;
                case "CTRL": SendKeyPress(VK_CONTROL); break;
                case "ALT": SendKeyPress(VK_MENU); break;
                case "SHIFT": SendKeyPress(VK_SHIFT); break;
            }
        }

        // 2. Direct Unicode Text Input (Instant Typewriter)
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
